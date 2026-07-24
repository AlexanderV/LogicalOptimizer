namespace LogicalOptimizer;

/// <summary>
///     Implication node representing logical implication operation
/// </summary>
public sealed class ImpNode : BinaryNode
{
    public ImpNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right, forceParens)
    {
    }

    public override string Operator => "→";

    public override AstNode Clone()
    {
        return new ImpNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }
}
