using System.Diagnostics;
using System.Globalization;
using System.Text;
using LogicalOptimizer;

namespace LogicalOptimizer.Benchmarks;

/// <summary>
///     Deterministic console harness for roadmap B2 (published comparison table:
///     result size / time vs SymPy and PyEDA on a shared function set). Reads the
///     shared corpus (<c>tools/comparison_corpus.txt</c> — the SAME file consumed by
///     <c>tools/compare_sympy_pyeda.py</c>), optimizes every function, and emits a
///     GitHub-markdown table of OUR measured result size (literal count of the
///     optimized form), minimization status, and wall-clock time.
///
///     Timings are a median over a handful of runs after one warm-up; they are
///     machine-dependent and indicative, not publication-grade error bars — the
///     BenchmarkDotNet suites remain the authority for tight measurement. This
///     harness exists so the cross-tool table in doc/BENCHMARKS.md has a reproducible,
///     single-command source for OUR column.
///
///     Run: <c>dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare</c>
/// </summary>
internal static class ComparisonHarness
{
    private const int WarmupRuns = 1;
    private const int MeasuredRuns = 7;

    public static int Run(string[] args)
    {
        var corpusPath = args.Length > 1 ? args[1] : FindCorpus();
        if (corpusPath is null || !File.Exists(corpusPath))
        {
            Console.Error.WriteLine(
                "Corpus not found. Expected tools/comparison_corpus.txt (pass an explicit path as the second argument).");
            return 1;
        }

        var functions = ParseCorpus(corpusPath);
        if (functions.Count == 0)
        {
            Console.Error.WriteLine($"Corpus at '{corpusPath}' contained no functions.");
            return 1;
        }

        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions
        { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

        var sb = new StringBuilder();
        sb.AppendLine("| Zone | Function | Vars | Input literals | LogicalOptimizer literals | Status | Time (ms) |");
        sb.AppendLine("|------|----------|-----:|---------------:|--------------------------:|--------|----------:|");

        foreach (var (zone, name, expression) in functions)
        {
            var inputLiterals = AstMetrics.CountLiterals(Parse(expression));

            // Warm up the JIT and any per-run caches so the timed runs are steady state.
            for (var i = 0; i < WarmupRuns; i++) optimizer.OptimizeExpression(expression, options);

            var samples = new double[MeasuredRuns];
            OptimizationResult result = null!;
            for (var i = 0; i < MeasuredRuns; i++)
            {
                var sw = Stopwatch.StartNew();
                result = optimizer.OptimizeExpression(expression, options);
                sw.Stop();
                samples[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            var medianMs = samples[MeasuredRuns / 2];
            var outputLiterals = AstMetrics.CountLiterals(Parse(result.Optimized));

            sb.Append("| ").Append(zone)
                .Append(" | ").Append(name)
                .Append(" | ").Append(result.Variables.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(inputLiterals.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(outputLiterals.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(StatusLabel(result.MinimizationStatus))
                .Append(" | ").Append(medianMs.ToString("F3", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        Console.Out.Write(sb.ToString());
        return 0;
    }

    private static string StatusLabel(MinimizationStatus status)
    {
        return status switch
        {
            MinimizationStatus.MinimalProven => "MinimalProven",
            MinimizationStatus.BudgetExceeded => "BudgetExceeded",
            _ => "Heuristic"
        };
    }

    private static List<(string Zone, string Name, string Expression)> ParseCorpus(string path)
    {
        var functions = new List<(string, string, string)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', 3);
            if (parts.Length != 3) continue;
            functions.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return functions;
    }

    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    /// <summary>Walk up from the running assembly to locate the committed corpus file.</summary>
    private static string? FindCorpus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "comparison_corpus.txt");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
