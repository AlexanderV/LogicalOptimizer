using System;

namespace LogicalOptimizer;

/// <summary>
/// Main console application entry point - simplified and focused on orchestration
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        try
        {
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

            if (options.RunTests)
            {
                var testRunner = new TestRunner();
                return testRunner.RunTests() ? 0 : 1;
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

            // Handle CSV input
            if (options.CsvInput)
            {
                expression = CsvProcessor.ProcessCsvInput(expression);
            }

            // Optimize expression
            var optimizer = new BooleanExpressionOptimizer();
            var needsMetrics = options.Verbose;
            var needsDebugInfo = options.Verbose;

            var result = optimizer.OptimizeExpression(expression, needsMetrics, needsDebugInfo);

            // Display results
            var outputFormatter = new OutputFormatter();
            outputFormatter.DisplayResult(result, options);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing expression: {ex.Message}");
            return 1;
        }
    }
}
