using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer;

/// <summary>
/// Handles comprehensive demonstration of the optimizer features
/// </summary>
public class DemoRunner
{
    private readonly BooleanExpressionOptimizer _optimizer;

    public DemoRunner()
    {
        _optimizer = new BooleanExpressionOptimizer();
    }

    public void RunComprehensiveDemo()
    {
        Console.WriteLine("=== COMPREHENSIVE BOOLEAN OPTIMIZER DEMO ===\n");

        // Demonstration expressions with different optimization types
        var testCases = new Dictionary<string, string>
        {
            // Basic rules
            {"De Morgan", "!(a & b)"},
            {"Double Negation", "!!a"},
            {"Absorption", "a | (a & b)"},
            {"Complement", "a | !a"},
            {"Factorization", "(a | b) & (a | c)"},

            // Advanced rules
            {"Extended Absorption", "a | (!a & b)"},
            {"Consensus", "a & b | !a & c"},
            {"Complex Consensus", "a & b | !a & c | b & c"},

            // Complex expressions
            {"Tautology", "a | b | !a | c"},
            {"Contradiction", "a & !a"},
            {"Mixed Complex", "a & (b | c) | !a & d | b & c"},

            // Real examples
            {"Control Logic", "(start & !stop) | (running & !error)"},
            {"State Machine", "(state1 & event_a) | (state2 & event_b)"}
        };

        foreach (var testCase in testCases)
        {
            RunSingleDemo(testCase.Key, testCase.Value);
        }
    }

    private void RunSingleDemo(string testName, string expression)
    {
        Console.WriteLine($"=== {testName} ===");
        Console.WriteLine($"Expression: {expression}");

        try
        {
            // Full optimization with metrics
            var result = _optimizer.OptimizeExpression(expression, true);

            Console.WriteLine($"Optimized: {result.Optimized}");

            if (result.Metrics != null)
            {
                var improvement = result.Metrics.OriginalNodes > result.Metrics.OptimizedNodes ? "✓ Improved" :
                    result.Metrics.OriginalNodes < result.Metrics.OptimizedNodes ? "⚠ Expanded" : "= Same";

                Console.WriteLine(
                    $"Result: {improvement} ({result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes} nodes)");

                if (result.Metrics.AppliedRules > 0)
                    Console.WriteLine(
                        $"Applied {result.Metrics.AppliedRules} rules: {string.Join(", ", result.Metrics.RuleApplicationCount.Select(r => $"{r.Key}({r.Value})"))}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
        }

        Console.WriteLine();
    }
}
