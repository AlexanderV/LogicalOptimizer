using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Mid-flight cancellation (debt from the audit: only pre-cancelled tokens were
///     tested before): the token fires WHILE the engine is running, and the engine must
///     abandon work promptly with OperationCanceledException. Each workload is sized to
///     run for many seconds uncancelled, so "it finished before the token fired" cannot
///     happen on any realistic machine; the elapsed-time bound proves the cancellation
///     was honored mid-computation rather than at the end.
/// </summary>
[Trait("Category", "Performance")]
public class MidFlightCancellationTests
{
    private static void AssertCancelsPromptly(Action<CancellationToken> workload)
    {
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => workload(source.Token));
        stopwatch.Stop();
        // Generous bound: the uncancelled workloads run for minutes; under full-suite
        // parallel CPU load even a promptly-honored token can take seconds to observe
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Cancellation honored only after {stopwatch.Elapsed} — engine ignores the token mid-flight");
    }

    [Fact]
    public void ExactMinimizer_CancelsDuringDenseThirteenVariableRun()
    {
        // 13-var dense random QM without budgets runs for many seconds (14 vars would
        // exhaust memory faster than any token check); the token must interrupt prime
        // generation / cover search long before completion
        var rng = new Random(42);
        var variables = Enumerable.Range(0, 13).Select(i => $"v{i:D2}").ToList();
        var onSet = new HashSet<int>();
        for (var m = 0; m < 1 << 13; m++)
            if (rng.Next(2) == 0)
                onSet.Add(m);

        AssertCancelsPromptly(token =>
            TruthTableMinimizer.MinimalSop(variables, onSet, cancellationToken: token));
    }

    [Fact]
    public void SatSolver_CancelsDuringHardPhaseTransitionInstance()
    {
        // 400-variable instance at the 4.26 clause ratio: far beyond what resolves in
        // 250 ms; Solve must observe the token between conflicts
        var rng = new Random(4242);
        var solver = new SatSolver(400);
        for (var c = 0; c < 1704; c++)
        {
            var clause = new HashSet<int>();
            while (clause.Count < 3)
            {
                var v = rng.Next(1, 401);
                clause.Add(rng.Next(2) == 0 ? v : -v);
            }

            solver.AddClause(clause.ToArray());
        }

        AssertCancelsPromptly(token => solver.Solve(int.MaxValue, token));
    }

    [Fact]
    public void Bdd_CancelsDuringExponentialAdversarialBuild()
    {
        // The interleaving-hostile order with a huge node budget explodes: 26 split
        // pairs mean ~2^26 nodes, far beyond what allocates in 250 ms; the token must
        // stop MakeNode long before the budget does
        var terms = string.Join(" | ", Enumerable.Range(1, 26).Select(i => $"a{i:D2} & b{i:D2}"));
        var preamble = string.Join(" & ", Enumerable.Range(1, 26).Select(i => $"a{i:D2}"));
        var ast = RandomExpressions.Parse($"{preamble} | {terms}");

        AssertCancelsPromptly(token =>
        {
            // Sorted order splits every pair: a01..a14 then b01..b14 => 2^14-ish nodes
            var manager = new BinaryDecisionDiagram(ast.GetVariables(), int.MaxValue, token);
            manager.FromAst(ast);
        });
    }

    [Fact]
    public void Facade_CancelsDuringOptimization()
    {
        // Full pipeline on a dense 12-variable expression: with the budgets lifted the
        // exact backend runs for minutes uncancelled, so the token must interrupt it
        var rng = new Random(777);
        var terms = Enumerable.Range(0, 400)
            .Select(_ => $"v{rng.Next(12):D2} & {(rng.Next(2) == 0 ? "!" : "")}v{rng.Next(12):D2} & v{rng.Next(12):D2}");
        var expression = string.Join(" | ", terms);

        AssertCancelsPromptly(token =>
            new BooleanExpressionOptimizer().OptimizeExpression(expression, new OptimizationOptions
            {
                CancellationToken = token,
                Budget = new ResourceBudget
                {
                    QmPairComparisonLimit = long.MaxValue,
                    CoverStepLimit = int.MaxValue
                }
            }));
    }
}
