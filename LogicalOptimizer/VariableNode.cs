namespace LogicalOptimizer;

public class VariableNode : AstNode
{
    public VariableNode(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; set; }

    public override AstNode Clone()
    {
        return new VariableNode(Name);
    }

    public override string ToString()
    {
        return Name;
    }

    public override HashSet<string> GetVariables()
    {
        return new HashSet<string> {Name};
    }

    public override bool Equals(object? obj)
    {
        return obj is VariableNode other && Name == other.Name;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}