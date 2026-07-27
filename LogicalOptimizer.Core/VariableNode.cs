namespace LogicalOptimizer;

/// <summary>Named boolean variable. Immutable; hash code is computed once in the constructor.</summary>
public sealed class VariableNode : AstNode
{
    private readonly int _hashCode;

    /// <summary>Creates a variable with the given name.</summary>
    public VariableNode(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _hashCode = Name.GetHashCode();
    }

    /// <summary>Variable name.</summary>
    public string Name { get; }

    /// <summary>Nodes are fully immutable, so cloning returns the same instance.</summary>
    public override AstNode Clone()
    {
        return this;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return AstFormatter.Format(this);
    }

    /// <inheritdoc />
    public override HashSet<string> GetVariables()
    {
        return new HashSet<string> { Name };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is VariableNode other && Name == other.Name;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _hashCode;
    }
}
