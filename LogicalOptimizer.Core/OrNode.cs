namespace LogicalOptimizer;

public sealed class OrNode : BinaryNode
{
    public OrNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right, forceParens)
    {
    }

    public override string Operator => "|";

    public override AstNode Clone()
    {
        return new OrNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }
}
