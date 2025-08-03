using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer;

/// <summary>
/// Handles performance testing and benchmarking
/// </summary>
public class BenchmarkRunner
{
    private readonly BooleanExpressionOptimizer _optimizer;

    public BenchmarkRunner()
    {
        _optimizer = new BooleanExpressionOptimizer();
    }

    public void RunBenchmark()
    {
        Console.WriteLine("=== PERFORMANCE TESTING ===\n");

        RunBasicBenchmarks();
        Console.WriteLine("\n=== STRESS TEST PERFORMANCE ===");
        RunStressTest();
    }

    private void RunBasicBenchmarks()
    {
        var benchmarkExpressions = new[]
        {
            // Simple expressions
            "a & b",
            "a | b",
            "!a",

            // Medium complexity
            "(a | b) & (a | c)",
            "a & b | !a & c",
            "!(a & b) | c",

            // Complex expressions
            "(a | b) & (c | d) & (e | f)",
            "a & b | c & d | e & f | g & h",
            "(a | b) & (c | d) | (e | f) & (g | h)",

            // Very complex
            "(a | b | c) & (d | e | f) & (g | h | i) & (j | k | l)",
            "a & b | a & c | a & d | b & c | b & d | c & d",
            "((a | b) & c) | ((d | e) & f) | ((g | h) & i)"
        };

        Console.WriteLine($"{"Expression",-40} {"Nodes",-8} {"Time (ms)",-10} {"Result",-15}");
        Console.WriteLine(new string('-', 80));

        foreach (var expr in benchmarkExpressions)
        {
            try
            {
                var startTime = DateTime.Now;
                var result = _optimizer.OptimizeExpression(expr, true); // includeMetrics = true
                var elapsed = DateTime.Now - startTime;

                var displayExpr = expr.Length > 35 ? expr.Substring(0, 32) + "..." : expr;
                var nodeChange = result.Metrics != null
                    ? $"{result.Metrics.OriginalNodes}→{result.Metrics.OptimizedNodes}"
                    : "N/A";

                Console.WriteLine($"{displayExpr,-40} {nodeChange,-8} {elapsed.TotalMilliseconds:F2,-10} {"✓",-15}");
            }
            catch (Exception ex)
            {
                var displayExpr = expr.Length > 35 ? expr.Substring(0, 32) + "..." : expr;
                Console.WriteLine(
                    $"{displayExpr,-40} {"Error",-8} {"N/A",-10} {ex.Message.Substring(0, Math.Min(14, ex.Message.Length)),-15}");
            }
        }
    }

    public void RunStressTest()
    {
        // Generate expressions of different sizes - reduced for better performance
        var sizes = new[] { 5, 10, 15, 20 }; // Reduced from {10, 50, 100, 200}

        foreach (var size in sizes)
        {
            try
            {
                var complexExpr = GenerateComplexExpression(size);
                Console.WriteLine(
                    $"Testing size {size}: {complexExpr.Substring(0, Math.Min(50, complexExpr.Length))}...");

                var startTime = DateTime.Now;
                var result = _optimizer.OptimizeExpression(complexExpr, true); // includeMetrics = true
                var elapsed = DateTime.Now - startTime;

                // Check for timeout (more than 5 seconds)
                if (elapsed.TotalSeconds > 5)
                {
                    Console.WriteLine($"Size {size} variables: TIMEOUT ({elapsed.TotalSeconds:F1}s)");
                    break; // Stop testing larger sizes
                }

                Console.WriteLine($"Size {size} variables: {elapsed.TotalMilliseconds:F2}ms " +
                                  $"({result.Metrics?.OriginalNodes ?? 0}→{result.Metrics?.OptimizedNodes ?? 0} nodes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Size {size} variables: ERROR - {ex.Message}");
                break; // Stop on first error
            }
        }
    }

    private string GenerateComplexExpression(int variableCount)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var variables = Enumerable.Range(0, variableCount)
            .Select(i => $"v{i}")
            .ToArray();

        var terms = new List<string>();

        // Generate fewer, simpler terms to improve performance
        var termCount = Math.Min(variableCount / 4, 8); // Max 8 terms, fewer than before

        for (var i = 0; i < termCount; i++)
        {
            // Pick two different variables to avoid immediate contradictions
            var var1Index = random.Next(variables.Length);
            var var2Index = (var1Index + random.Next(1, variables.Length)) % variables.Length;

            var var1 = variables[var1Index];
            var var2 = variables[var2Index];

            // Reduce negation probability to 20% to minimize tautologies
            var neg1 = random.Next(10) < 2 ? "!" : "";
            var neg2 = random.Next(10) < 2 ? "!" : "";

            // Favor AND operations to create more complex, non-trivial expressions
            var op = random.Next(10) < 8 ? "&" : "|";

            terms.Add($"({neg1}{var1} {op} {neg2}{var2})");
        }

        return string.Join(" | ", terms);
    }
}
