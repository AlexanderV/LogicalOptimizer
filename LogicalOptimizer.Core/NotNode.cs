namespace LogicalOptimizer;

/// <summary>Logical negation node. Immutable; hash code is computed once in the constructor.</summary>
public sealed class NotNode : AstNode
{
    private readonly int _hashCode;

    /// <summary>Creates a negation of <paramref name="operand" />.</summary>
    public NotNode(AstNode operand)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        _hashCode = HashCode.Combine(typeof(NotNode), Operand);
    }

    /// <summary>Negated operand.</summary>
    public AstNode Operand { get; }

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
        return Operand.GetVariables();
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is NotNode other && Operand.Equals(other.Operand);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _hashCode;
    }
}
