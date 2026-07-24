using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Reduced Ordered Binary Decision Diagram over a fixed (sorted) variable order,
///     with a shared unique table (hash-consing) and memoized ite. Because ROBDDs are
///     canonical, two expressions are equivalent exactly when they build to the same
///     node — ideal for repeated equivalence queries against one baseline. A node budget
///     turns pathological orderings into a clean exception instead of memory blowup
///     (callers can fall back to the SAT-based <c>EquivalenceChecker</c>).
/// </summary>
public sealed class BinaryDecisionDiagram
{
    public const int DefaultNodeBudget = 1_000_000;

    /// <summary>Thrown message marker when the node budget interrupts construction.</summary>
    internal const string NodeBudgetMessage = "BDD node budget exceeded";

    public const int FalseNode = 0;
    public const int TrueNode = 1;

    private readonly List<(int Variable, int Low, int High)> _nodes = new();
    private readonly Dictionary<(int Variable, int Low, int High), int> _uniqueTable = new();
    private readonly Dictionary<(int F, int G, int H), int> _iteCache = new();
    private readonly Dictionary<string, int> _variableIndex = new();
    private readonly List<string> _variables = new();
    private readonly int _nodeBudget;
    private readonly CancellationToken _cancellationToken;

    public BinaryDecisionDiagram(IEnumerable<string> variables, int nodeBudget = DefaultNodeBudget,
        CancellationToken cancellationToken = default)
        : this(variables, sortVariables: true, nodeBudget, cancellationToken)
    {
    }

    /// <summary>
    ///     With <paramref name="sortVariables" /> false the given sequence IS the diagram's
    ///     variable order (top to bottom) — the order is the single biggest lever on BDD
    ///     size, see <see cref="BuildWithBestOrder" />.
    /// </summary>
    public BinaryDecisionDiagram(IEnumerable<string> variables, bool sortVariables,
        int nodeBudget = DefaultNodeBudget, CancellationToken cancellationToken = default)
    {
        _nodeBudget = nodeBudget;
        _cancellationToken = cancellationToken;
        // Terminals occupy slots 0 and 1; their variable index sorts after every real variable
        _nodes.Add((int.MaxValue, 0, 0));
        _nodes.Add((int.MaxValue, 1, 1));

        var order = sortVariables ? variables.OrderBy(v => v) : variables;
        foreach (var name in order)
            if (_variableIndex.TryAdd(name, _variables.Count))
                _variables.Add(name);
    }

    public IReadOnlyList<string> Variables => _variables;

    /// <summary>Live node count including the two terminals.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>Build a diagram for one expression in its own manager.</summary>
    public static BinaryDecisionDiagram Build(AstNode ast, int nodeBudget = DefaultNodeBudget,
        CancellationToken cancellationToken = default)
    {
        var manager = new BinaryDecisionDiagram(ast.GetVariables(), nodeBudget, cancellationToken);
        manager.Root = manager.FromAst(ast);
        return manager;
    }

    /// <summary>Root of the last <see cref="Build" />; only set by that factory.</summary>
    public int Root { get; private set; } = FalseNode;

    /// <summary>
    ///     Canonical equivalence: build both sides in one manager and compare roots.
    ///     Null when the node budget was exceeded (verdict unknown — fall back to SAT).
    /// </summary>
    public static bool? AreEquivalent(AstNode left, AstNode right, int nodeBudget = DefaultNodeBudget)
    {
        var variables = left.GetVariables();
        variables.UnionWith(right.GetVariables());
        var manager = new BinaryDecisionDiagram(variables, nodeBudget);

        try
        {
            return manager.FromAst(left) == manager.FromAst(right);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Translate an expression into this manager; returns its node.</summary>
    public int FromAst(AstNode node)
    {
        switch (node)
        {
            case ConstantNode constant:
                return constant.Value ? TrueNode : FalseNode;
            case VariableNode variable:
                return MakeNode(_variableIndex[variable.Name], FalseNode, TrueNode);
            case NotNode not:
                return Ite(FromAst(not.Operand), FalseNode, TrueNode);
            case AndNode and:
                return Ite(FromAst(and.Left), FromAst(and.Right), FalseNode);
            case OrNode or:
                return Ite(FromAst(or.Left), TrueNode, FromAst(or.Right));
            case XorNode xor:
                {
                    var right = FromAst(xor.Right);
                    return Ite(FromAst(xor.Left), Negate(right), right);
                }
            case EqvNode eqv:
                {
                    var right = FromAst(eqv.Right);
                    return Ite(FromAst(eqv.Left), right, Negate(right));
                }
            case NandNode nand:
                return Negate(Ite(FromAst(nand.Left), FromAst(nand.Right), FalseNode));
            case NorNode nor:
                return Negate(Ite(FromAst(nor.Left), TrueNode, FromAst(nor.Right)));
            case ImpNode imp:
                return Ite(FromAst(imp.Left), FromAst(imp.Right), TrueNode);
            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
    }

    public int Negate(int node)
    {
        return Ite(node, FalseNode, TrueNode);
    }

    public bool IsTautology(int node)
    {
        return node == TrueNode;
    }

    public bool IsContradiction(int node)
    {
        return node == FalseNode;
    }

    /// <summary>Number of satisfying assignments over all manager variables.</summary>
    public BigInteger CountSatisfyingAssignments(int node)
    {
        var memo = new Dictionary<int, BigInteger>();
        return SatCount(node, memo) * BigInteger.Pow(2, VariableLevel(node));
    }

    /// <summary>Evaluate the function at one assignment (defaults missing variables to false).</summary>
    public bool Evaluate(int node, IReadOnlyDictionary<string, bool> assignment)
    {
        while (node > TrueNode)
        {
            var (variable, low, high) = _nodes[node];
            node = assignment.TryGetValue(_variables[variable], out var value) && value ? high : low;
        }

        return node == TrueNode;
    }

    /// <summary>
    ///     Lazily enumerate ALL total satisfying assignments in lexicographic variable
    ///     order (false before true). The count can be exponential — combine with
    ///     Take/TakeWhile or use <see cref="CountSatisfyingAssignments" /> first.
    /// </summary>
    public IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateSatisfyingAssignments(int node)
    {
        return EnumerateFrom(node, 0, new bool[_variables.Count]);
    }

    private IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateFrom(int node, int level, bool[] assignment)
    {
        if (node == FalseNode) yield break;

        if (level == _variables.Count)
        {
            var snapshot = new Dictionary<string, bool>();
            for (var i = 0; i < _variables.Count; i++) snapshot[_variables[i]] = assignment[i];
            yield return snapshot;
            yield break;
        }

        var branchesHere = VariableLevel(node) == level;
        foreach (var value in new[] { false, true })
        {
            assignment[level] = value;
            var child = branchesHere
                ? value ? _nodes[node].High : _nodes[node].Low
                : node; // variable absent from this path: both values allowed
            foreach (var result in EnumerateFrom(child, level + 1, assignment))
                yield return result;
        }
    }

    /// <summary>
    ///     A total assignment satisfying the function. Any non-false node has one:
    ///     reduction collapses all-zero subgraphs into the false terminal itself.
    /// </summary>
    public IReadOnlyDictionary<string, bool> FindSatisfyingAssignment(int node)
    {
        if (node == FalseNode)
            throw new InvalidOperationException("Function is unsatisfiable");

        var assignment = new Dictionary<string, bool>();
        while (node > TrueNode)
        {
            var (variable, low, high) = _nodes[node];
            var takeLow = low != FalseNode;
            assignment[_variables[variable]] = !takeLow;
            node = takeLow ? low : high;
        }

        foreach (var name in _variables) assignment.TryAdd(name, false);
        return assignment;
    }

    /// <summary>Cofactor by a named variable: f with <paramref name="variable" /> pinned to <paramref name="value" />.</summary>
    public int Restrict(int node, string variable, bool value)
    {
        var level = RequireVariable(variable);
        return RestrictLevel(node, level, value, new Dictionary<int, int>());
    }

    /// <summary>Existential quantification: ∃x.f = f|x=0 ∨ f|x=1, over one or more variables.</summary>
    public int Exists(int node, params string[] variables)
    {
        foreach (var variable in variables)
        {
            var level = RequireVariable(variable);
            node = QuantifyLevel(node, level, universal: false, new Dictionary<int, int>());
        }

        return node;
    }

    /// <summary>Universal quantification: ∀x.f = f|x=0 ∧ f|x=1, over one or more variables.</summary>
    public int ForAll(int node, params string[] variables)
    {
        foreach (var variable in variables)
        {
            var level = RequireVariable(variable);
            node = QuantifyLevel(node, level, universal: true, new Dictionary<int, int>());
        }

        return node;
    }

    /// <summary>Functional composition: f[x := g] = ite(g, f|x=1, f|x=0).</summary>
    public int Compose(int node, string variable, int replacement)
    {
        var level = RequireVariable(variable);
        var memo = new Dictionary<int, int>();
        return Ite(replacement,
            RestrictLevel(node, level, true, memo),
            RestrictLevel(node, level, false, new Dictionary<int, int>()));
    }

    private int RequireVariable(string variable)
    {
        if (!_variableIndex.TryGetValue(variable, out var level))
            throw new ArgumentException($"Variable '{variable}' is not registered in this manager");
        return level;
    }

    private int RestrictLevel(int node, int level, bool value, Dictionary<int, int> memo)
    {
        if (node <= TrueNode || VariableLevel(node) > level) return node;
        if (memo.TryGetValue(node, out var cached)) return cached;

        var (variable, low, high) = _nodes[node];
        var result = variable == level
            ? value ? high : low
            : MakeNode(variable, RestrictLevel(low, level, value, memo),
                RestrictLevel(high, level, value, memo));
        memo[node] = result;
        return result;
    }

    private int QuantifyLevel(int node, int level, bool universal, Dictionary<int, int> memo)
    {
        if (node <= TrueNode || VariableLevel(node) > level) return node;
        if (memo.TryGetValue(node, out var cached)) return cached;

        var (variable, low, high) = _nodes[node];
        int result;
        if (variable == level)
        {
            result = universal ? Ite(low, high, FalseNode) : Ite(low, TrueNode, high);
        }
        else
        {
            var quantifiedLow = QuantifyLevel(low, level, universal, memo);
            var quantifiedHigh = QuantifyLevel(high, level, universal, memo);
            result = MakeNode(variable, quantifiedLow, quantifiedHigh);
        }

        memo[node] = result;
        return result;
    }

    /// <summary>
    ///     Build trying several variable-order heuristics (sorted, first-appearance DFS,
    ///     reversed) and keep the smallest diagram. The order is the dominant factor in
    ///     BDD size — e.g. an adder ordered a1,b1,a2,b2 is linear while a1,a2,b1,b2 is
    ///     exponential. Orders whose build exceeds the budget are skipped; throws only
    ///     when every candidate exceeds it.
    /// </summary>
    public static BinaryDecisionDiagram BuildWithBestOrder(AstNode ast, int nodeBudget = DefaultNodeBudget,
        CancellationToken cancellationToken = default)
    {
        var appearance = new List<string>();
        var seen = new HashSet<string>();
        CollectAppearanceOrder(ast, appearance, seen);

        var candidates = new List<List<string>>
        {
            appearance,
            appearance.OrderBy(v => v).ToList(),
            Enumerable.Reverse(appearance).ToList()
        };

        BinaryDecisionDiagram? best = null;
        foreach (var order in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Cap each attempt at the current best size — worse orders abort early
            var budget = best == null ? nodeBudget : Math.Min(nodeBudget, best.NodeCount);
            try
            {
                var manager = new BinaryDecisionDiagram(order, sortVariables: false, budget, cancellationToken);
                manager.Root = manager.FromAst(ast);
                if (best == null || manager.NodeCount < best.NodeCount) best = manager;
            }
            catch (InvalidOperationException)
            {
                // This order blew the budget; try the next one
            }
        }

        return best ?? throw new InvalidOperationException(NodeBudgetMessage);
    }

    /// <summary>
    ///     Sifting (Rudell-style, rebuild-based): starting from the best heuristic order,
    ///     each variable in turn is tried at every position and left where the diagram is
    ///     smallest; passes repeat until a full pass finds no improvement. Each candidate
    ///     build is capped at the current best size, so bad positions abort early. More
    ///     expensive than <see cref="BuildWithBestOrder" /> but finds orders the static
    ///     heuristics cannot; the rebuild count is bounded by
    ///     <paramref name="maxRebuilds" />.
    /// </summary>
    public static BinaryDecisionDiagram BuildWithSiftedOrder(AstNode ast, int nodeBudget = DefaultNodeBudget,
        int maxRebuilds = 400, CancellationToken cancellationToken = default)
    {
        var best = BuildWithBestOrder(ast, nodeBudget, cancellationToken);
        var order = best.Variables.ToList();
        var rebuilds = 0;

        for (var pass = 0; pass < 4; pass++)
        {
            var improved = false;
            foreach (var variable in order.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var basePosition = order.IndexOf(variable);
                for (var position = 0; position <= order.Count - 1; position++)
                {
                    if (position == basePosition) continue;
                    if (++rebuilds > maxRebuilds) return best;

                    var candidate = order.ToList();
                    candidate.RemoveAt(basePosition);
                    candidate.Insert(position, variable);
                    try
                    {
                        var manager = new BinaryDecisionDiagram(candidate, sortVariables: false,
                            Math.Min(nodeBudget, best.NodeCount), cancellationToken);
                        manager.Root = manager.FromAst(ast);
                        if (manager.NodeCount < best.NodeCount)
                        {
                            best = manager;
                            order = candidate;
                            basePosition = position;
                            improved = true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Worse than the current best before finishing — skip
                    }
                }
            }

            if (!improved) break;
        }

        return best;
    }

    private static void CollectAppearanceOrder(AstNode node, List<string> order, HashSet<string> seen)
    {
        switch (node)
        {
            case VariableNode variable:
                if (seen.Add(variable.Name)) order.Add(variable.Name);
                break;
            case NotNode not:
                CollectAppearanceOrder(not.Operand, order, seen);
                break;
            case BinaryNode binary:
                CollectAppearanceOrder(binary.Left, order, seen);
                CollectAppearanceOrder(binary.Right, order, seen);
                break;
        }
    }

    private BigInteger SatCount(int node, Dictionary<int, BigInteger> memo)
    {
        if (node == FalseNode) return BigInteger.Zero;
        if (node == TrueNode) return BigInteger.One;
        if (memo.TryGetValue(node, out var cached)) return cached;

        var (variable, low, high) = _nodes[node];
        var count = SatCount(low, memo) * BigInteger.Pow(2, VariableLevel(low) - variable - 1) +
                    SatCount(high, memo) * BigInteger.Pow(2, VariableLevel(high) - variable - 1);
        memo[node] = count;
        return count;
    }

    /// <summary>Variable level of a node; terminals sit one past the last variable.</summary>
    private int VariableLevel(int node)
    {
        return node <= TrueNode ? _variables.Count : _nodes[node].Variable;
    }

    private int MakeNode(int variable, int low, int high)
    {
        if (low == high) return low;

        var key = (variable, low, high);
        if (_uniqueTable.TryGetValue(key, out var existing)) return existing;

        _cancellationToken.ThrowIfCancellationRequested();
        if (_nodes.Count >= _nodeBudget)
            throw new InvalidOperationException(NodeBudgetMessage);

        var index = _nodes.Count;
        _nodes.Add(key);
        _uniqueTable[key] = index;
        return index;
    }

    /// <summary>if-then-else: the single connective every boolean operation reduces to.</summary>
    public int Ite(int f, int g, int h)
    {
        if (f == TrueNode) return g;
        if (f == FalseNode) return h;
        if (g == h) return g;
        if (g == TrueNode && h == FalseNode) return f;

        var key = (f, g, h);
        if (_iteCache.TryGetValue(key, out var cached)) return cached;

        var level = Math.Min(VariableLevel(f), Math.Min(VariableLevel(g), VariableLevel(h)));
        var low = Ite(Cofactor(f, level, false), Cofactor(g, level, false), Cofactor(h, level, false));
        var high = Ite(Cofactor(f, level, true), Cofactor(g, level, true), Cofactor(h, level, true));

        var result = MakeNode(level, low, high);
        _iteCache[key] = result;
        return result;
    }

    private int Cofactor(int node, int level, bool positive)
    {
        if (node <= TrueNode) return node;
        var (variable, low, high) = _nodes[node];
        if (variable != level) return node;
        return positive ? high : low;
    }
}
