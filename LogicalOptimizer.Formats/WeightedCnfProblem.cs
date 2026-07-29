using System.Globalization;
using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     A weighted partial MaxSAT problem parsed from WCNF: hard clauses that must hold and
///     soft clauses that carry a positive weight, the total weight of the falsified softs
///     being minimized. Hands off directly to the in-house <see cref="MaxSatSolver" />.
///     <see cref="Top" /> is the hard-clause weight sentinel of the classic format, preserved
///     so the writer round-trips.
/// </summary>
public sealed class WeightedCnfProblem
{
    private readonly int[][] _hardClauses;
    private readonly (long Weight, int[] Literals)[] _softClauses;

    internal WeightedCnfProblem(int variableCount, long top, int[][] hardClauses,
        (long Weight, int[] Literals)[] softClauses)
    {
        VariableCount = variableCount;
        Top = top;
        _hardClauses = hardClauses;
        _softClauses = softClauses;
    }

    /// <summary>Declared number of variables (indices 1..VariableCount).</summary>
    public int VariableCount { get; }

    /// <summary>The hard-clause weight sentinel (classic <c>p wcnf n m top</c> header).</summary>
    public long Top { get; }

    /// <summary>Hard clauses that any feasible assignment must satisfy.</summary>
    public IReadOnlyList<int[]> HardClauses => _hardClauses;

    /// <summary>Soft clauses paired with the positive weight paid when they are falsified.</summary>
    public IReadOnlyList<(long Weight, int[] Literals)> SoftClauses => _softClauses;

    /// <summary>Build a <see cref="MaxSatSolver" /> loaded with every hard and soft clause.</summary>
    public MaxSatSolver ToSolver()
    {
        var solver = new MaxSatSolver(VariableCount);
        foreach (var clause in _hardClauses) solver.AddHard(clause);
        foreach (var (weight, literals) in _softClauses)
        {
            if (weight > int.MaxValue)
                throw new ComputationBudgetExceededException(
                    $"Soft-clause weight {weight} exceeds the supported maximum of {int.MaxValue}");
            solver.AddSoft((int)weight, literals);
        }

        return solver;
    }

    /// <summary>Optimize this instance with the in-house weighted partial MaxSAT solver.</summary>
    public MaxSatResult Solve(int maxConflictsPerCall = 1_000_000, CancellationToken cancellationToken = default)
    {
        return ToSolver().Solve(maxConflictsPerCall, cancellationToken);
    }

    /// <summary>
    ///     Write this problem back out as classic WCNF text (round-trips through the parser):
    ///     a <c>p wcnf n m top</c> header, then <c>top ... 0</c> hard clauses and
    ///     <c>weight ... 0</c> soft clauses.
    /// </summary>
    public void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var clauseCount = _hardClauses.Length + _softClauses.Length;
        writer.Write("p wcnf ");
        writer.Write(VariableCount.ToString(CultureInfo.InvariantCulture));
        writer.Write(' ');
        writer.Write(clauseCount.ToString(CultureInfo.InvariantCulture));
        writer.Write(' ');
        writer.Write(Top.ToString(CultureInfo.InvariantCulture));
        writer.Write('\n');

        var builder = new StringBuilder();
        foreach (var clause in _hardClauses)
            WriteClause(writer, builder, Top, clause);
        foreach (var (weight, literals) in _softClauses)
            WriteClause(writer, builder, weight, literals);
    }

    private static void WriteClause(TextWriter writer, StringBuilder builder, long weight, int[] literals)
    {
        builder.Clear();
        builder.Append(weight.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
        foreach (var literal in literals)
        {
            builder.Append(literal.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
        }

        builder.Append('0');
        writer.Write(builder.ToString());
        writer.Write('\n');
    }
}
