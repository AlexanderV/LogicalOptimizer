namespace LogicalOptimizer;

/// <summary>
///     N-ary conjunction node. The constructors are low-level: no flattening, sorting,
///     deduplication or folding is performed — build through
///     <see cref="FormulaFactory.And(AstNode[])" /> to get canonical trees.
/// </summary>
public sealed class AndNode : NaryNode
{
    /// <summary>Low-level n-ary constructor (at least two operands, stored as given).</summary>
    public AndNode(IReadOnlyList<AstNode> operands) : base(operands)
    {
    }

    /// <summary>Low-level two-operand convenience constructor.</summary>
    public AndNode(AstNode left, AstNode right) : base(new[] { left, right })
    {
    }

    /// <inheritdoc />
    public override string Operator => "&";
}
