namespace LogicalOptimizer;

/// <summary>
///     And-Inverter Graph: the circuit representation used by industrial synthesis tools
///     (ABC). Every function is a DAG of two-input AND nodes with complement bits on the
///     edges; structural hashing makes shared subcircuits count once, which is what makes
///     multi-level size metrics honest. Literals are ABC-style: node index shifted left,
///     complement in bit 0; literal 0 is constant false and literal 1 constant true.
///     Full cut-based rewriting is future work — see <see cref="Cleanup" /> for the
///     rebuild pass provided today.
/// </summary>
internal sealed partial class AndInverterGraph
{
    public const int FalseLiteral = 0;
    public const int TrueLiteral = 1;

    // Node 0 is the constant node; input nodes have Left = Right = -1
    private readonly List<(int Left, int Right)> _nodes = new() { (-1, -1) };

    // Reference (fanout) count per node, kept index-parallel to _nodes. The count of a
    // node is the number of fanin edges that point at it — one per AND node that uses it
    // as a child, plus one for the Root primary output. Complement bits on edges do not
    // matter: reference counts are per-node, so a regular and a complemented edge to the
    // same node each count once. Constants and inputs carry counts too (an input's count
    // is how many AND nodes / the Root use it). Because And() folds away constant and
    // complementary operands, no created AND node ever has the constant node as a fanin,
    // so _refCount[0] only moves when the Root itself is a constant.
    private readonly List<int> _refCount = new() { 0 };
    private readonly Dictionary<(int, int), int> _uniqueTable = new();
    private readonly Dictionary<string, int> _inputLiteral = new();
    private readonly List<string> _inputs = new();

    /// <summary>Input variable names in creation (sorted) order.</summary>
    public IReadOnlyList<string> Inputs => _inputs;

    /// <summary>Root literal of the function loaded by <see cref="FromAst" />.</summary>
    public int Root { get; private set; } = FalseLiteral;

    /// <summary>Two-input AND nodes only — inputs and the constant are not counted.</summary>
    public int AndNodeCount => _nodes.Count - 1 - _inputs.Count;

    /// <summary>Create (or return) the literal of a primary input.</summary>
    public int CreateInput(string name)
    {
        if (_inputLiteral.TryGetValue(name, out var existing)) return existing;
        var literal = _nodes.Count << 1;
        _nodes.Add((-1, -1));
        _refCount.Add(0);
        _inputs.Add(name);
        _inputLiteral[name] = literal;
        return literal;
    }

    public static int Not(int literal)
    {
        return literal ^ 1;
    }

    /// <summary>Structurally hashed AND with one-level simplification rules.</summary>
    public int And(int a, int b)
    {
        if (a == FalseLiteral || b == FalseLiteral) return FalseLiteral;
        if (a == TrueLiteral) return b;
        if (b == TrueLiteral) return a;
        if (a == b) return a;
        if (a == (b ^ 1)) return FalseLiteral;

        var key = a <= b ? (a, b) : (b, a);
        if (_uniqueTable.TryGetValue(key, out var cached)) return cached;

        var literal = _nodes.Count << 1;
        _nodes.Add((key.Item1, key.Item2));
        _refCount.Add(0);
        _uniqueTable[key] = literal;
        // The new node contributes one fanin edge to each of its two (distinct) children.
        _refCount[key.Item1 >> 1]++;
        _refCount[key.Item2 >> 1]++;
        return literal;
    }

    public int Or(int a, int b)
    {
        return Not(And(Not(a), Not(b)));
    }

    public int Xor(int a, int b)
    {
        return Or(And(a, Not(b)), And(Not(a), b));
    }

    public int Ite(int f, int g, int h)
    {
        return Or(And(f, g), And(Not(f), h));
    }

    /// <summary>Build a graph for an expression; the result's <see cref="Root" /> is set.</summary>
    public static AndInverterGraph FromAst(AstNode ast)
    {
        var graph = new AndInverterGraph();
        foreach (var name in ast.GetVariables().OrderBy(v => v)) graph.CreateInput(name);
        graph.Root = graph.Translate(ast, new Dictionary<AstNode, int>());
        graph._refCount[graph.Root >> 1]++; // Root counts as a primary-output reference.
        return graph;
    }

    private int Translate(AstNode node, Dictionary<AstNode, int> cache)
    {
        if (cache.TryGetValue(node, out var cached)) return cached;

        var literal = node switch
        {
            ConstantNode constant => constant.Value ? TrueLiteral : FalseLiteral,
            VariableNode variable => CreateInput(variable.Name),
            NotNode not => Not(Translate(not.Operand, cache)),
            AndNode and => TranslateNary(and.Operands, cache, And),
            OrNode or => TranslateNary(or.Operands, cache, Or),
            XorNode xor => Xor(Translate(xor.Left, cache), Translate(xor.Right, cache)),
            EqvNode eqv => Not(Xor(Translate(eqv.Left, cache), Translate(eqv.Right, cache))),
            ImpNode imp => Or(Not(Translate(imp.Left, cache)), Translate(imp.Right, cache)),
            NandNode nand => Not(And(Translate(nand.Left, cache), Translate(nand.Right, cache))),
            NorNode nor => Not(Or(Translate(nor.Left, cache), Translate(nor.Right, cache))),
            _ => throw new NotSupportedException($"Unsupported node type: {node.GetType()}")
        };
        cache[node] = literal;
        return literal;
    }

    /// <summary>
    ///     Fold an n-ary connective into two-input gates via balanced pairwise reduction,
    ///     keeping the resulting circuit depth logarithmic in the operand count.
    /// </summary>
    private int TranslateNary(IReadOnlyList<AstNode> operands, Dictionary<AstNode, int> cache,
        Func<int, int, int> combine)
    {
        var literals = new List<int>(operands.Count);
        foreach (var operand in operands) literals.Add(Translate(operand, cache));

        while (literals.Count > 1)
        {
            var next = new List<int>((literals.Count + 1) / 2);
            for (var i = 0; i + 1 < literals.Count; i += 2) next.Add(combine(literals[i], literals[i + 1]));
            if (literals.Count % 2 == 1) next.Add(literals[^1]);
            literals = next;
        }

        return literals[0];
    }

    /// <summary>Evaluate a literal under an assignment (missing variables default to false).</summary>
    public bool Evaluate(int literal, IReadOnlyDictionary<string, bool> assignment)
    {
        var memo = new Dictionary<int, bool>();
        return EvaluateNode(literal >> 1, assignment, memo) ^ (literal & 1) == 1;
    }

    private bool EvaluateNode(int node, IReadOnlyDictionary<string, bool> assignment, Dictionary<int, bool> memo)
    {
        if (node == 0) return false; // constant node: plain literal 0 is false
        if (memo.TryGetValue(node, out var cached)) return cached;

        bool value;
        var (left, right) = _nodes[node];
        if (left < 0)
        {
            var name = _inputs[node - 1];
            value = assignment.TryGetValue(name, out var v) && v;
        }
        else
        {
            value = (EvaluateNode(left >> 1, assignment, memo) ^ ((left & 1) == 1)) &&
                    (EvaluateNode(right >> 1, assignment, memo) ^ ((right & 1) == 1));
        }

        memo[node] = value;
        return value;
    }

    /// <summary>
    ///     Convert a literal back to an expression AST. Complemented ANDs of complemented
    ///     children are recovered as ORs so the output stays readable.
    /// </summary>
    public AstNode ToAst(int literal)
    {
        var node = literal >> 1;
        var complemented = (literal & 1) == 1;

        if (node == 0) return complemented ? ConstantNode.True : ConstantNode.False;

        var (left, right) = _nodes[node];
        if (left < 0)
        {
            AstNode input = new VariableNode(_inputs[node - 1]);
            return complemented ? new NotNode(input) : input;
        }

        // !(!x & !y) is OR; recover it for readability
        if (complemented && (left & 1) == 1 && (right & 1) == 1)
            return new OrNode(ToAst(left ^ 1), ToAst(right ^ 1));

        AstNode and = new AndNode(ToAst(left), ToAst(right));
        return complemented ? new NotNode(and) : and;
    }

    /// <summary>
    ///     Rebuild the graph bottom-up, re-applying hashing and the one-level rules, and
    ///     return the compacted graph (its <see cref="Root" /> is the rebuilt function).
    ///     Removes nodes that became dead or foldable after their operands simplified.
    /// </summary>
    public AndInverterGraph Cleanup()
    {
        var rebuilt = new AndInverterGraph();
        foreach (var name in _inputs) rebuilt.CreateInput(name);
        var map = new Dictionary<int, int>();
        rebuilt.Root = Rebuild(Root, rebuilt, map);
        rebuilt._refCount[rebuilt.Root >> 1]++; // Root counts as a primary-output reference.
        return rebuilt;
    }

    private int Rebuild(int literal, AndInverterGraph target, Dictionary<int, int> map)
    {
        var node = literal >> 1;
        var complemented = literal & 1;
        if (node == 0) return FalseLiteral ^ complemented;

        if (!map.TryGetValue(node, out var mapped))
        {
            var (left, right) = _nodes[node];
            mapped = left < 0
                ? target._inputLiteral[_inputs[node - 1]]
                : target.And(Rebuild(left, target, map), Rebuild(right, target, map));
            map[node] = mapped;
        }

        return mapped ^ complemented;
    }

    // ---------------------------------------------------------------------------------
    // Reference counting and MFFC — internal bookkeeping for DAG-aware cut rewriting.
    // See the _refCount field comment for the counting convention.
    // ---------------------------------------------------------------------------------

    /// <summary>
    ///     Reference (fanout) count of the node addressed by <paramref name="literal" />.
    ///     The complement bit is ignored — counts are per node, not per literal.
    /// </summary>
    public int RefCount(int literal)
    {
        return _refCount[literal >> 1];
    }

    /// <summary>Copy of every node's reference count, index-parallel to the node array.</summary>
    public int[] SnapshotRefCounts()
    {
        return _refCount.ToArray();
    }

    /// <summary>Copy of the raw fanin structure (index 0 is the constant, inputs have Left &lt; 0).</summary>
    public (int Left, int Right)[] SnapshotNodes()
    {
        return _nodes.ToArray();
    }

    /// <summary>
    ///     Recursively drop the reference counts of everything in the fanin cone of a node,
    ///     as if the node were removed. Decrements each fanin edge and recurses into any
    ///     child whose count reaches zero. The node's own count is left untouched (its
    ///     fanouts live outside the cone). Returns the number of AND nodes freed — the MFFC
    ///     size. Exact inverse of <see cref="Reference" />. Inputs and the constant are not
    ///     counted and have no fanins to descend into.
    /// </summary>
    public int Dereference(int literal)
    {
        var node = literal >> 1;
        if (node == 0 || _nodes[node].Left < 0) return 0;
        return DereferenceNode(node, null);
    }

    /// <summary>
    ///     Undo a <see cref="Dereference" />: restore the reference counts of the fanin cone,
    ///     re-referencing exactly the nodes the matching dereference freed. Returns the same
    ///     count as the paired dereference.
    /// </summary>
    public int Reference(int literal)
    {
        var node = literal >> 1;
        if (node == 0 || _nodes[node].Left < 0) return 0;
        return ReferenceNode(node, null);
    }

    /// <summary>
    ///     Number of AND nodes in the maximum fanout-free cone of <paramref name="literal" /> —
    ///     the nodes that would become dead if this node were removed. Computed by the classic
    ///     deref/ref pair, which leaves all reference counts exactly as they were.
    /// </summary>
    public int MffcSize(int literal)
    {
        var node = literal >> 1;
        if (node == 0 || _nodes[node].Left < 0) return 0;
        var freed = DereferenceNode(node, null);
        ReferenceNode(node, null);
        return freed;
    }

    /// <summary>
    ///     Node indices making up the maximum fanout-free cone of <paramref name="literal" />
    ///     (the node itself plus every descendant used only within the cone). Inputs and the
    ///     constant are excluded. Reference counts are restored before returning.
    /// </summary>
    public int[] ComputeMffc(int literal)
    {
        var node = literal >> 1;
        if (node == 0 || _nodes[node].Left < 0) return Array.Empty<int>();
        var cone = new List<int>();
        DereferenceNode(node, cone);
        ReferenceNode(node, null);
        return cone.ToArray();
    }

    // ---------------------------------------------------------------------------------
    // Cut enumeration — thin front doors onto AigCutEnumerator (see that file). Kept here
    // so callers can enumerate cuts straight off a graph; the enumerator snapshots the node
    // array, so mutating the graph afterwards means re-enumerating.
    // ---------------------------------------------------------------------------------

    /// <summary>k-feasible cuts of every node, computed bottom-up.</summary>
    public Dictionary<int, List<Cut>> EnumerateAllCuts(int k = 4)
    {
        return new AigCutEnumerator(this).EnumerateAll(k);
    }

    /// <summary>k-feasible cuts of a single node.</summary>
    public IReadOnlyList<Cut> EnumerateCuts(int literal, int k = 4)
    {
        return new AigCutEnumerator(this).Enumerate(literal >> 1, k);
    }

    private int DereferenceNode(int node, List<int>? cone)
    {
        var (left, right) = _nodes[node];
        cone?.Add(node);
        return 1 + DereferenceChild(left, cone) + DereferenceChild(right, cone);
    }

    private int DereferenceChild(int fanin, List<int>? cone)
    {
        var child = fanin >> 1;
        // A child drops out of the cone (and recurses) only when its last fanin edge goes
        // away; inputs and the constant are never part of an MFFC.
        if (--_refCount[child] == 0 && _nodes[child].Left >= 0)
            return DereferenceNode(child, cone);
        return 0;
    }

    private int ReferenceNode(int node, List<int>? cone)
    {
        var (left, right) = _nodes[node];
        cone?.Add(node);
        return 1 + ReferenceChild(left, cone) + ReferenceChild(right, cone);
    }

    private int ReferenceChild(int fanin, List<int>? cone)
    {
        var child = fanin >> 1;
        if (++_refCount[child] == 1 && _nodes[child].Left >= 0)
            return ReferenceNode(child, cone);
        return 0;
    }
}
