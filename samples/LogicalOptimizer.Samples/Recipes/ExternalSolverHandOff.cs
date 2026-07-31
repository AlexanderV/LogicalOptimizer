using System.Diagnostics;
using LogicalOptimizer;

namespace Samples.Recipes;

/// <summary>
///     Recipe 6 — the external-solver seam. An equivalence query is routed through a
///     user-supplied SAT solver (CaDiCaL, Kissat, ...) via <see cref="IExternalSatSolver" />:
///     the library still parses, builds the miter, does the Tseitin encoding and decodes the
///     counterexample; only the raw CNF query leaves the process as a DIMACS file.
///     <para>
///         Set the environment variable <c>LOGICALOPTIMIZER_EXTERNAL_SOLVER</c> to a solver
///         executable to run the hand-off for real. Without it the recipe degrades
///         gracefully: it explains what is missing and demonstrates the same seam with an
///         in-process adapter backed by the embedded solver, so the recipe always passes.
///     </para>
/// </summary>
internal static class ExternalSolverHandOff
{
    public static void Run()
    {
        const string baseline = "admin | (owner & businessHours)";
        const string broken = "admin & (owner | businessHours)"; // a refactor gone wrong

        var solverPath = Environment.GetEnvironmentVariable("LOGICALOPTIMIZER_EXTERNAL_SOLVER");
        IExternalSatSolver adapter;
        if (solverPath is not null && File.Exists(solverPath))
        {
            Console.WriteLine($"Using external solver: {solverPath}");
            adapter = new DimacsProcessSolver(solverPath);
        }
        else
        {
            Console.WriteLine(solverPath is null
                ? "LOGICALOPTIMIZER_EXTERNAL_SOLVER is not set (point it at a cadical/kissat executable " +
                  "to run the hand-off for real); demonstrating the seam with the embedded solver instead."
                : $"External solver not found at '{solverPath}'; demonstrating the seam with the embedded solver instead.");
            adapter = new EmbeddedSolverAdapter();
        }

        var checker = new ExternalSatEquivalenceChecker(adapter);
        var factory = new FormulaFactory();

        // Equivalent pair: the miter is UNSAT (the external solver's UNSAT verdict is trusted).
        var same = checker.Check(factory.Parse(baseline), factory.Parse("(owner & businessHours) | admin"));
        Console.WriteLine($"reordered baseline equivalent: {same.AreEquivalent}");

        // Broken refactor: the miter is SAT; the model is verified against the CNF, then
        // decoded back to a named counterexample.
        var differ = checker.Check(factory.Parse(baseline), factory.Parse(broken));
        Console.WriteLine($"broken refactor equivalent: {differ.AreEquivalent}");
        Console.WriteLine("counterexample: " + string.Join(", ",
            differ.Counterexample!.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}")));

        SampleAssert.That(same.AreEquivalent == true, "the reordered baseline must verify as equivalent");
        SampleAssert.That(differ.AreEquivalent == false, "the broken refactor must be caught");
    }

    /// <summary>
    ///     Reference process-based adapter: writes the problem as DIMACS to a temp file,
    ///     runs a standard SAT-competition solver executable on it, and parses the
    ///     "s SATISFIABLE" / "s UNSATISFIABLE" verdict and "v" model lines. Anything
    ///     unexpected (crash, timeout, unparsable output) becomes an Unknown verdict.
    /// </summary>
    private sealed class DimacsProcessSolver : IExternalSatSolver
    {
        private readonly string _solverPath;
        private readonly TimeSpan _timeout;

        public DimacsProcessSolver(string solverPath, TimeSpan? timeout = null)
        {
            _solverPath = solverPath;
            _timeout = timeout ?? TimeSpan.FromSeconds(60);
        }

        public ExternalSatResult Solve(ExternalSatProblem problem, CancellationToken cancellationToken = default)
        {
            var dimacsPath = Path.Combine(Path.GetTempPath(), $"logicaloptimizer-{Guid.NewGuid():N}.cnf");
            File.WriteAllText(dimacsPath, problem.ToDimacs());
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = _solverPath,
                    Arguments = $"\"{dimacsPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is null) return ExternalSatResult.Unknown();

                using var registration = cancellationToken.Register(() => TryKill(process));
                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
                {
                    TryKill(process);
                    return ExternalSatResult.Unknown();
                }

                cancellationToken.ThrowIfCancellationRequested();
                return ParseCompetitionOutput(output);
            }
            finally
            {
                try
                {
                    File.Delete(dimacsPath);
                }
                catch (IOException)
                {
                    // A leftover temp file is not worth failing the query over.
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited between the check and the kill.
            }
        }

        /// <summary>SAT-competition output: one "s" verdict line, "v" lines with the model, 0-terminated.</summary>
        private static ExternalSatResult ParseCompetitionOutput(string output)
        {
            string? verdict = null;
            var model = new List<int>();
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.StartsWith("s ", StringComparison.Ordinal))
                    verdict = line[2..].Trim();
                else if (line.StartsWith("v ", StringComparison.Ordinal))
                    foreach (var token in line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!int.TryParse(token, out var literal)) return ExternalSatResult.Unknown();
                        if (literal != 0) model.Add(literal);
                    }
            }

            return verdict switch
            {
                "SATISFIABLE" => ExternalSatResult.Satisfiable(model),
                "UNSATISFIABLE" => ExternalSatResult.Unsatisfiable(),
                _ => ExternalSatResult.Unknown()
            };
        }
    }

    /// <summary>
    ///     Fallback adapter so the recipe runs everywhere: the embedded CDCL solver
    ///     playing the role of the external one. The wiring through the seam — problem,
    ///     verdict, model verification, counterexample decoding — is identical.
    /// </summary>
    private sealed class EmbeddedSolverAdapter : IExternalSatSolver
    {
        public ExternalSatResult Solve(ExternalSatProblem problem, CancellationToken cancellationToken = default)
        {
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
}
