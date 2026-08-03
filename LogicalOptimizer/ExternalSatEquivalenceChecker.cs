namespace LogicalOptimizer;

/// <summary>
///     Equivalence backend that routes the SAT-miter query through a user-supplied
///     <see cref="IExternalSatSolver" /> (e.g. a process adapter around CaDiCaL or
///     Kissat) while everything else — miter construction, Tseitin encoding,
///     counterexample decoding — stays in-library. Opt-in only: the default backends
///     remain <see cref="HybridEquivalenceChecker" /> and
///     <see cref="BddEquivalenceChecker" /> on the embedded engines.
///     <para>
///         Trust model: a Satisfiable verdict (non-equivalence) is VERIFIED — the
///         returned model must satisfy the miter CNF (checked in linear time via
///         <see cref="ExternalSatProblem.IsSatisfiedBy" />), otherwise the solver is
///         lying or broken and an <see cref="InvalidOperationException" /> is thrown.
///         An Unsatisfiable verdict (equivalence) is TRUSTED — refuting it cheaply is
///         not possible; demand a DRAT/LRAT proof from the solver and check it out of
///         band if the equivalence claim must be independently verifiable.
///     </para>
/// </summary>
public sealed class ExternalSatEquivalenceChecker : IEquivalenceChecker
{
    private readonly IExternalSatSolver _solver;

    /// <summary>Create a checker that sends every miter query to <paramref name="solver" />.</summary>
    public ExternalSatEquivalenceChecker(IExternalSatSolver solver)
    {
        ArgumentNullException.ThrowIfNull(solver);
        _solver = solver;
    }

    /// <summary>
    ///     Check equivalence through the external solver: UNSAT miter means equivalent
    ///     (trusted), SAT yields a verified counterexample over the input variables,
    ///     Unknown passes through as an inconclusive verdict.
    /// </summary>
    public EquivalenceCheckResult Check(AstNode left, AstNode right, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        // An already-canceled token must not pay for miter construction and Tseitin
        // encoding before the adapter gets its first chance to observe the token.
        cancellationToken.ThrowIfCancellationRequested();

        // Same miter as the embedded path: left XOR right is satisfiable exactly when
        // the sides differ somewhere. The full Tseitin style keeps models projectable
        // onto the input variables.
        var miter = TseitinConverter.Convert(new XorNode(left, right));
        var problem = ExternalSatProblem.FromCnf(miter);
        var result = _solver.Solve(problem, cancellationToken);
        // An adapter that ignores the token's contract must not turn a canceled query
        // into a verdict: cancellation wins over whatever the solver returned.
        cancellationToken.ThrowIfCancellationRequested();

        switch (result.Verdict)
        {
            case SatResult.Unsatisfiable:
                return EquivalenceCheckResult.Equivalent();

            case SatResult.Satisfiable:
                if (result.Model is null || !problem.IsSatisfiedBy(result.Model))
                    throw new InvalidOperationException(
                        "The external SAT solver claimed Satisfiable but its model does not satisfy " +
                        "the miter CNF. SAT verdicts are verified per the seam's trust contract; " +
                        "this adapter is lying or broken.");
                return EquivalenceCheckResult.NotEquivalent(DecodeCounterexample(miter, result.Model));

            default:
                return EquivalenceCheckResult.Unknown();
        }
    }

    private static IReadOnlyDictionary<string, bool> DecodeCounterexample(TseitinCnf miter, IReadOnlyList<int> model)
    {
        // A verified (possibly partial) model of the miter stays satisfying under any
        // completion, so an input variable the model leaves unassigned defaults to false.
        var counterexample = new Dictionary<string, bool>();
        foreach (var name in miter.InputVariables) counterexample[name] = false;
        foreach (var literal in model)
        {
            var variable = Math.Abs(literal);
            if (variable <= miter.InputVariables.Count)
                counterexample[miter.InputVariables[variable - 1]] = literal > 0;
        }

        return counterexample;
    }
}
