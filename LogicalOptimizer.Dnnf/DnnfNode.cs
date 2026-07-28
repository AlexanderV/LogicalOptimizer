namespace LogicalOptimizer;

/// <summary>Node kinds of the compiled d-DNNF DAG.</summary>
internal enum DnnfKind : byte
{
    /// <summary>Constant FALSE terminal (zero models).</summary>
    False,

    /// <summary>Constant TRUE terminal (one model over the empty scope).</summary>
    True,

    /// <summary>A single literal; <see cref="DnnfNode.Value" /> is the signed DIMACS literal.</summary>
    Literal,

    /// <summary>
    ///     A decomposable conjunction: <see cref="DnnfNode.Children" /> have pairwise-disjoint
    ///     variable scopes, so the model count is the product of the children's counts.
    /// </summary>
    And,

    /// <summary>
    ///     A deterministic decision (disjunction) on variable <see cref="DnnfNode.Value" />:
    ///     <see cref="DnnfNode.Children" /> is exactly [low, high] where low is the residual with
    ///     the variable set false and high with it set true. The two branches are mutually
    ///     exclusive on that variable, so the model count is the sum of the branch counts.
    /// </summary>
    Or
}

/// <summary>
///     One node of the compact, hash-consed d-DNNF DAG. Stored in a flat array; children are
///     referenced by index. Kept as a readonly struct so the whole circuit is a single array.
/// </summary>
internal readonly struct DnnfNode
{
    public DnnfNode(DnnfKind kind, int value, int[] children)
    {
        Kind = kind;
        Value = value;
        Children = children;
    }

    public DnnfKind Kind { get; }

    /// <summary>Literal: the signed literal. Or: the (positive) decision variable index. Otherwise unused.</summary>
    public int Value { get; }

    /// <summary>And: the conjuncts. Or: [low, high]. Terminals/Literal: empty.</summary>
    public int[] Children { get; }
}
