namespace LogicalOptimizer;

public class OrNode : BinaryNode
{
    public OrNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right)
    {
        ForceParentheses = forceParens;
    }

    public bool ForceParentheses { get; set; }

    public override string Operator => "|";

    public override AstNode Clone()
    {
        return new OrNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }
}