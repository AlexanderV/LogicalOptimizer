namespace LogicalOptimizer;

/// <summary>
///     N-ary disjunction node. The constructors are low-level: no flattening, sorting,
///     deduplication or folding is performed — build through
///     <see cref="FormulaFactory.Or(AstNode[])" /> to get canonical trees.
/// </summary>
public sealed class OrNode : NaryNode
{
    /// <summary>Low-level n-ary constructor (at least two operands, stored as given).</summary>
    public OrNode(IReadOnlyList<AstNode> operands) : base(operands)
    {
    }

    /// <summary>Low-level two-operand convenience constructor.</summary>
    public OrNode(AstNode left, AstNode right) : base(new[] { left, right })
    {
    }

    /// <inheritdoc />
    public override string Operator => "|";
}
