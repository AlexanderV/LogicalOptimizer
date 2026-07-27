namespace LogicalOptimizer;

/// <summary>
///     Base class for the n-ary associative connectives of the canonical core
///     (<see cref="AndNode" /> and <see cref="OrNode" />). Holds a flat, immutable
///     operand list of at least two entries. Structural equality is order-sensitive over
///     the operand list (operand order is canonical for factory-built trees), and the
///     hash code is computed once in the constructor — trees are fully immutable.
/// </summary>
public abstract class NaryNode : AstNode
{
    private readonly int _hashCode;

    /// <summary>
    ///     Low-level constructor: stores a defensive copy of <paramref name="operands" />
    ///     as-is. No flattening, sorting, deduplication or constant folding is performed —
    ///     build through <see cref="FormulaFactory" /> to get canonical trees.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than two operands were supplied.</exception>
    protected NaryNode(IReadOnlyList<AstNode> operands)
    {
        if (operands is null) throw new ArgumentNullException(nameof(operands));
        if (operands.Count < 2)
            throw new ArgumentException(
                $"An n-ary node requires at least 2 operands, got {operands.Count}", nameof(operands));

        var copy = new AstNode[operands.Count];
        for (var i = 0; i < operands.Count; i++)
            copy[i] = operands[i] ?? throw new ArgumentException(
                "Operand list must not contain null entries", nameof(operands));
        Operands = copy;

        var hash = new HashCode();
        hash.Add(GetType());
        foreach (var operand in copy) hash.Add(operand);
        _hashCode = hash.ToHashCode();
    }

    /// <summary>Flat operand list; always contains at least two entries.</summary>
    public IReadOnlyList<AstNode> Operands { get; }

    /// <summary>Operator symbol used when rendering (<c>&amp;</c> or <c>|</c>).</summary>
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
        var variables = new HashSet<string>();
        foreach (var operand in Operands) variables.UnionWith(operand.GetVariables());
        return variables;
    }

    /// <summary>
    ///     Structural, order-sensitive equality: same runtime type and pairwise-equal
    ///     operand lists.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not NaryNode other ||
            GetType() != other.GetType() ||
            _hashCode != other._hashCode ||
            Operands.Count != other.Operands.Count)
            return false;

        for (var i = 0; i < Operands.Count; i++)
            if (!Operands[i].Equals(other.Operands[i]))
                return false;
        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _hashCode;
    }
}
