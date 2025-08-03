namespace LogicalOptimizer;

/// <summary>
/// Implication node representing logical implication operation
/// </summary>
public class ImpNode : BinaryNode
{
    public override string Operator => "→";

    public ImpNode(AstNode left, AstNode right) : base(left, right)
    {
    }

    public override string ToString()
    {
        // ImpNode doesn't need automatic parentheses like other binary nodes
        // The → operator is clear enough on its own
        return $"{Left} → {Right}";
    }

    public override AstNode Clone()
    {
        return new ImpNode(Left.Clone(), Right.Clone());
    }
}
