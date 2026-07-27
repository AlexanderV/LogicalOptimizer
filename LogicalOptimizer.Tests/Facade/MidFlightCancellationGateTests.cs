using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Gate-visible mid-flight cancellation regression. The strong mid-flight tests live
///     in <see cref="MidFlightCancellationTests" />, but that class is
///     [Category=Performance] and the CI filter (Category!=Performance&amp;Category!=Exhaustive)
///     excludes it — so the bug-guarding behavior it was written for (the QM/cover token
///     granularity gaps documented in doc/TESTING.md) never runs in the gate. This class
///     carries NO Performance trait: one bounded, deterministic mid-flight cancellation of
///     the exact (Quine–McCluskey) facade backend so the gate actually exercises it.
/// </summary>
public class MidFlightCancellationGateTests
{
    [Fact]
    public void Facade_HonorsCancellation_MidExactOptimization()
    {
        // Dense 12-variable SOP with the QM budgets lifted: the exact backend's prime
        // generation / cover search runs for many seconds uncancelled, so a token that
        // fires 200 ms in must interrupt it mid-flight rather than after completion.
        // Deterministic seed ⇒ identical workload every run.
        var rng = new Random(20260727);
        var terms = Enumerable.Range(0, 300)
            .Select(_ => $"v{rng.Next(12):D2} & {(rng.Next(2) == 0 ? "!" : "")}v{rng.Next(12):D2} & v{rng.Next(12):D2}");
        var expression = string.Join(" | ", terms);

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() =>
            new BooleanExpressionOptimizer().OptimizeExpression(expression, new OptimizationOptions
            {
                CancellationToken = source.Token,
                Budget = new ResourceBudget
                {
                    QmPairComparisonLimit = long.MaxValue,
                    CoverStepLimit = int.MaxValue
                }
            }));
        stopwatch.Stop();

        // The uncancelled workload runs for many seconds; a bound comfortably below that
        // (yet generous for a loaded CI box) proves the token was honored mid-computation
        // rather than at the very end.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"Cancellation honored only after {stopwatch.Elapsed} — engine ignores the token mid-flight");
    }
}
