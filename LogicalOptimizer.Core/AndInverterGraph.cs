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
public sealed class AndInverterGraph
{
    public const int FalseLiteral = 0;
    public const int TrueLiteral = 1;

    // Node 0 is the constant node; input nodes have Left = Right = -1
    private readonly List<(int Left, int Right)> _nodes = new() { (-1, -1) };
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
        _uniqueTable[key] = literal;
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
            AndNode and => And(Translate(and.Left, cache), Translate(and.Right, cache)),
            OrNode or => Or(Translate(or.Left, cache), Translate(or.Right, cache)),
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
}
