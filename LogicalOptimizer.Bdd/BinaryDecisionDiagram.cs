using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Reduced Ordered Binary Decision Diagram over a variable order, with a shared unique
///     table (hash-consing) and memoized ite. Because ROBDDs are canonical, two expressions
///     are equivalent exactly when they build to the same node — ideal for repeated
///     equivalence queries against one baseline. A node budget turns pathological orderings
///     into a clean exception instead of memory blowup (callers can fall back to the
///     SAT-based <c>EquivalenceChecker</c>).
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
///     <para>
///         Variable position is decoupled from variable identity: the stored
///         <c>Variable</c> field of a node is a stable variable id, and <see cref="_varLevel" />
///         maps each id to its current level (position, top to bottom). That indirection lets
///         <see cref="BuildWithSiftedOrder" /> reorder variables in place with adjacent-level
///         swaps instead of rebuilding the whole diagram.
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

    // Variable-order bookkeeping. _varLevel[id] is the current level (position) of the
    // variable whose id is `id`; _levelVar is its inverse. _nodesOfVar[id] lists every
    // node index that branches on that variable — the level-indexed enumeration an
    // adjacent-level swap needs (a node's level is _varLevel of its stored Variable id).
    private readonly int[] _varLevel;
    private readonly int[] _levelVar;
    private readonly List<int>[] _nodesOfVar;

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

        var count = _variables.Count;
        _varLevel = new int[count];
        _levelVar = new int[count];
        _nodesOfVar = new List<int>[count];
        for (var i = 0; i < count; i++)
        {
            _varLevel[i] = i; // identity order until sifting reorders it
            _levelVar[i] = i;
            _nodesOfVar[i] = new List<int>();
        }
    }

    /// <summary>Variable names in current top-to-bottom (level) order.</summary>
    public IReadOnlyList<string> Variables
    {
        get
        {
            var result = new string[_variables.Count];
            for (var level = 0; level < result.Length; level++)
                result[level] = _variables[_levelVar[level]];
            return result;
        }
    }

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
            for (var i = 0; i < _variables.Count; i++) snapshot[_variables[_levelVar[i]]] = assignment[i];
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
        if (!_variableIndex.TryGetValue(variable, out var id))
            throw new ArgumentException($"Variable '{variable}' is not registered in this manager");
        return _varLevel[id];
    }

    private int RestrictLevel(int edge, int level, bool value, Dictionary<int, int> memo)
    {
        if (IsConstant(edge) || VariableLevel(edge) > level) return edge;
        if (memo.TryGetValue(edge, out var cached)) return cached;

        var (variable, low, high) = _nodes[NodeOf(edge)];
        // Absolute child edges, with the current node's complement parity folded in.
        var lowEdge = IsComplemented(edge) ? Complement(low) : low;
        var highEdge = IsComplemented(edge) ? Complement(high) : high;

        var result = _varLevel[variable] == level
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
        if (_varLevel[variable] == level)
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
    ///     Sifting (Rudell-style, in place): starting from the best heuristic order, each
    ///     variable in turn is bubbled up to the top and down to the bottom using only
    ///     adjacent-level swaps and left where the diagram is smallest; passes repeat until
    ///     one finds no improvement. Unlike a rebuild, a single swap rewrites only the two
    ///     affected levels, so this is far cheaper than reconstructing the diagram per trial
    ///     position while finding orders the static heuristics cannot. The number of trial
    ///     swaps is bounded by <paramref name="maxRebuilds" />; the result is never larger
    ///     than <see cref="BuildWithBestOrder" />.
    /// </summary>
    public static BinaryDecisionDiagram BuildWithSiftedOrder(AstNode ast, int nodeBudget = DefaultNodeBudget,
        int maxRebuilds = 400, CancellationToken cancellationToken = default)
    {
        var manager = BuildWithBestOrder(ast, nodeBudget, cancellationToken);
        manager.Sift(maxRebuilds, cancellationToken);
        return manager;
    }

    /// <summary>
    ///     Reorder variables in place by Rudell sifting: each variable is moved through every
    ///     level with adjacent swaps and returned to its smallest-diagram position. Trial
    ///     swaps are capped by <paramref name="maxSwaps" />; each variable is sifted
    ///     atomically so the diagram is never left larger than where it started.
    /// </summary>
    private void Sift(int maxSwaps, CancellationToken cancellationToken)
    {
        var n = _variables.Count;
        if (n < 2) return;

        Collect();
        var swaps = 0;

        for (var pass = 0; pass < 4; pass++)
        {
            var sizeBeforePass = ReachableCount();

            for (var vid = 0; vid < n; vid++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (swaps >= maxSwaps)
                {
                    Collect();
                    return;
                }

                var level = _varLevel[vid];
                var bestLevel = level;
                var bestSize = NodeCount;

                // Bubble the variable up to the top, tracking the smallest diagram seen.
                // Collecting after every swap keeps dead nodes from inflating the size (and
                // the budget) and makes NodeCount the honest reachable-node metric.
                while (level > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SwapAdjacentLevels(level - 1);
                    Collect();
                    swaps++;
                    level--;
                    if (NodeCount < bestSize)
                    {
                        bestSize = NodeCount;
                        bestLevel = level;
                    }
                }

                // Then all the way down to the bottom, still tracking the best position.
                while (level < n - 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SwapAdjacentLevels(level);
                    Collect();
                    swaps++;
                    level++;
                    if (NodeCount < bestSize)
                    {
                        bestSize = NodeCount;
                        bestLevel = level;
                    }
                }

                // Return the variable to its best-found position (swaps are reversible, so
                // this reproduces exactly the smallest configuration seen).
                while (level > bestLevel)
                {
                    SwapAdjacentLevels(level - 1);
                    Collect();
                    level--;
                }
            }

            if (ReachableCount() >= sizeBeforePass) break; // a full pass found nothing
        }

        Collect();
    }

    /// <summary>
    ///     Exchange the variables at <paramref name="level" /> and <paramref name="level" />+1.
    ///     Every node at <paramref name="level" /> that depends on the lower variable is
    ///     rewritten by the Shannon swap identity
    ///     <c>ite(x, ite(y,f11,f10), ite(y,f01,f00)) = ite(y, ite(x,f11,f01), ite(x,f10,f00))</c>;
    ///     nodes that do not depend on it simply move down a level. Only the two affected
    ///     levels are rehashed and the ite cache is cleared. The rewrite reuses each level-x
    ///     node's slot so parents keep pointing at the same (function-preserving) handle, and
    ///     the new THEN edge stays regular — the canonical complement-edge invariant holds
    ///     through the swap.
    /// </summary>
    internal void SwapAdjacentLevels(int level)
    {
        var x = _levelVar[level];
        var y = _levelVar[level + 1];

        // Partition the level-x nodes: those that reference y at the next level must be
        // rewritten; the rest just descend a level when the perm is swapped below.
        var xNodes = _nodesOfVar[x];
        var toRewrite = new List<int>();
        var stay = new List<int>();
        foreach (var nf in xNodes)
        {
            var (_, low, high) = _nodes[nf];
            if (BranchesOn(low, y) || BranchesOn(high, y)) toRewrite.Add(nf);
            else stay.Add(nf);
        }

        // Drop the stale unique-table keys of the nodes about to be repurposed, so a newly
        // built x-node never hash-conses onto a slot that is becoming a y-node.
        foreach (var nf in toRewrite)
            _uniqueTable.Remove(_nodes[nf]);

        // Rebuild the x-level list from the survivors; MakeNode appends the new x-nodes.
        _nodesOfVar[x] = new List<int>(stay);

        foreach (var nf in toRewrite)
        {
            var (_, lowEdge, highEdge) = _nodes[nf];

            // Cofactor f wrt y on each x-branch. highEdge (the THEN edge) is regular, so
            // f11 is a stored THEN edge and therefore regular too.
            Cofactors(highEdge, y, out var f11, out var f10);
            Cofactors(lowEdge, y, out var f01, out var f00);

            // New x-nodes: f|y=1 = ite(x,f11,f01), f|y=0 = ite(x,f10,f00). Because f11 is
            // regular, gHigh is regular — the new node's THEN edge keeps the invariant.
            var gHigh = MakeNode(x, f01, f11);
            var gLow = MakeNode(x, f00, f10);

            // Reuse nf's slot as the top y-node: f = ite(y, gHigh, gLow). Same handle, same
            // function — every parent edge remains valid.
            var key = (y, gLow, gHigh);
            _nodes[nf] = key;
            _uniqueTable[key] = nf;
            _nodesOfVar[y].Add(nf);
        }

        // Swap the two variables' positions; nodes not rewritten now sit a level lower/higher.
        _varLevel[x] = level + 1;
        _varLevel[y] = level;
        _levelVar[level] = y;
        _levelVar[level + 1] = x;

        // ite results are keyed by node handles whose level meaning changed; drop the cache.
        _iteCache.Clear();
    }

    /// <summary>True when <paramref name="edge" /> points at a node branching on variable id <paramref name="variable" />.</summary>
    private bool BranchesOn(int edge, int variable)
    {
        var node = NodeOf(edge);
        return node != One && _nodes[node].Variable == variable;
    }

    /// <summary>
    ///     Split <paramref name="edge" /> into its cofactors with respect to variable
    ///     <paramref name="variable" /> (which sits at the level just below the edge's own):
    ///     <paramref name="cofactor1" /> for the variable true, <paramref name="cofactor0" />
    ///     for false, folding in the edge's complement bit.
    /// </summary>
    private void Cofactors(int edge, int variable, out int cofactor1, out int cofactor0)
    {
        var node = NodeOf(edge);
        if (node != One && _nodes[node].Variable == variable)
        {
            var (_, low, high) = _nodes[node];
            cofactor1 = high;
            cofactor0 = low;
            if (IsComplemented(edge))
            {
                cofactor1 = Complement(cofactor1);
                cofactor0 = Complement(cofactor0);
            }
        }
        else
        {
            cofactor1 = cofactor0 = edge; // independent of the variable
        }
    }

    /// <summary>Count nodes reachable from the root, including the terminal (the honest size metric).</summary>
    private int ReachableCount()
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(NodeOf(Root));
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == One || !seen.Add(node)) continue;
            var (_, low, high) = _nodes[node];
            stack.Push(NodeOf(low));
            stack.Push(NodeOf(high));
        }

        return seen.Count + 1; // + the terminal
    }

    /// <summary>
    ///     Garbage-collect: compact the flat node list to exactly the nodes reachable from
    ///     the root, renumbering handles and rebuilding the unique table and level index.
    ///     Swaps leave dead nodes behind; collecting keeps <see cref="NodeCount" /> honest
    ///     and the level lists free of stale entries.
    /// </summary>
    private void Collect()
    {
        var visited = new HashSet<int>();
        var order = new List<int>();
        var stack = new Stack<int>();
        stack.Push(NodeOf(Root));
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == One || !visited.Add(node)) continue;
            order.Add(node);
            var (_, low, high) = _nodes[node];
            stack.Push(NodeOf(low));
            stack.Push(NodeOf(high));
        }

        if (order.Count + 1 == _nodes.Count) return; // nothing dead

        var map = new int[_nodes.Count];
        map[One] = One;
        var next = 1;
        foreach (var node in order) map[node] = next++;

        var compacted = new List<(int, int, int)>(order.Count + 1) { (int.MaxValue, 0, 0) };
        _uniqueTable.Clear();
        foreach (var list in _nodesOfVar) list.Clear();

        foreach (var node in order)
        {
            var (variable, low, high) = _nodes[node];
            var key = (variable, Remap(low, map), Remap(high, map));
            var index = compacted.Count;
            compacted.Add(key);
            _uniqueTable[key] = index;
            _nodesOfVar[variable].Add(index);
        }

        _nodes.Clear();
        _nodes.AddRange(compacted);
        Root = Remap(Root, map);
        _iteCache.Clear();
    }

    private static int Remap(int edge, int[] map)
    {
        return (map[edge >> 1] << 1) | (edge & 1);
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
        var level = _varLevel[_nodes[node].Variable];
        return BigInteger.Pow(2, _variables.Count - level) - regular;
    }

    private BigInteger SatCountNode(int node, Dictionary<int, BigInteger> memo)
    {
        if (memo.TryGetValue(node, out var cached)) return cached;

        var (variable, low, high) = _nodes[node];
        var level = _varLevel[variable];
        var count = SatCount(low, memo) * BigInteger.Pow(2, VariableLevel(low) - level - 1) +
                    SatCount(high, memo) * BigInteger.Pow(2, VariableLevel(high) - level - 1);
        memo[node] = count;
        return count;
    }

    /// <summary>Variable level of an edge's node; the terminal sits one past the last variable.</summary>
    private int VariableLevel(int edge)
    {
        var node = NodeOf(edge);
        return node == One ? _variables.Count : _varLevel[_nodes[node].Variable];
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
            _nodesOfVar[variable].Add(index);
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

        var result = MakeNode(_levelVar[level], low, high);
        _iteCache[key] = result;
        return complementResult ? Complement(result) : result;
    }

    private int Cofactor(int edge, int level, bool positive)
    {
        var node = NodeOf(edge);
        if (node == One) return edge; // terminal: both cofactors are the edge itself
        var (variable, low, high) = _nodes[node];
        if (_varLevel[variable] != level) return edge; // does not branch on this level

        var child = positive ? high : low;
        // The cofactor of a complemented edge is the complement of the child's cofactor.
        return IsComplemented(edge) ? Complement(child) : child;
    }
}
