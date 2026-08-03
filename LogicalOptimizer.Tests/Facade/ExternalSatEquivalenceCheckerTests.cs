using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Contract tests for the external-solver seam's consumer path, using in-process
///     fakes only (no external executable): verdict plumbing for SAT/UNSAT/Unknown,
///     counterexample decoding round-trip, cancellation propagation, and the trust
///     asymmetry — a lying SAT claim (bogus model) is detected and rejected, while a
///     lying UNSAT claim is trusted by documented contract.
/// </summary>
public class ExternalSatEquivalenceCheckerTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    /// <summary>The embedded CDCL solver playing the external role behind the seam.</summary>
    private sealed class EmbeddedBackedSolver : IExternalSatSolver
    {
        public ExternalSatProblem? LastProblem { get; private set; }

        public ExternalSatResult Solve(ExternalSatProblem problem, CancellationToken cancellationToken = default)
        {
            LastProblem = problem;
            var solver = new SatSolver(problem.VariableCount);
            foreach (var clause in problem.Clauses) solver.AddClause(clause);

            switch (solver.Solve(problem.Assumptions, cancellationToken: cancellationToken))
            {
                case SatResult.Satisfiable:
                    var model = new List<int>(problem.VariableCount);
                    for (var v = 1; v <= problem.VariableCount; v++)
                        model.Add(solver.GetValue(v) ? v : -v);
                    return ExternalSatResult.Satisfiable(model);
                case SatResult.Unsatisfiable:
                    return ExternalSatResult.Unsatisfiable();
                default:
                    return ExternalSatResult.Unknown();
            }
        }
    }

    private sealed class ScriptedSolver : IExternalSatSolver
    {
        private readonly Func<ExternalSatProblem, CancellationToken, ExternalSatResult> _respond;

        public ScriptedSolver(Func<ExternalSatProblem, CancellationToken, ExternalSatResult> respond)
        {
            _respond = respond;
        }

        public ExternalSatResult Solve(ExternalSatProblem problem, CancellationToken cancellationToken = default)
        {
            return _respond(problem, cancellationToken);
        }
    }

    [Fact]
    public void Check_EquivalentPair_UnsatVerdictBecomesEquivalent()
    {
        var checker = new ExternalSatEquivalenceChecker(new EmbeddedBackedSolver());

        var result = checker.Check(Parse("!(a & b)"), Parse("!a | !b"));

        Assert.True(result.AreEquivalent);
        Assert.Null(result.Counterexample);
    }

    [Fact]
    public void Check_NonEquivalentPair_ModelRoundTripsToACounterexample()
    {
        var checker = new ExternalSatEquivalenceChecker(new EmbeddedBackedSolver());
        var left = Parse("a | b & c");
        var right = Parse("(a | b) & c");

        var result = checker.Check(left, right);

        Assert.False(result.AreEquivalent);
        Assert.NotNull(result.Counterexample);
        var assignment = result.Counterexample!.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.NotEqual(TruthTable.Evaluate(left, assignment), TruthTable.Evaluate(right, assignment));
    }

    [Fact]
    public void Check_UnknownVerdict_PassesThroughAsInconclusive()
    {
        var checker = new ExternalSatEquivalenceChecker(
            new ScriptedSolver((_, _) => ExternalSatResult.Unknown()));

        Assert.Null(checker.Check(Parse("a & b"), Parse("a | b")).AreEquivalent);
    }

    [Fact]
    public void Check_HandsTheMiterCnfToTheAdapter()
    {
        var adapter = new EmbeddedBackedSolver();
        new ExternalSatEquivalenceChecker(adapter).Check(Parse("a & b"), Parse("b & a"));

        // The adapter sees the plain one-shot CNF query: clauses, no assumptions.
        Assert.NotNull(adapter.LastProblem);
        Assert.True(adapter.LastProblem!.Clauses.Count > 0);
        Assert.Empty(adapter.LastProblem.Assumptions);
        Assert.Contains($"p cnf {adapter.LastProblem.VariableCount} {adapter.LastProblem.Clauses.Count}",
            adapter.LastProblem.ToDimacs());
    }

    [Fact]
    public void Check_PropagatesTheCancellationTokenToTheAdapter()
    {
        using var source = new CancellationTokenSource();
        CancellationToken observed = default;
        var checker = new ExternalSatEquivalenceChecker(new ScriptedSolver((_, token) =>
        {
            observed = token;
            return ExternalSatResult.Unknown();
        }));

        checker.Check(Parse("a"), Parse("a"), source.Token);

        Assert.Equal(source.Token, observed);
    }

    [Fact]
    public void Check_CanceledAdapter_SurfacesOperationCanceled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var checker = new ExternalSatEquivalenceChecker(new ScriptedSolver((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return ExternalSatResult.Unknown();
        }));

        Assert.ThrowsAny<OperationCanceledException>(
            () => checker.Check(Parse("a & b"), Parse("a | b"), source.Token));
    }

    [Fact]
    public void Check_PreCanceledToken_ThrowsWithoutCallingTheAdapter()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var adapterCalls = 0;
        var checker = new ExternalSatEquivalenceChecker(new ScriptedSolver((_, _) =>
        {
            adapterCalls++;
            return ExternalSatResult.Unknown();
        }));

        Assert.ThrowsAny<OperationCanceledException>(
            () => checker.Check(Parse("a & b"), Parse("a | b"), source.Token));
        Assert.Equal(0, adapterCalls);
    }

    [Fact]
    public void Check_AdapterIgnoresCancellation_VerdictIsNotReturned()
    {
        // The adapter contract says honor the token; an adapter that ignores it and
        // returns a verdict anyway must not have that verdict reported as a result.
        using var source = new CancellationTokenSource();
        var checker = new ExternalSatEquivalenceChecker(new ScriptedSolver((_, _) =>
        {
            source.Cancel(); // canceled mid-flight; the adapter presses on regardless
            return ExternalSatResult.Unsatisfiable();
        }));

        Assert.ThrowsAny<OperationCanceledException>(
            () => checker.Check(Parse("a & b"), Parse("b & a"), source.Token));
    }

    [Fact]
    public void Check_LyingSatClaim_BogusModelIsDetected()
    {
        // Trust model, verified half: a SAT verdict must carry a model that satisfies the
        // miter CNF. An all-negative assignment falsifies the root unit clause, so the lie
        // is caught by the cheap linear check instead of producing a fake counterexample.
        var checker = new ExternalSatEquivalenceChecker(new ScriptedSolver((problem, _) =>
            ExternalSatResult.Satisfiable(
                Enumerable.Range(1, problem.VariableCount).Select(v => -v).ToArray())));

        Assert.Throws<InvalidOperationException>(() => checker.Check(Parse("a & b"), Parse("b & a")));
    }

    [Fact]
    public void Check_LyingSatClaim_MissingModelIsDetected()
    {
        var checker = new ExternalSatEquivalenceChecker(new ScriptedSolver((problem, _) =>
            ExternalSatResult.Satisfiable(Array.Empty<int>())));

        Assert.Throws<InvalidOperationException>(() => checker.Check(Parse("a & b"), Parse("a | b")));
    }

    [Fact]
    public void Check_LyingUnsatClaim_IsTrustedByDocumentedContract()
    {
        // Trust model, trusted half: an UNSAT verdict has no cheap refutation, so a lying
        // solver CAN make non-equivalent formulas pass. This is the documented asymmetry
        // (see the XML docs and the external-solvers article) — callers who need an
        // independently checkable equivalence claim must demand a DRAT proof out of band.
        var checker = new ExternalSatEquivalenceChecker(
            new ScriptedSolver((_, _) => ExternalSatResult.Unsatisfiable()));

        Assert.True(checker.Check(Parse("a & b"), Parse("a | b")).AreEquivalent);
    }
}
