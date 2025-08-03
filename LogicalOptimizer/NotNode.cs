namespace LogicalOptimizer;

public class NotNode : AstNode
{
    public NotNode(AstNode operand)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    public AstNode Operand { get; set; }

    public override AstNode Clone()
    {
        return new NotNode(Operand.Clone());
    }

    public override string ToString()
    {
        return $"!{(Operand is BinaryNode ? $"({Operand})" : Operand.ToString())}";
    }

    public override HashSet<string> GetVariables()
    {
        return Operand.GetVariables();
    }

    public override bool Equals(object? obj)
    {
        return obj is NotNode other && Operand.Equals(other.Operand);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine("NOT", Operand.GetHashCode());
    }
}