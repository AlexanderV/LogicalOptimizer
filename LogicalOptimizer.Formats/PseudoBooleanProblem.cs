using System.Globalization;
using System.Text;

namespace LogicalOptimizer;

/// <summary>The comparison operator of a pseudo-Boolean constraint.</summary>
public enum PseudoBooleanComparison
{
    /// <summary>Σ cᵢ·lᵢ ≥ bound.</summary>
    GreaterOrEqual,

    /// <summary>Σ cᵢ·lᵢ ≤ bound.</summary>
    LessOrEqual,

    /// <summary>Σ cᵢ·lᵢ = bound.</summary>
    Equal
}

/// <summary>
///     A single linear pseudo-Boolean constraint <c>Σ coefficientᵢ · literalᵢ  OP  bound</c>.
///     A literal is a 1-based signed variable index: a positive value <c>v</c> is <c>xv</c>,
///     a negative value <c>-v</c> is the negation <c>~xv</c>. Coefficients may be negative.
/// </summary>
public sealed class PseudoBooleanConstraint
{
    private readonly (long Coefficient, int Literal)[] _terms;

    internal PseudoBooleanConstraint((long Coefficient, int Literal)[] terms,
        PseudoBooleanComparison comparison, long bound)
    {
        _terms = terms;
        Comparison = comparison;
        Bound = bound;
    }

    /// <summary>The weighted terms, each a (coefficient, signed-literal) pair.</summary>
    public IReadOnlyList<(long Coefficient, int Literal)> Terms => _terms;

    public PseudoBooleanComparison Comparison { get; }

    public long Bound { get; }
}

/// <summary>
///     A pseudo-Boolean (0/1 integer linear) problem parsed from OPB: an optional linear
///     <c>min:</c> objective (preserved for round-tripping but not optimized by the decision
///     <see cref="Solve" />) and a list of linear constraints. Feasibility is decided by
///     encoding every constraint to CNF through <see cref="PseudoBooleanEncoder" /> /
///     <see cref="CardinalityEncoder" /> and handing the result to the in-house
///     <see cref="SatSolver" />.
/// </summary>
public sealed class PseudoBooleanProblem
{
    private readonly PseudoBooleanConstraint[] _constraints;
    private readonly (long Coefficient, int Literal)[]? _objective;

    internal PseudoBooleanProblem(int variableCount, PseudoBooleanConstraint[] constraints,
        (long Coefficient, int Literal)[]? objective)
    {
        VariableCount = variableCount;
        _constraints = constraints;
        _objective = objective;
    }

    /// <summary>Declared number of variables (indices 1..VariableCount).</summary>
    public int VariableCount { get; }

    public IReadOnlyList<PseudoBooleanConstraint> Constraints => _constraints;

    /// <summary>The linear minimization objective, or <c>null</c> when the instance has none.</summary>
    public IReadOnlyList<(long Coefficient, int Literal)>? Objective => _objective;

    /// <summary>
    ///     Encode all constraints to CNF and build a <see cref="SatSolver" /> whose
    ///     satisfiability is exactly the feasibility of this pseudo-Boolean instance. The
    ///     objective is ignored: this is a decision, not an optimization, encoding.
    /// </summary>
    public SatSolver ToSolver()
    {
        var builder = new CnfBuilder(VariableCount);
        foreach (var constraint in _constraints) Encode(builder, constraint);
        return builder.ToSolver();
    }

    /// <summary>Decide whether this pseudo-Boolean instance is feasible.</summary>
    public SatResult Solve(int maxConflicts = 1_000_000, CancellationToken cancellationToken = default)
    {
        return ToSolver().Solve(maxConflicts, cancellationToken);
    }

    /// <summary>
    ///     Encode one constraint into <paramref name="builder" />. Negative coefficients are
    ///     normalized to positive ones over the complementary literal using
    ///     <c>c·l = c + (−c)·¬l</c>, which shifts the bound by the accumulated constant.
    /// </summary>
    private static void Encode(CnfBuilder builder, PseudoBooleanConstraint constraint)
    {
        var literals = new List<int>(constraint.Terms.Count);
        var weights = new List<long>(constraint.Terms.Count);
        var bound = constraint.Bound;

        foreach (var (coefficient, literal) in constraint.Terms)
        {
            if (coefficient == 0) continue;
            if (coefficient > 0)
            {
                literals.Add(literal);
                weights.Add(coefficient);
            }
            else
            {
                // c·l = c + (−c)·¬l : move the constant c to the bound, keep a positive weight
                literals.Add(-literal);
                weights.Add(-coefficient);
                bound -= coefficient;
            }
        }

        switch (constraint.Comparison)
        {
            case PseudoBooleanComparison.GreaterOrEqual:
                PseudoBooleanEncoder.AtLeast(builder, literals, weights, bound);
                break;
            case PseudoBooleanComparison.LessOrEqual:
                PseudoBooleanEncoder.AtMost(builder, literals, weights, bound);
                break;
            default: // Equal
                PseudoBooleanEncoder.AtLeast(builder, literals, weights, bound);
                PseudoBooleanEncoder.AtMost(builder, literals, weights, bound);
                break;
        }
    }

    /// <summary>
    ///     Write this problem back out as OPB text (round-trips through the parser): the
    ///     <c>* #variable= n #constraint= m</c> header, an optional <c>min:</c> objective, and
    ///     one <c>terms OP bound ;</c> line per constraint.
    /// </summary>
    public void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("* #variable= ");
        writer.Write(VariableCount.ToString(CultureInfo.InvariantCulture));
        writer.Write(" #constraint= ");
        writer.Write(_constraints.Length.ToString(CultureInfo.InvariantCulture));
        writer.Write('\n');

        var builder = new StringBuilder();
        if (_objective is { Length: > 0 })
        {
            builder.Append("min:");
            AppendTerms(builder, _objective);
            builder.Append(" ;");
            writer.Write(builder.ToString());
            writer.Write('\n');
        }

        foreach (var constraint in _constraints)
        {
            builder.Clear();
            AppendTerms(builder, constraint.Terms);
            builder.Append(' ');
            builder.Append(constraint.Comparison switch
            {
                PseudoBooleanComparison.GreaterOrEqual => ">=",
                PseudoBooleanComparison.LessOrEqual => "<=",
                _ => "="
            });
            builder.Append(' ');
            builder.Append(constraint.Bound.ToString(CultureInfo.InvariantCulture));
            builder.Append(" ;");
            writer.Write(builder.ToString());
            writer.Write('\n');
        }
    }

    private static void AppendTerms(StringBuilder builder, IReadOnlyList<(long Coefficient, int Literal)> terms)
    {
        foreach (var (coefficient, literal) in terms)
        {
            builder.Append(' ');
            if (coefficient >= 0) builder.Append('+');
            builder.Append(coefficient.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            if (literal < 0) builder.Append('~');
            builder.Append('x');
            builder.Append(Math.Abs(literal).ToString(CultureInfo.InvariantCulture));
        }
    }
}
