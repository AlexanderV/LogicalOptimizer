using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Top-down decision-DNNF compiler (in the style of c2d / D4), producing a compact,
///     hash-consed d-DNNF DAG with component caching.
///     <para>
///         Design choice — <b>Tseitin projection, not direct NNF compilation.</b> The formula is
///         first turned into its full biconditional Tseitin CNF. That encoding is equisatisfiable
///         AND equi-count over the input variables: each satisfying input assignment extends to
///         exactly one assignment of the gate auxiliaries. Therefore the model count of the whole
///         CNF (over all its variables) already equals the model count of the original formula
///         over its inputs, so counting needs no projection at all, and enumeration only has to
///         drop the auxiliary variables (which are functionally determined). This is simpler and
///         less error-prone than re-deriving a smooth NNF over the original variables, and it is
///         verified exactly against the BDD oracle in the differential test suite.
///     </para>
///     <para>
///         The recursion keeps the circuit <b>smooth</b>: every call compiles a clause set over an
///         explicit variable scope and returns a node whose model scope is exactly that set —
///         assigned variables become literal nodes, variables that drop out of all clauses become
///         explicit free-choice decision nodes, and the rest are covered by components. As a
///         result the counter never needs a 2^gap correction.
///     </para>
/// </summary>
internal sealed class DnnfCompiler
{
    internal const string NodeBudgetMessage = "DNNF node budget exceeded";

    private readonly Dictionary<string, int> _andCache = new();
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, int> _componentCache = new();
    private readonly Dictionary<int, int> _literalCache = new();
    private readonly int _nodeBudget;
    private readonly List<DnnfNode> _nodes = new();
    private readonly Dictionary<(int Variable, int Low, int High), int> _orCache = new();
    private int _cancellationCounter;
    private readonly int _falseId;
    private readonly int _trueId;

    private DnnfCompiler(int nodeBudget, CancellationToken cancellationToken)
    {
        _nodeBudget = nodeBudget;
        _cancellationToken = cancellationToken;
        // Terminal sentinels occupy ids 0 and 1 and are never budgeted.
        _nodes.Add(new DnnfNode(DnnfKind.False, 0, Array.Empty<int>()));
        _nodes.Add(new DnnfNode(DnnfKind.True, 0, Array.Empty<int>()));
        _falseId = 0;
        _trueId = 1;
    }

    public static DnnfCircuit Compile(AstNode formula, int nodeBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (nodeBudget < 1) throw new ArgumentOutOfRangeException(nameof(nodeBudget));
        cancellationToken.ThrowIfCancellationRequested();

        var compiler = new DnnfCompiler(nodeBudget, cancellationToken);
        return compiler.Run(formula);
    }

    private DnnfCircuit Run(AstNode formula)
    {
        var cnf = TseitinConverter.Convert(formula);

        var clauses = new List<int[]>(cnf.Clauses.Count);
        var scope = new HashSet<int>();
        foreach (var clause in cnf.Clauses)
        {
            var copy = (int[])clause.Clone();
            clauses.Add(copy);
            foreach (var literal in copy) scope.Add(Math.Abs(literal));
        }

        // Tseitin always emits the root unit clause, so an empty clause set never occurs; a
        // constant-true formula still yields clauses over its pinned auxiliary. Guard anyway.
        var root = clauses.Count == 0 ? _trueId : Compile(clauses, scope);

        return new DnnfCircuit(_nodes.ToArray(), root, cnf.InputVariables, cnf.InputVariables.Count, _falseId);
    }

    /// <summary>
    ///     Compile a clause set responsible for exactly the variables in <paramref name="scope" />.
    ///     Returns a node whose model scope is precisely <paramref name="scope" />.
    /// </summary>
    private int Compile(List<int[]> clauses, HashSet<int> scope)
    {
        Tick();

        // 1. Unit propagation to a fixpoint.
        var assignment = new Dictionary<int, bool>();
        var current = clauses;
        while (true)
        {
            var unit = 0;
            var hasEmpty = false;
            foreach (var clause in current)
            {
                if (clause.Length == 0)
                {
                    hasEmpty = true;
                    break;
                }

                if (clause.Length == 1 && unit == 0) unit = clause[0];
            }

            if (hasEmpty) return _falseId;
            if (unit == 0) break;

            var variable = Math.Abs(unit);
            var value = unit > 0;
            if (assignment.TryGetValue(variable, out var existing) && existing != value) return _falseId;
            assignment[variable] = value;
            current = Restrict(current, variable, value);
        }

        // 2. Assigned literals become literal conjuncts.
        var conjuncts = new List<int>();
        foreach (var (variable, value) in assignment)
            conjuncts.Add(MakeLiteral(value ? variable : -variable));

        // 3. Variables still in scope but no longer mentioned are free (both polarities allowed).
        var residualVariables = new HashSet<int>();
        foreach (var clause in current)
            foreach (var literal in clause)
                residualVariables.Add(Math.Abs(literal));

        foreach (var variable in scope)
            if (!assignment.ContainsKey(variable) && !residualVariables.Contains(variable))
                conjuncts.Add(MakeFreeChoice(variable));

        // 4. Decompose the residual into connected components; each compiles independently
        //    (decomposable AND).
        if (current.Count > 0)
            foreach (var component in ConnectedComponents(current))
            {
                var compiled = CompileComponent(component);
                if (compiled == _falseId) return _falseId;
                conjuncts.Add(compiled);
            }

        return MakeAnd(conjuncts);
    }

    /// <summary>
    ///     Compile a single connected component (every variable of it appears in its clauses).
    ///     Cached by the normalized clause set — the source of the sub-exponential sharing.
    /// </summary>
    private int CompileComponent(List<int[]> component)
    {
        Tick();

        var key = Canonical(component);
        if (_componentCache.TryGetValue(key, out var cached)) return cached;

        var decision = ChooseDecisionVariable(component);
        var branchScope = new HashSet<int>();
        foreach (var clause in component)
            foreach (var literal in clause)
            {
                var variable = Math.Abs(literal);
                if (variable != decision) branchScope.Add(variable);
            }

        var low = Compile(Restrict(component, decision, false), new HashSet<int>(branchScope));
        var high = Compile(Restrict(component, decision, true), new HashSet<int>(branchScope));

        var node = MakeOr(decision, low, high);
        _componentCache[key] = node;
        return node;
    }

    // Assign a variable and simplify: drop satisfied clauses, remove the falsified literal.
    private static List<int[]> Restrict(List<int[]> clauses, int variable, bool value)
    {
        var result = new List<int[]>(clauses.Count);
        foreach (var clause in clauses)
        {
            var satisfied = false;
            var reduced = new List<int>(clause.Length);
            foreach (var literal in clause)
            {
                if (Math.Abs(literal) == variable)
                {
                    if (literal > 0 == value)
                    {
                        satisfied = true;
                        break;
                    }

                    continue; // falsified literal: drop it
                }

                reduced.Add(literal);
            }

            if (satisfied) continue;
            result.Add(reduced.ToArray()); // may be empty -> conflict, detected by the caller
        }

        return result;
    }

    private static int ChooseDecisionVariable(List<int[]> clauses)
    {
        // Most frequently occurring variable (ties broken by smallest index) — a cheap,
        // effective branching heuristic for structured CNF.
        var frequency = new Dictionary<int, int>();
        foreach (var clause in clauses)
            foreach (var literal in clause)
            {
                var variable = Math.Abs(literal);
                frequency[variable] = frequency.GetValueOrDefault(variable) + 1;
            }

        var best = 0;
        var bestCount = -1;
        foreach (var (variable, count) in frequency)
            if (count > bestCount || (count == bestCount && variable < best))
            {
                best = variable;
                bestCount = count;
            }

        return best;
    }

    private static List<List<int[]>> ConnectedComponents(List<int[]> clauses)
    {
        // Union-find over the variables sharing a clause; each class becomes a component.
        var parent = new Dictionary<int, int>();

        int Find(int x)
        {
            if (!parent.TryGetValue(x, out var p))
            {
                parent[x] = x;
                return x;
            }

            if (p == x) return x;
            var root = Find(p);
            parent[x] = root;
            return root;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        foreach (var clause in clauses)
        {
            var first = Math.Abs(clause[0]);
            Find(first);
            foreach (var literal in clause) Union(first, Math.Abs(literal));
        }

        var groups = new Dictionary<int, List<int[]>>();
        foreach (var clause in clauses)
        {
            var root = Find(Math.Abs(clause[0]));
            if (!groups.TryGetValue(root, out var list))
            {
                list = new List<int[]>();
                groups[root] = list;
            }

            list.Add(clause);
        }

        return groups.Values.ToList();
    }

    private static string Canonical(List<int[]> clauses)
    {
        var normalized = new List<int[]>(clauses.Count);
        foreach (var clause in clauses)
        {
            var copy = (int[])clause.Clone();
            Array.Sort(copy);
            normalized.Add(copy);
        }

        normalized.Sort(CompareClauses);

        var builder = new StringBuilder();
        foreach (var clause in normalized)
        {
            builder.Append('[');
            for (var i = 0; i < clause.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(clause[i]);
            }

            builder.Append(']');
        }

        return builder.ToString();
    }

    private static int CompareClauses(int[] a, int[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var comparison = a[i].CompareTo(b[i]);
            if (comparison != 0) return comparison;
        }

        return a.Length.CompareTo(b.Length);
    }

    private void Tick()
    {
        if ((_cancellationCounter++ & 0xFF) == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }

    private int AddNode(DnnfNode node)
    {
        if (_nodes.Count >= _nodeBudget)
            throw new NodeBudgetExceededException($"{NodeBudgetMessage} ({_nodeBudget})");
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    private int MakeLiteral(int literal)
    {
        if (_literalCache.TryGetValue(literal, out var id)) return id;
        id = AddNode(new DnnfNode(DnnfKind.Literal, literal, Array.Empty<int>()));
        _literalCache[literal] = id;
        return id;
    }

    // A free variable: a decision whose branches are both TRUE, so either polarity is a model.
    private int MakeFreeChoice(int variable)
    {
        return MakeOr(variable, _trueId, _trueId);
    }

    private int MakeOr(int variable, int low, int high)
    {
        // Both branches unsatisfiable -> the decision itself is unsatisfiable. Otherwise the
        // node is kept even if one branch is FALSE, because the decision variable stays in scope.
        if (low == _falseId && high == _falseId) return _falseId;

        var key = (variable, low, high);
        if (_orCache.TryGetValue(key, out var id)) return id;
        id = AddNode(new DnnfNode(DnnfKind.Or, variable, new[] { low, high }));
        _orCache[key] = id;
        return id;
    }

    private int MakeAnd(List<int> children)
    {
        var filtered = new List<int>(children.Count);
        foreach (var child in children)
        {
            if (child == _falseId) return _falseId;
            if (child == _trueId) continue; // TRUE has empty scope; drop it
            filtered.Add(child);
        }

        if (filtered.Count == 0) return _trueId;
        if (filtered.Count == 1) return filtered[0];

        filtered.Sort();
        var key = string.Join(",", filtered);
        if (_andCache.TryGetValue(key, out var id)) return id;
        id = AddNode(new DnnfNode(DnnfKind.And, 0, filtered.ToArray()));
        _andCache[key] = id;
        return id;
    }
}
