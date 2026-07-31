using System;

namespace LogicalOptimizer;

/// <summary>
/// Main console application entry point - simplified and focused on orchestration
/// </summary>
internal class Program
{
    public static int Main(string[] args)
    {
        try
        {
            // Standard-format verbs (solve/maxsat/solve-pb/count) read DIMACS/WCNF/OPB files
            // and bypass the expression-oriented flow entirely.
            if (args.Length > 0 && FormatCommands.IsFormatVerb(args[0]))
                return FormatCommands.Run(args);

            // The `check` verb compares TWO expressions, which the single-expression
            // optimize flow cannot express — it has its own parsing and exit codes.
            if (args.Length > 0 && args[0] == CheckCommand.Verb)
                return CheckCommand.Run(args);

            // Parse command line arguments
            var options = CommandLineProcessor.ParseArguments(args);

            if (!options.IsValid)
            {
                Console.Error.WriteLine($"Error: {options.ErrorMessage}");
                CommandLineProcessor.ShowUsage();
                return 1;
            }

            // Handle special commands
            if (options.ShowHelp)
            {
                CommandLineProcessor.ShowHelp();
                return 0;
            }

            if (options.ShowCsvExample)
            {
                CommandLineProcessor.ShowCsvExample();
                return 0;
            }

            if (options.RunDemo)
            {
                var demoRunner = new DemoRunner();
                demoRunner.RunComprehensiveDemo();
                return 0;
            }

            if (options.RunBenchmark)
            {
                var benchmarkRunner = new BenchmarkRunner();
                benchmarkRunner.RunBenchmark();
                return 0;
            }

            if (options.RunStressTest)
            {
                var benchmarkRunner = new BenchmarkRunner();
                benchmarkRunner.RunStressTest();
                return 0;
            }

            // Process expression
            return ProcessExpression(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int ProcessExpression(CommandLineProcessor.CommandLineOptions options)
    {
        // The received argument is kept separate from the expression actually analyzed: for CSV
        // input the two differ, and the JSON report has to name the former (that is what makes the
        // artifact traceable back to the CSV or the file the run was given). Both are declared out
        // here so the error path reports the same pair as the success path.
        var received = options.Expression;
        var source = options.CsvInput ? CliInputSource.Csv : CliInputSource.Expression;
        string? analyzed = null;

        try
        {
            var expression = received;

            // Multi-output CSV: minimize every declared output column and print the pairs
            if (options.OutputColumns.Count > 0)
            {
                foreach (var (name, minimized) in
                         CsvProcessor.ProcessCsvMultiOutput(expression, options.OutputColumns))
                    Console.WriteLine($"{name} = {minimized}");
                return 0;
            }

            // Handle CSV input
            if (options.CsvInput)
            {
                expression = analyzed = CsvProcessor.ProcessCsvInput(expression);
            }

            // Optimize expression, computing only the artifacts the chosen mode displays.
            // JSON is a full report, so it always computes CNF/DNF/advanced.
            var json = options.Format == CliOutputFormat.Json;
            var optimizer = new BooleanExpressionOptimizer();
            var optimizationOptions = new OptimizationOptions
            {
                ComputeCnf = json || (!options.DnfOnly && !options.Advanced && !options.AnfOnly),
                ComputeDnf = json || (!options.CnfOnly && !options.Advanced && !options.AnfOnly),
                CnfMode = options.CnfMode,
                ComputeAdvancedForms = json || (!options.CnfOnly && !options.DnfOnly && !options.AnfOnly),
                IncludeMetrics = options.Verbose,
                IncludeTruthTables = options.Verbose,
                IncludeDebugInfo = options.Verbose,
                IncludeTrace = options.Trace
            };

            var result = optimizer.OptimizeExpression(expression, optimizationOptions);

            // Display results
            if (json)
                JsonReportWriter.Write(Console.Out, result, received, source);
            else
                new OutputFormatter().DisplayResult(result, options);

            return 0;
        }
        catch (Exception ex)
        {
            // The facade wraps a parse failure, so the structured diagnostic may be the inner one.
            var diagnostic = (ex as FormulaParseException ?? ex.InnerException as FormulaParseException)?.Diagnostic;

            if (options.Format == CliOutputFormat.Json)
                JsonReportWriter.WriteError(Console.Out, received, source, ex.Message, diagnostic, analyzed);

            // Prefer the clean structured message over the facade's wrapped one.
            Console.Error.WriteLine($"Error processing expression: {diagnostic?.Message ?? ex.Message}");
            if (diagnostic is not null)
                Console.Error.WriteLine(diagnostic.Snippet);
            return 2;
        }
    }
}
