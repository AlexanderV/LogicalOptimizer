using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace LogicalOptimizer.Benchmarks;

/// <summary>
///     Cancellation-overshoot measurement (competitive-assessment roadmap: "resource bounds
///     and cancellation must be observable"). For each budgeted long-running engine this
///     harness starts a workload sized to run for many seconds uncancelled (the same shapes
///     <c>MidFlightCancellationTests</c> uses to prove mid-flight cancellation), cancels the
///     token after a fixed delay, and measures the <b>overshoot</b> — the wall-clock latency
///     between the moment the token actually fired and the moment the engine returned
///     control (via <see cref="OperationCanceledException" />). Median/max over N
///     repetitions are reported per engine.
///     <para>
///         Honesty notes: overshoot is wall-clock and machine-dependent — it is reported,
///         never asserted as a bound. The cancel moment is captured by a callback registered
///         on the token (it runs at the instant the source transitions to cancelled), not by
///         trusting the requested timer delay, so timer slack does not inflate the numbers.
///         A repetition that completes before the token fires measures nothing and is
///         reported as <c>completedBeforeCancel</c>, never folded into the statistics.
///     </para>
///     Run: <c>dotnet run -c Release --project LogicalOptimizer.Benchmarks -- cancellation-overshoot</c>
///     Options: <c>--engine &lt;name&gt;</c> (repeatable filter), <c>--repetitions N</c>
///     (default 5), <c>--cancel-after-ms M</c> (default 250), <c>--out &lt;file.json&gt;</c>.
/// </summary>
public static class CancellationOvershootHarness
{
    private const int DefaultRepetitions = 5;
    private const double DefaultCancelAfterMs = 250;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     One measured repetition. <see cref="OvershootMs" /> is meaningful only when
    ///     <see cref="Cancelled" /> is true.
    /// </summary>
    public sealed record OvershootSample(bool Cancelled, double CancelObservedAtMs, double ReturnedAtMs)
    {
        /// <summary>Latency between the token firing and the engine returning control.</summary>
        public double OvershootMs => Cancelled ? ReturnedAtMs - CancelObservedAtMs : 0;
    }

    /// <summary>Per-engine overshoot statistics over <see cref="Repetitions" /> runs.</summary>
    public sealed record EngineOvershootResult(
        string Engine, int Repetitions, int CancelledRuns, int CompletedBeforeCancel,
        double CancelAfterMs, double MedianOvershootMs, double MaxOvershootMs);

    /// <summary>
    ///     Run <paramref name="workload" /> once on the calling thread, cancel its token after
    ///     <paramref name="cancelAfter" />, and time both the actual cancel moment and the
    ///     return moment on one shared stopwatch.
    /// </summary>
    public static OvershootSample MeasureOne(Action<CancellationToken> workload, TimeSpan cancelAfter)
    {
        using var source = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        // Capture the instant the source transitions to cancelled: the registered callback
        // runs synchronously inside Cancel(), so this is the true cancel time — NOT the
        // requested delay, which a busy timer thread can honor late.
        long cancelTicks = -1;
        using var registration = source.Token.Register(
            () => Volatile.Write(ref cancelTicks, stopwatch.Elapsed.Ticks));

        source.CancelAfter(cancelAfter);

        var cancelled = false;
        try
        {
            workload(source.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        var returnedAtMs = stopwatch.Elapsed.TotalMilliseconds;
        var observedTicks = Volatile.Read(ref cancelTicks);

        // Two benign races collapse to zero overshoot rather than a fabricated number:
        // the workload can observe IsCancellationRequested and throw before the timer
        // thread has run our callback (observedTicks still -1), or the callback can be
        // timestamped a hair after the workload returned.
        var cancelObservedAtMs = observedTicks < 0
            ? returnedAtMs
            : Math.Min(TimeSpan.FromTicks(observedTicks).TotalMilliseconds, returnedAtMs);

        return new OvershootSample(cancelled, cancelObservedAtMs, returnedAtMs);
    }

    /// <summary>
    ///     Repeat <see cref="MeasureOne" /> <paramref name="repetitions" /> times and reduce
    ///     the cancelled runs to median/max overshoot. Runs that finished before the token
    ///     fired are counted separately and contribute nothing to the statistics.
    /// </summary>
    public static EngineOvershootResult Measure(string engine, Action<CancellationToken> workload,
        TimeSpan cancelAfter, int repetitions)
    {
        if (repetitions < 1) throw new ArgumentOutOfRangeException(nameof(repetitions));

        var overshoots = new List<double>(repetitions);
        var completedBeforeCancel = 0;
        for (var i = 0; i < repetitions; i++)
        {
            var sample = MeasureOne(workload, cancelAfter);
            if (sample.Cancelled) overshoots.Add(sample.OvershootMs);
            else completedBeforeCancel++;
        }

        overshoots.Sort();
        var median = overshoots.Count > 0 ? overshoots[overshoots.Count / 2] : 0;
        var max = overshoots.Count > 0 ? overshoots[^1] : 0;

        return new EngineOvershootResult(
            engine, repetitions, overshoots.Count, completedBeforeCancel,
            cancelAfter.TotalMilliseconds, median, max);
    }

    /// <summary>
    ///     The budgeted long-running engines and, per engine, a workload sized to run for
    ///     many seconds uncancelled (mirroring <c>MidFlightCancellationTests</c> so "it
    ///     finished before the token fired" cannot happen on a realistic machine). Each
    ///     invocation builds a fresh instance, so repetitions are independent.
    /// </summary>
    public static IReadOnlyList<(string Engine, Action<CancellationToken> Workload)> EngineWorkloads()
    {
        return new (string, Action<CancellationToken>)[]
        {
            ("optimizer-pipeline", token =>
            {
                // Dense 12-variable expression of 8-literal products with the exact-backend
                // budgets lifted: prime generation / cover search run far beyond any
                // realistic cancel delay (same shape as Facade_CancelsDuringOptimization).
                var rng = new Random(777);
                var terms = Enumerable.Range(0, 140).Select(_ =>
                {
                    var vars = new SortedSet<int>();
                    while (vars.Count < 8) vars.Add(rng.Next(12));
                    return string.Join(" & ", vars.Select(v => (rng.Next(2) == 0 ? "!" : "") + $"v{v:D2}"));
                });
                new BooleanExpressionOptimizer().OptimizeExpression(string.Join(" | ", terms),
                    new OptimizationOptions
                    {
                        CancellationToken = token,
                        Budget = new ResourceBudget
                        {
                            QmPairComparisonLimit = long.MaxValue,
                            CoverStepLimit = int.MaxValue
                        }
                    });
            }),
            ("exact-minimizer", token =>
            {
                // 13-variable dense random Quine-McCluskey without budgets.
                var rng = new Random(42);
                var variables = Enumerable.Range(0, 13).Select(i => $"v{i:D2}").ToList();
                var onSet = new HashSet<int>();
                for (var m = 0; m < 1 << 13; m++)
                    if (rng.Next(2) == 0)
                        onSet.Add(m);
                TruthTableMinimizer.MinimalSop(variables, onSet, cancellationToken: token);
            }),
            ("sat-solve", token =>
            {
                // 400-variable random 3-SAT at the 4.26 phase-transition clause ratio.
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

                solver.Solve(int.MaxValue, token);
            }),
            ("bdd-build", token =>
            {
                // Interleaving-hostile variable order with the node budget lifted: ~2^26
                // nodes, far beyond what allocates before any realistic cancel delay.
                var terms = string.Join(" | ", Enumerable.Range(1, 26).Select(i => $"a{i:D2} & b{i:D2}"));
                var preamble = string.Join(" & ", Enumerable.Range(1, 26).Select(i => $"a{i:D2}"));
                BinaryDecisionDiagram.Build(Parse($"{preamble} | {terms}"), int.MaxValue, token);
            }),
            ("dnnf-compile", token =>
            {
                // Random 3-CNF at the phase-transition ratio: #SAT-hard, so d-DNNF
                // compilation (the expensive step; counting a compiled circuit is cheap)
                // runs long with the node budget lifted.
                var rng = new Random(20260731);
                var clauses = new List<string>();
                for (var c = 0; c < 511; c++)
                {
                    var vars = new HashSet<int>();
                    while (vars.Count < 3) vars.Add(rng.Next(1, 121));
                    clauses.Add("(" + string.Join(" | ",
                        vars.Select(v => (rng.Next(2) == 0 ? "!" : "") + $"x{v:D3}")) + ")");
                }

                KnowledgeCompilation.CompileToDnnf(Parse(string.Join(" & ", clauses)),
                    int.MaxValue, token);
            })
        };
    }

    public static int Run(string[] args)
    {
        var repetitions = IntOption(args, "--repetitions") ?? DefaultRepetitions;
        var cancelAfter = TimeSpan.FromMilliseconds(DoubleOption(args, "--cancel-after-ms") ?? DefaultCancelAfterMs);
        var outPath = StringOption(args, "--out");
        var engineFilter = StringOptions(args, "--engine");

        var workloads = EngineWorkloads()
            .Where(w => engineFilter.Count == 0 ||
                        engineFilter.Contains(w.Engine, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (workloads.Count == 0)
        {
            Console.Error.WriteLine(
                $"No engine matches the --engine filter. Known engines: {string.Join(", ", EngineWorkloads().Select(w => w.Engine))}");
            return 1;
        }

        Console.Error.WriteLine(
            $"cancellation-overshoot: {workloads.Count} engine(s), {repetitions} repetition(s), cancel after {F(cancelAfter.TotalMilliseconds)} ms");
        Console.Error.WriteLine(
            "overshoot = (observed return time - observed cancel time); wall-clock, machine-dependent, reported not asserted");

        var results = new List<EngineOvershootResult>();
        foreach (var (engine, workload) in workloads)
        {
            Console.Error.Write($"  {engine} ");
            var result = Measure(engine, workload, cancelAfter, repetitions);
            results.Add(result);
            Console.Error.WriteLine("done");
            if (result.CancelledRuns == 0)
                Console.Error.WriteLine(
                    $"  WARNING: {engine} completed every run before the token fired — nothing was measured; raise the workload or lower --cancel-after-ms.");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"{"Engine",-20} {"Reps",4} {"Cancelled",9} {"Completed",9} {"Cancel (ms)",11} {"Median overshoot (ms)",21} {"Max overshoot (ms)",18}");
        foreach (var r in results)
            Console.Out.WriteLine(
                $"{r.Engine,-20} {r.Repetitions,4} {r.CancelledRuns,9} {r.CompletedBeforeCancel,9} {F(r.CancelAfterMs),11} {F(r.MedianOvershootMs),21} {F(r.MaxOvershootMs),18}");

        if (outPath is not null)
        {
            var document = new
            {
                schemaVersion = 1,
                tool = "LogicalOptimizer.Benchmarks -- cancellation-overshoot",
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                cancelAfterMs = cancelAfter.TotalMilliseconds,
                repetitions,
                note = "Overshoot = observed return time - observed cancel time, per repetition; " +
                       "median/max over the cancelled repetitions only. Wall-clock, machine-dependent; " +
                       "reported for observability, never asserted as a bound.",
                engines = results.Select(r => new
                {
                    engine = r.Engine,
                    repetitions = r.Repetitions,
                    cancelledRuns = r.CancelledRuns,
                    completedBeforeCancel = r.CompletedBeforeCancel,
                    cancelAfterMs = r.CancelAfterMs,
                    medianOvershootMs = r.MedianOvershootMs,
                    maxOvershootMs = r.MaxOvershootMs
                })
            };
            File.WriteAllText(outPath, JsonSerializer.Serialize(document, JsonOptions) + "\n");
            Console.Out.WriteLine();
            Console.Out.WriteLine($"Wrote {outPath}");
        }

        return 0;
    }

    // --- helpers -------------------------------------------------------------------
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static string F(double ms)
    {
        return ms.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string? StringOption(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static List<string> StringOptions(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 1; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                values.Add(args[i + 1]);
        return values;
    }

    private static int? IntOption(string[] args, string name)
    {
        var raw = StringOption(args, name);
        return raw is null ? null : int.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static double? DoubleOption(string[] args, string name)
    {
        var raw = StringOption(args, name);
        return raw is null ? null : double.Parse(raw, CultureInfo.InvariantCulture);
    }
}
