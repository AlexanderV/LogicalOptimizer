namespace LogicalOptimizer;

public class AndNode : BinaryNode
{
    public AndNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right)
    {
        ForceParentheses = forceParens;
    }

    public bool ForceParentheses { get; set; }

    public override string Operator => "&";

    public override AstNode Clone()
    {
        return new AndNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }
}