namespace LogicalOptimizer;

public abstract class BinaryNode : AstNode
{
    protected BinaryNode(AstNode left, AstNode right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public AstNode Left { get; set; }
    public AstNode Right { get; set; }
    public abstract string Operator { get; }

    public override string ToString()
    {
        var leftStr = Left?.ToString() ?? "null";
        var rightStr = Right?.ToString() ?? "null";

        // Force parentheses have priority over precedence rules
        if (this is AndNode andNode && andNode.ForceParentheses) return $"({leftStr} {Operator} {rightStr})";
        if (this is OrNode orNode && orNode.ForceParentheses) return $"({leftStr} {Operator} {rightStr})";

        // Standard precedence rules - apply only if no forced parentheses
        if (Left is BinaryNode leftBin && GetPrecedence(leftBin) < GetPrecedence(this)) leftStr = $"({leftStr})";

        if (Right is BinaryNode rightBin && GetPrecedence(rightBin) < GetPrecedence(this)) rightStr = $"({rightStr})";

        return $"{leftStr} {Operator} {rightStr}";
    }

    public override HashSet<string> GetVariables()
    {
        var vars = Left.GetVariables();
        vars.UnionWith(Right.GetVariables());
        return vars;
    }

    public override bool Equals(object? obj)
    {
        return obj is BinaryNode other &&
               GetType() == other.GetType() &&
               Left.Equals(other.Left) &&
               Right.Equals(other.Right);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), Left.GetHashCode(), Right.GetHashCode());
    }

    private int GetPrecedence(BinaryNode node)
    {
        if (node is AndNode) return 2; // AND has higher precedence
        if (node is OrNode) return 1; // OR has lower precedence
        return 0;
    }
}