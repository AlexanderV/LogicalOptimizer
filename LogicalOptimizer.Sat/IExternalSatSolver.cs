using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Seam for plugging an external SAT solver (CaDiCaL, Kissat, a solver behind a
///     service, ...) into consumers that otherwise use the embedded CDCL solver. The
///     contract is deliberately minimal and one-shot — CNF in, verdict out — not an
///     incremental IPASIR binding: the library keeps parsing, Tseitin encoding and
///     counterexample decoding, and only the raw CNF query is handed off.
///     <para>
///         Trust model (asymmetric, enforced by the in-library consumers such as
///         <c>ExternalSatEquivalenceChecker</c>): a <see cref="SatResult.Satisfiable" />
///         verdict must come with a model, and the model IS verified against the CNF
///         (cheap, linear in the clause count) — a bogus model is detected and rejected.
///         A <see cref="SatResult.Unsatisfiable" /> verdict cannot be checked cheaply and
///         is TRUSTED; if that matters, run a proof-producing solver and check its
///         DRAT/LRAT certificate out of band (e.g. with drat-trim).
///     </para>
/// </summary>
public interface IExternalSatSolver
{
    /// <summary>
    ///     Decide satisfiability of <paramref name="problem" />. Implementations should
    ///     honor <paramref name="cancellationToken" /> (throw
    ///     <see cref="OperationCanceledException" />) and report
    ///     <see cref="SatResult.Unknown" /> when they give up without a verdict
    ///     (timeout, resource limit, solver not available mid-flight).
    /// </summary>
    ExternalSatResult Solve(ExternalSatProblem problem, CancellationToken cancellationToken = default);
}

/// <summary>
///     A one-shot CNF satisfiability query for an <see cref="IExternalSatSolver" />.
///     Literals follow the DIMACS convention: variables are 1-based, a positive literal
///     <c>v</c> asserts variable <c>v</c>, a negative literal <c>-v</c> its negation.
///     Optional assumptions are unit constraints for this query; adapters that speak
///     plain DIMACS may append them as unit clauses (equivalent for a single call), which
///     is exactly what <see cref="ToDimacs" /> does.
/// </summary>
public sealed class ExternalSatProblem
{
    /// <summary>
    ///     Create a problem over variables <c>1..variableCount</c>. Every literal in
    ///     <paramref name="clauses" /> and <paramref name="assumptions" /> must be
    ///     non-zero and within range.
    /// </summary>
    public ExternalSatProblem(int variableCount, IReadOnlyList<int[]> clauses,
        IReadOnlyList<int>? assumptions = null)
    {
        if (variableCount < 0) throw new ArgumentOutOfRangeException(nameof(variableCount));
        ArgumentNullException.ThrowIfNull(clauses);

        VariableCount = variableCount;
        Clauses = clauses;
        Assumptions = assumptions ?? Array.Empty<int>();

        foreach (var clause in Clauses)
        {
            ArgumentNullException.ThrowIfNull(clause, nameof(clauses));
            foreach (var literal in clause) ValidateLiteral(literal, nameof(clauses));
        }

        foreach (var literal in Assumptions) ValidateLiteral(literal, nameof(assumptions));
    }

    /// <summary>Number of variables; valid DIMACS indices are 1..VariableCount.</summary>
    public int VariableCount { get; }

    /// <summary>Clauses as arrays of DIMACS literals (no terminating 0).</summary>
    public IReadOnlyList<int[]> Clauses { get; }

    /// <summary>Assumption literals for this query; empty when none.</summary>
    public IReadOnlyList<int> Assumptions { get; }

    /// <summary>Build a problem from an equisatisfiable <see cref="TseitinCnf" />.</summary>
    public static ExternalSatProblem FromCnf(TseitinCnf cnf, IReadOnlyList<int>? assumptions = null)
    {
        ArgumentNullException.ThrowIfNull(cnf);
        return new ExternalSatProblem(cnf.TotalVariableCount, cnf.Clauses, assumptions);
    }

    /// <summary>
    ///     The problem in DIMACS CNF format, ready to hand to any standard solver.
    ///     Assumptions are appended as unit clauses (plain DIMACS has no assumption
    ///     syntax; for a one-shot query the two are equivalent).
    /// </summary>
    public string ToDimacs()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"p cnf {VariableCount} {Clauses.Count + Assumptions.Count}");
        foreach (var clause in Clauses)
            sb.AppendLine(string.Join(" ", clause) + " 0");
        foreach (var assumption in Assumptions)
            sb.AppendLine($"{assumption} 0");
        return sb.ToString();
    }

    /// <summary>
    ///     Verify a claimed model against this problem — the cheap half of the seam's
    ///     trust contract, linear in the total literal count. The model is a set of
    ///     DIMACS literals (as printed on a solver's "v" lines, without the final 0);
    ///     it may be partial, but every clause and every assumption must contain a
    ///     literal the model asserts. Contradictory or out-of-range literals fail.
    /// </summary>
    public bool IsSatisfiedBy(IReadOnlyList<int> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var assignment = new sbyte[VariableCount + 1];
        foreach (var literal in model)
        {
            var variable = Math.Abs(literal);
            if (literal == 0 || variable > VariableCount) return false;
            var sign = literal > 0 ? (sbyte)1 : (sbyte)-1;
            if (assignment[variable] == -sign) return false; // contradictory model
            assignment[variable] = sign;
        }

        foreach (var clause in Clauses)
        {
            var satisfied = false;
            foreach (var literal in clause)
                if (assignment[Math.Abs(literal)] == (literal > 0 ? 1 : -1))
                {
                    satisfied = true;
                    break;
                }

            if (!satisfied) return false;
        }

        foreach (var assumption in Assumptions)
            if (assignment[Math.Abs(assumption)] != (assumption > 0 ? 1 : -1))
                return false;

        return true;
    }

    private void ValidateLiteral(int literal, string parameterName)
    {
        var variable = Math.Abs(literal);
        if (literal == 0 || variable > VariableCount)
            throw new ArgumentOutOfRangeException(parameterName, $"Literal {literal} out of range");
    }
}

/// <summary>
///     Verdict of an <see cref="IExternalSatSolver" /> query, reusing the embedded
///     solver's <see cref="SatResult" /> vocabulary. A Satisfiable verdict carries the
///     model; Unsatisfiable and Unknown carry nothing.
/// </summary>
public sealed class ExternalSatResult
{
    private static readonly ExternalSatResult UnsatisfiableInstance =
        new(SatResult.Unsatisfiable, null);

    private static readonly ExternalSatResult UnknownInstance =
        new(SatResult.Unknown, null);

    private ExternalSatResult(SatResult verdict, IReadOnlyList<int>? model)
    {
        Verdict = verdict;
        Model = model;
    }

    /// <summary>Satisfiable, Unsatisfiable, or Unknown (gave up without a verdict).</summary>
    public SatResult Verdict { get; }

    /// <summary>
    ///     Satisfying assignment as DIMACS literals (a solver's "v" lines without the
    ///     final 0); set exactly when <see cref="Verdict" /> is Satisfiable. Consumers
    ///     verify it via <see cref="ExternalSatProblem.IsSatisfiedBy" /> — SAT claims
    ///     are checked, UNSAT claims are trusted.
    /// </summary>
    public IReadOnlyList<int>? Model { get; }

    /// <summary>A Satisfiable verdict with its model.</summary>
    public static ExternalSatResult Satisfiable(IReadOnlyList<int> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new ExternalSatResult(SatResult.Satisfiable, model);
    }

    /// <summary>An Unsatisfiable verdict (trusted by consumers; see the seam's trust model).</summary>
    public static ExternalSatResult Unsatisfiable()
    {
        return UnsatisfiableInstance;
    }

    /// <summary>No verdict — timeout, budget, or solver unavailable.</summary>
    public static ExternalSatResult Unknown()
    {
        return UnknownInstance;
    }
}
