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
        try
        {
            var expression = options.Expression;

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
                expression = CsvProcessor.ProcessCsvInput(expression);
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
                IncludeDebugInfo = options.Verbose
            };

            var result = optimizer.OptimizeExpression(expression, optimizationOptions);

            // Display results
            if (json)
                JsonReportWriter.Write(Console.Out, result);
            else
                new OutputFormatter().DisplayResult(result, options);

            return 0;
        }
        catch (Exception ex)
        {
            if (options.Format == CliOutputFormat.Json)
                JsonReportWriter.WriteError(Console.Out, options.Expression, ex.Message);
            Console.Error.WriteLine($"Error processing expression: {ex.Message}");
            return 2;
        }
    }
}
