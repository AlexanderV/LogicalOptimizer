namespace LogicalOptimizer;

/// <summary>
///     Boolean constant (0 or 1). A dedicated node type so that algorithms never have
///     to remember that some "variables" are not really variables.
/// </summary>
public sealed class ConstantNode : AstNode
{
    public static readonly ConstantNode True = new(true);
    public static readonly ConstantNode False = new(false);

    public ConstantNode(bool value)
    {
        Value = value;
    }

    public bool Value { get; }

    public override AstNode Clone()
    {
        return this; // immutable
    }

    public override string ToString()
    {
        return Value ? "1" : "0";
    }

    public override HashSet<string> GetVariables()
    {
        return new HashSet<string>();
    }

    public override bool Equals(object? obj)
    {
        return obj is ConstantNode other && other.Value == Value;
    }

    public override int GetHashCode()
    {
        return Value ? 1 : 0;
    }
}
