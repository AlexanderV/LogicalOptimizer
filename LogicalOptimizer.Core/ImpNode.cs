namespace LogicalOptimizer;

/// <summary>
///     Implication node representing logical implication operation
/// </summary>
public sealed class ImpNode : BinaryNode
{
    /// <summary>Creates an implication left → right.</summary>
    public ImpNode(AstNode left, AstNode right) : base(left, right)
    {
    }

    /// <inheritdoc />
    public override string Operator => "→";
}
