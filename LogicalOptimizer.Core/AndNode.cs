namespace LogicalOptimizer;

public sealed class AndNode : BinaryNode
{
    public AndNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right, forceParens)
    {
    }

    public override string Operator => "&";

    public override AstNode Clone()
    {
        return new AndNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }
}
