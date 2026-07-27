using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Gate-visible mid-flight cancellation regression. The full mid-flight matrix (SAT,
///     BDD, facade) lives in <see cref="MidFlightCancellationTests" />, but that class is
///     [Category=Performance] and the CI filter (Category!=Performance&amp;Category!=Exhaustive)
///     excludes it — so the bug-guarding behavior it was written for (the QM/cover token
///     granularity gaps documented in doc/TESTING.md) never runs in the gate. This class
///     carries NO Performance trait: one deterministic mid-flight cancellation of the exact
///     (Quine–McCluskey) backend so the gate actually exercises it.
///
///     Determinism (avoiding the timing race an earlier version had): the workload is the
///     SAME reliably-multi-second dense 13-variable QM used by the Performance suite, but the
///     token fires EARLY (50 ms). The race is workload-runtime vs cancel-delay; a
///     seconds-scale workload against a 50 ms delay wins by a ~40x margin on any machine, so
///     "it finished before the token fired" cannot happen. Because the token is not
///     pre-cancelled, entry passes and the throw necessarily comes from a mid-loop token
///     check — proving mid-flight granularity. The test returns as soon as that check fires
///     (~tens of ms), so it is gate-cheap. There is deliberately NO Stopwatch/wall-clock
///     assertion (doc/TESTING.md Part 2 rule 4): the token-honored contract is the throw
///     itself, not a duration bound.
/// </summary>
public class MidFlightCancellationGateTests
{
    [Fact]
    public void ExactMinimizer_HonorsCancellation_MidQuineMcCluskeyRun()
    {
        // Dense 13-variable random ON-set: prime generation / cover search run for many
        // seconds uncancelled (14 vars would exhaust memory before a token check), so a
        // token that fires 50 ms in must interrupt it mid-flight. Deterministic seed ⇒
        // identical workload every run.
        var rng = new Random(20260727);
        var variables = Enumerable.Range(0, 13).Select(i => $"v{i:D2}").ToList();
        var onSet = new HashSet<int>();
        for (var m = 0; m < 1 << 13; m++)
            if (rng.Next(2) == 0)
                onSet.Add(m);

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.Throws<OperationCanceledException>(() =>
            TruthTableMinimizer.MinimalSop(variables, onSet, cancellationToken: source.Token));
    }
}
