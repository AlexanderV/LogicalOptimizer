namespace LogicalOptimizer;

/// <summary>
///     Base class for the derived binary connectives (<see cref="ImpNode" />,
///     <see cref="XorNode" />, <see cref="NandNode" />, <see cref="NorNode" />,
///     <see cref="EqvNode" />) that live outside the canonical core. The core
///     connectives And/Or are n-ary (<see cref="NaryNode" />);
///     <see cref="FormulaFactory.Import" /> decomposes derived nodes into And/Or/Not.
///     Nodes are immutable and the hash code is computed once in the constructor.
/// </summary>
public abstract class BinaryNode : AstNode
{
    private readonly int _hashCode;

    /// <summary>Creates a derived binary node over the two operands.</summary>
    protected BinaryNode(AstNode left, AstNode right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        _hashCode = HashCode.Combine(GetType(), Left, Right);
    }

    /// <summary>Left operand.</summary>
    public AstNode Left { get; }

    /// <summary>Right operand.</summary>
    public AstNode Right { get; }

    /// <summary>Operator symbol used when rendering.</summary>
    public abstract string Operator { get; }

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
        var vars = Left.GetVariables();
        vars.UnionWith(Right.GetVariables());
        return vars;
    }

    /// <summary>Structural equality: same runtime type and equal operands.</summary>
    public override bool Equals(object? obj)
    {
        return obj is BinaryNode other &&
               GetType() == other.GetType() &&
               Left.Equals(other.Left) &&
               Right.Equals(other.Right);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _hashCode;
    }
}
