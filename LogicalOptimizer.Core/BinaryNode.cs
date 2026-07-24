namespace LogicalOptimizer;

public abstract class BinaryNode : AstNode
{
    protected BinaryNode(AstNode left, AstNode right, bool forceParens = false)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        ForceParentheses = forceParens;
    }

    public AstNode Left { get; }
    public AstNode Right { get; }

    /// <summary>Display hint only; not part of structural equality.</summary>
    public bool ForceParentheses { get; set; }
    public abstract string Operator { get; }

    public override string ToString()
    {
        var leftStr = FormatChild(Left);
        var rightStr = FormatChild(Right);
        var result = $"{leftStr} {Operator} {rightStr}";
        return ForceParentheses ? $"({result})" : result;
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
        return System.HashCode.Combine(GetType(), Left, Right);
    }

    private string FormatChild(AstNode child)
    {
        var text = child.ToString();
        // A child with ForceParentheses already wrapped itself in ToString —
        // wrapping again would print ((...))
        return child is BinaryNode { ForceParentheses: false } childBinary && NeedsParentheses(childBinary)
            ? $"({text})"
            : text;
    }

    private bool NeedsParentheses(BinaryNode child)
    {
        // Extended operators (XOR, NAND, NOR, EQV, IMP) have no defined precedence
        // in the input grammar, so their binary children are always parenthesized.
        if (GetPrecedence(this) == 0) return true;
        return GetPrecedence(child) < GetPrecedence(this);
    }

    private static int GetPrecedence(BinaryNode node)
    {
        return node switch
        {
            AndNode => 2,
            OrNode => 1,
            _ => 0
        };
    }
}
