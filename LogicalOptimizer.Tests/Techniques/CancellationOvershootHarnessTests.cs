using LogicalOptimizer.Benchmarks;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The cancellation-overshoot harness (<c>-- cancellation-overshoot</c>) measures the
///     latency between a mid-flight cancel and the engine returning control. These tests pin
///     the measurement plumbing itself: the gate-visible ones use workloads whose outcome is
///     deterministic (block on the token's wait handle, or return immediately), so no
///     wall-clock value is ever asserted; the end-to-end run against a real engine is
///     timing-sensitive and stays in the Performance category like the mid-flight
///     cancellation suite it mirrors.
/// </summary>
public class CancellationOvershootHarnessTests
{
    [Fact]
    public void MeasureOne_WorkloadHonoringTheToken_RecordsACancelledSample()
    {
        // Deterministic: the workload blocks until the token fires, then throws — it can
        // neither finish early nor ignore the cancel, whatever the machine's timing.
        var sample = CancellationOvershootHarness.MeasureOne(token =>
        {
            token.WaitHandle.WaitOne();
            throw new OperationCanceledException(token);
        }, TimeSpan.FromMilliseconds(50));

        Assert.True(sample.Cancelled);
        Assert.True(sample.OvershootMs >= 0,
            $"Overshoot must be non-negative, got {sample.OvershootMs}");
        Assert.True(sample.ReturnedAtMs >= sample.CancelObservedAtMs,
            "The return timestamp must not precede the observed cancel timestamp");
    }

    [Fact]
    public void MeasureOne_WorkloadFinishingBeforeTheCancel_IsReportedNotFabricated()
    {
        // A run that completes before the token fires measures nothing; it must be flagged
        // as not-cancelled (and later excluded from statistics), never given a made-up
        // overshoot. The 30 s delay guarantees the no-op returns first on any machine.
        var sample = CancellationOvershootHarness.MeasureOne(_ => { }, TimeSpan.FromSeconds(30));

        Assert.False(sample.Cancelled);
        Assert.Equal(0, sample.OvershootMs);
    }

    [Fact]
    public void Measure_AggregatesOverCancelledRunsOnly()
    {
        // First repetition completes before the cancel; the remaining two block until the
        // token fires. Only the cancelled runs may enter the statistics.
        var repetition = 0;
        var result = CancellationOvershootHarness.Measure("synthetic", token =>
        {
            if (Interlocked.Increment(ref repetition) == 1) return;
            token.WaitHandle.WaitOne();
            throw new OperationCanceledException(token);
        }, TimeSpan.FromMilliseconds(20), 3);

        Assert.Equal("synthetic", result.Engine);
        Assert.Equal(3, result.Repetitions);
        Assert.Equal(2, result.CancelledRuns);
        Assert.Equal(1, result.CompletedBeforeCancel);
        Assert.Equal(20, result.CancelAfterMs);
        Assert.True(result.MedianOvershootMs >= 0);
        Assert.True(result.MaxOvershootMs >= result.MedianOvershootMs,
            "Max overshoot cannot be below the median");
    }

    [Fact]
    public void EngineWorkloads_CoverEveryBudgetedLongRunningEngine()
    {
        // The engines the competitive assessment requires to be observable: the full
        // optimizer pipeline, exact minimization, SAT solve, BDD build, d-DNNF compile.
        var engines = CancellationOvershootHarness.EngineWorkloads().Select(w => w.Engine).ToList();

        Assert.Equal(
            new[] { "optimizer-pipeline", "exact-minimizer", "sat-solve", "bdd-build", "dnnf-compile" },
            engines);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SatEngine_OvershootIsMeasuredEndToEnd()
    {
        // Real engine, real cancel: the 400-variable phase-transition instance runs far
        // beyond 250 ms uncancelled, so both repetitions must be cancelled mid-flight and
        // produce a genuine (non-fabricated) overshoot measurement. The generous upper
        // bound only proves the measurement is sane, mirroring MidFlightCancellationTests.
        var (engine, workload) = CancellationOvershootHarness.EngineWorkloads()
            .Single(w => w.Engine == "sat-solve");

        var result = CancellationOvershootHarness.Measure(
            engine, workload, TimeSpan.FromMilliseconds(250), 2);

        Assert.Equal(2, result.CancelledRuns);
        Assert.Equal(0, result.CompletedBeforeCancel);
        Assert.True(result.MedianOvershootMs >= 0);
        Assert.True(result.MaxOvershootMs < 60_000,
            $"Cancellation honored only after {result.MaxOvershootMs} ms — measurement or engine is broken");
    }
}
