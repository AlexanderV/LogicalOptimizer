using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Reduced Ordered Binary Decision Diagram over a fixed (sorted) variable order,
///     with a shared unique table (hash-consing) and memoized ite. Because ROBDDs are
///     canonical, two expressions are equivalent exactly when they build to the same
///     node — ideal for repeated equivalence queries against one baseline. A node budget
///     turns pathological orderings into a clean exception instead of memory blowup
///     (callers can fall back to the SAT-based <c>EquivalenceChecker</c>).
///     <para>
///         The diagram uses CUDD-style <b>complement edges</b>: an "edge" is an int that
///         packs a node index and a complement bit (the low bit); a function and its
///         negation share the very same node, halving memory, and <see cref="Negate" /> is
///         an O(1) bit flip. There is a single terminal node (ONE); <c>FALSE</c> is the
///         complemented edge to it. The canonical invariant is that the THEN (high) edge of
///         every stored node is always regular (non-complemented); when a node would be
///         built with a complemented then-edge, both children are complemented and a
///         complemented edge to the normalized node is returned. That single rule keeps the
///         representation canonical, so equivalence is still edge equality.
///     </para>
/// </summary>
public sealed class BinaryDecisionDiagram
{
    public const int DefaultNodeBudget = 1_000_000;

    /// <summary>Thrown message marker when the node budget interrupts construction.</summary>
    internal const string NodeBudgetMessage = "BDD node budget exceeded";

    /// <summary>The single terminal node (ONE) lives at node index 0.</summary>
    private const int One = 0;

    // Edges, not node indices: edge = (nodeIndex << 1) | complementBit. The regular edge
    // to ONE is the constant-true function; its complement is the constant-false function.
    internal const int TrueNode = 0; // (One << 1) | 0
    internal const int FalseNode = 1; // (One << 1) | 1

    private readonly List<(int Variable, int Low, int High)> _nodes = new();
    private readonly Dictionary<(int Variable, int Low, int High), int> _uniqueTable = new();
    private readonly Dictionary<(int F, int G, int H), int> _iteCache = new();
    private readonly Dictionary<string, int> _variableIndex = new();
    private readonly List<string> _variables = new();
    private readonly int _nodeBudget;
    private readonly CancellationToken _cancellationToken;

    internal BinaryDecisionDiagram(IEnumerable<string> variables, int nodeBudget = DefaultNodeBudget,
        CancellationToken cancellationToken = default)
        : this(variables, sortVariables: true, nodeBudget, cancellationToken)
    {
    }

    /// <summary>
    ///     With <paramref name="sortVariables" /> false the given sequence IS the diagram's
    ///     variable order (top to bottom) — the order is the single biggest lever on BDD
    ///     size, see <see cref="BuildWithBestOrder" />.
    /// </summary>
    internal BinaryDecisionDiagram(IEnumerable<string> variables, bool sortVariables,
        int nodeBudget = DefaultNodeBudget, CancellationToken cancellationToken = default)
    {
        _nodeBudget = nodeBudget;
        _cancellationToken = cancellationToken;
        // The single terminal ONE occupies slot 0; its variable index sorts after every
        // real variable. FALSE is not a node — it is the complemented edge to ONE.
        _nodes.Add((int.MaxValue, 0, 0));

        var order = sortVariables ? variables.OrderBy(v => v) : variables;
        foreach (var name in order)
            if (_variableIndex.TryAdd(name, _variables.Count))
                _variables.Add(name);
    }

    public IReadOnlyList<string> Variables => _variables;

    /// <summary>Live node count including the single terminal.</summary>
    public int NodeCount => _nodes.Count;

    // ---- Complement-edge helpers -------------------------------------------------------
    // An edge packs a node index (high bits) and a complement flag (low bit). Node handles
    // and the complement bit are kept strictly separate through these functions so no
    // consumer ever inspects the raw layout directly.

    /// <summary>The regular (non-complemented) form of an edge.</summary>
    internal static int Regular(int edge)
    {
        return edge & ~1;
    }

    /// <summary>True when the edge carries the complement bit (its function is negated).</summary>
    internal static bool IsComplemented(int edge)
    {
        return (edge & 1) != 0;
    }

    /// <summary>Flip an edge's complement bit — this is exactly boolean negation, in O(1).</summary>
    internal static int Complement(int edge)
    {
        return edge ^ 1;
    }

    private static int MakeEdge(int nodeIndex, bool complemented)
    {
        return (nodeIndex << 1) | (complemented ? 1 : 0);
    }

    /// <summary>The node index an edge points at (the terminal ONE is index 0).</summary>
    private static int NodeOf(int edge)
    {
        return edge >> 1;
    }

    private static bool IsConstant(int edge)
    {
        return NodeOf(edge) == One;
    }

    /// <summary>Build a diagram for one expression in its own manager.</summary>
    public static BinaryDecisionDiagram Build(AstNode ast, int nodeBudget = DefaultNodeBudget,
        CancellationToken cancellationToken = default)
    {
        var manager = new BinaryDecisionDiagram(ast.GetVariables(), nodeBudget, cancellationToken);
        manager.Root = manager.FromAst(ast);
        return manager;
    }

    /// <summary>Root of the last <see cref="Build" />; only set by that factory.</summary>
    internal int Root { get; private set; } = FalseNode;

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

    /// <summary>Translate an expression into this manager; returns its edge.</summary>
    internal int FromAst(AstNode node)
    {
        switch (node)
        {
            case ConstantNode constant:
                return constant.Value ? TrueNode : FalseNode;
            case VariableNode variable:
                return MakeNode(_variableIndex[variable.Name], FalseNode, TrueNode);
            case NotNode not:
                return Negate(FromAst(not.Operand));
            case AndNode and:
                {
                    // Left-to-right fold over the n-ary operand list (operands are
                    // canonically sorted by the factory): acc & x = ite(acc, x, 0)
                    var result = TrueNode;
                    foreach (var operand in and.Operands)
                        result = Ite(result, FromAst(operand), FalseNode);
                    return result;
                }
            case OrNode or:
                {
                    // acc | x = ite(acc, 1, x)
                    var result = FalseNode;
                    foreach (var operand in or.Operands)
                        result = Ite(result, TrueNode, FromAst(operand));
                    return result;
                }
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

    /// <summary>Boolean negation: a single complement-bit flip (O(1) with complement edges).</summary>
    internal int Negate(int node)
    {
        return Complement(node);
    }

    internal bool IsTautology(int node)
    {
        return node == TrueNode;
    }

    internal bool IsContradiction(int node)
    {
        return node == FalseNode;
    }

    /// <summary>The built function is constant true.</summary>
    public bool IsTautology()
    {
        return IsTautology(Root);
    }

    /// <summary>The built function is constant false.</summary>
    public bool IsContradiction()
    {
        return IsContradiction(Root);
    }

    /// <summary>Number of satisfying assignments of the built function over all manager variables.</summary>
    public BigInteger CountSatisfyingAssignments()
    {
        return CountSatisfyingAssignments(Root);
    }

    /// <summary>Number of satisfying assignments over all manager variables.</summary>
    internal BigInteger CountSatisfyingAssignments(int node)
    {
        var memo = new Dictionary<int, BigInteger>();
        // SatCount is over levels VariableLevel(node)..n-1; multiply back in the free
        // variables above the root (levels 0..VariableLevel(node)-1).
        return SatCount(node, memo) * BigInteger.Pow(2, VariableLevel(node));
    }

    /// <summary>Evaluate the built function at one assignment (defaults missing variables to false).</summary>
    public bool Evaluate(IReadOnlyDictionary<string, bool> assignment)
    {
        return Evaluate(Root, assignment);
    }

    /// <summary>Evaluate the function at one assignment (defaults missing variables to false).</summary>
    internal bool Evaluate(int node, IReadOnlyDictionary<string, bool> assignment)
    {
        // Accumulate the complement parity along the path: the value at the terminal is
        // TRUE, flipped once for every complemented edge traversed (including the root).
        var complemented = IsComplemented(node);
        var index = NodeOf(node);
        while (index != One)
        {
            var (variable, low, high) = _nodes[index];
            var next = assignment.TryGetValue(_variables[variable], out var value) && value ? high : low;
            complemented ^= IsComplemented(next);
            index = NodeOf(next);
        }

        return !complemented;
    }

    /// <summary>
    ///     Lazily enumerate ALL total satisfying assignments in lexicographic variable
    ///     order (false before true). The count can be exponential — combine with
    ///     Take/TakeWhile or use <see cref="CountSatisfyingAssignments()" /> first.
    /// </summary>
    public IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateSatisfyingAssignments()
    {
        return EnumerateSatisfyingAssignments(Root);
    }

    internal IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateSatisfyingAssignments(int node)
    {
        return EnumerateFrom(node, 0, new bool[_variables.Count]);
    }

    private IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateFrom(int edge, int level, bool[] assignment)
    {
        // The constant-false function is uniquely the FALSE edge; prune it.
        if (edge == FalseNode) yield break;

        if (level == _variables.Count)
        {
            var snapshot = new Dictionary<string, bool>();
            for (var i = 0; i < _variables.Count; i++) snapshot[_variables[i]] = assignment[i];
            yield return snapshot;
            yield break;
        }

        var branchesHere = VariableLevel(edge) == level;
        foreach (var value in new[] { false, true })
        {
            assignment[level] = value;
            int child;
            if (branchesHere)
            {
                var (_, low, high) = _nodes[NodeOf(edge)];
                var raw = value ? high : low;
                child = IsComplemented(edge) ? Complement(raw) : raw;
            }
            else
            {
                child = edge; // variable absent from this path: both values allowed
            }

            foreach (var result in EnumerateFrom(child, level + 1, assignment))
                yield return result;
        }
    }

    /// <summary>
    ///     A total assignment satisfying the function. Any non-false node has one:
    ///     reduction collapses all-zero subgraphs into the false terminal itself.
    /// </summary>
    public IReadOnlyDictionary<string, bool> FindSatisfyingAssignment()
    {
        return FindSatisfyingAssignment(Root);
    }

    internal IReadOnlyDictionary<string, bool> FindSatisfyingAssignment(int node)
    {
        if (node == FalseNode)
            throw new InvalidOperationException("Function is unsatisfiable");

        var assignment = new Dictionary<string, bool>();
        var complemented = IsComplemented(node);
        var index = NodeOf(node);
        while (index != One)
        {
            var (variable, low, high) = _nodes[index];
            // Resolve both branches to absolute edges (folding in the accumulated
            // complement parity), then follow one that is not constant-false.
            var lowEdge = complemented ? Complement(low) : low;
            var takeLow = lowEdge != FalseNode;
            assignment[_variables[variable]] = !takeLow;

            var next = takeLow ? lowEdge : complemented ? Complement(high) : high;
            complemented = IsComplemented(next);
            index = NodeOf(next);
        }

        foreach (var name in _variables) assignment.TryAdd(name, false);
        return assignment;
    }

    /// <summary>Cofactor by a named variable: f with <paramref name="variable" /> pinned to <paramref name="value" />.</summary>
    internal int Restrict(int node, string variable, bool value)
    {
        var level = RequireVariable(variable);
        return RestrictLevel(node, level, value, new Dictionary<int, int>());
    }

    /// <summary>Existential quantification: ∃x.f = f|x=0 ∨ f|x=1, over one or more variables.</summary>
    internal int Exists(int node, params string[] variables)
    {
        foreach (var variable in variables)
        {
            var level = RequireVariable(variable);
            node = QuantifyLevel(node, level, universal: false, new Dictionary<int, int>());
        }

        return node;
    }

    /// <summary>Universal quantification: ∀x.f = f|x=0 ∧ f|x=1, over one or more variables.</summary>
    internal int ForAll(int node, params string[] variables)
    {
        foreach (var variable in variables)
        {
            var level = RequireVariable(variable);
            node = QuantifyLevel(node, level, universal: true, new Dictionary<int, int>());
        }

        return node;
    }

    /// <summary>Functional composition: f[x := g] = ite(g, f|x=1, f|x=0).</summary>
    internal int Compose(int node, string variable, int replacement)
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

    private int RestrictLevel(int edge, int level, bool value, Dictionary<int, int> memo)
    {
        if (IsConstant(edge) || VariableLevel(edge) > level) return edge;
        if (memo.TryGetValue(edge, out var cached)) return cached;

        var (variable, low, high) = _nodes[NodeOf(edge)];
        // Absolute child edges, with the current node's complement parity folded in.
        var lowEdge = IsComplemented(edge) ? Complement(low) : low;
        var highEdge = IsComplemented(edge) ? Complement(high) : high;

        var result = variable == level
            ? value ? highEdge : lowEdge
            : MakeNode(variable, RestrictLevel(lowEdge, level, value, memo),
                RestrictLevel(highEdge, level, value, memo));
        memo[edge] = result;
        return result;
    }

    private int QuantifyLevel(int edge, int level, bool universal, Dictionary<int, int> memo)
    {
        if (IsConstant(edge) || VariableLevel(edge) > level) return edge;
        if (memo.TryGetValue(edge, out var cached)) return cached;

        var (variable, low, high) = _nodes[NodeOf(edge)];
        var lowEdge = IsComplemented(edge) ? Complement(low) : low;
        var highEdge = IsComplemented(edge) ? Complement(high) : high;

        int result;
        if (variable == level)
            result = universal ? Ite(lowEdge, highEdge, FalseNode) : Ite(lowEdge, TrueNode, highEdge);
        else
            result = MakeNode(variable,
                QuantifyLevel(lowEdge, level, universal, memo),
                QuantifyLevel(highEdge, level, universal, memo));

        memo[edge] = result;
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
            case NaryNode nary:
                foreach (var operand in nary.Operands)
                    CollectAppearanceOrder(operand, order, seen);
                break;
            case BinaryNode binary:
                CollectAppearanceOrder(binary.Left, order, seen);
                CollectAppearanceOrder(binary.Right, order, seen);
                break;
        }
    }

    private BigInteger SatCount(int edge, Dictionary<int, BigInteger> memo)
    {
        if (IsConstant(edge))
            // TRUE edge satisfies its (empty) subspace once; FALSE edge never.
            return IsComplemented(edge) ? BigInteger.Zero : BigInteger.One;

        var node = NodeOf(edge);
        var regular = SatCountNode(node, memo);
        if (!IsComplemented(edge)) return regular;

        // A complemented edge represents the negated function; over the 2^k assignments to
        // the variables at levels VariableLevel(node)..n-1 the two counts are complementary.
        var level = _nodes[node].Variable;
        return BigInteger.Pow(2, _variables.Count - level) - regular;
    }

    private BigInteger SatCountNode(int node, Dictionary<int, BigInteger> memo)
    {
        if (memo.TryGetValue(node, out var cached)) return cached;

        var (variable, low, high) = _nodes[node];
        var count = SatCount(low, memo) * BigInteger.Pow(2, VariableLevel(low) - variable - 1) +
                    SatCount(high, memo) * BigInteger.Pow(2, VariableLevel(high) - variable - 1);
        memo[node] = count;
        return count;
    }

    /// <summary>Variable level of an edge's node; the terminal sits one past the last variable.</summary>
    private int VariableLevel(int edge)
    {
        var node = NodeOf(edge);
        return node == One ? _variables.Count : _nodes[node].Variable;
    }

    /// <summary>
    ///     Hash-cons a node and return an edge to it. Enforces the canonical invariant that
    ///     a stored node's THEN (high) edge is regular: if <paramref name="high" /> is
    ///     complemented, both children are complemented and a complemented edge is returned.
    /// </summary>
    private int MakeNode(int variable, int low, int high)
    {
        if (low == high) return low; // redundant test — reduction rule

        // Canonical normalization: the stored high edge must be regular.
        var complementResult = IsComplemented(high);
        if (complementResult)
        {
            low = Complement(low);
            high = Complement(high);
        }

        var key = (variable, low, high);
        if (!_uniqueTable.TryGetValue(key, out var index))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_nodes.Count >= _nodeBudget)
                throw new InvalidOperationException(NodeBudgetMessage);

            index = _nodes.Count;
            _nodes.Add(key);
            _uniqueTable[key] = index;
        }

        var edge = MakeEdge(index, complemented: false);
        return complementResult ? Complement(edge) : edge;
    }

    /// <summary>if-then-else: the single connective every boolean operation reduces to.</summary>
    internal int Ite(int f, int g, int h)
    {
        // Terminal cases (complement-edge aware).
        if (f == TrueNode) return g;
        if (f == FalseNode) return h;
        if (g == h) return g;
        if (g == TrueNode && h == FalseNode) return f;
        if (g == FalseNode && h == TrueNode) return Complement(f); // ite(f,0,1) = !f

        // Normalize for consistent memoization and canonical result complement:
        //   ite(!f, g, h) = ite(f, h, g)         — keep the test edge regular
        if (IsComplemented(f))
        {
            (g, h) = (h, g);
            f = Complement(f);
        }

        //   ite(f, g, h) = !ite(f, !g, !h)        — keep the then-value regular; the
        // result's complement bit is pulled out and re-applied after the recursion.
        var complementResult = false;
        if (IsComplemented(g))
        {
            g = Complement(g);
            h = Complement(h);
            complementResult = true;
        }

        var key = (f, g, h);
        if (_iteCache.TryGetValue(key, out var cached))
            return complementResult ? Complement(cached) : cached;

        var level = Math.Min(VariableLevel(f), Math.Min(VariableLevel(g), VariableLevel(h)));
        var low = Ite(Cofactor(f, level, false), Cofactor(g, level, false), Cofactor(h, level, false));
        var high = Ite(Cofactor(f, level, true), Cofactor(g, level, true), Cofactor(h, level, true));

        var result = MakeNode(level, low, high);
        _iteCache[key] = result;
        return complementResult ? Complement(result) : result;
    }

    private int Cofactor(int edge, int level, bool positive)
    {
        var node = NodeOf(edge);
        if (node == One) return edge; // terminal: both cofactors are the edge itself
        var (variable, low, high) = _nodes[node];
        if (variable != level) return edge; // does not branch on this variable

        var child = positive ? high : low;
        // The cofactor of a complemented edge is the complement of the child's cofactor.
        return IsComplemented(edge) ? Complement(child) : child;
    }
}
