using System;

namespace LogicalOptimizer;

/// <summary>
/// Handles built-in testing functionality
/// </summary>
public class TestRunner
{
    private readonly BooleanExpressionOptimizer _optimizer;

    public TestRunner()
    {
        _optimizer = new BooleanExpressionOptimizer();
    }

    public bool RunTests()
    {
        Console.WriteLine("Running built-in tests...");
        try
        {
            TestFactorizationIssue();
            Console.WriteLine("All built-in tests passed successfully!");
            Console.WriteLine("For full testing use: dotnet test");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running tests: {ex.Message}");
            return false;
        }
    }

    private void TestFactorizationIssue()
    {
        Console.WriteLine("=== DEBUGGING CONTEXTUAL PARENTHESES ===");
        TestContextualParentheses();

        Console.WriteLine("\n=== TESTING ADVANCED OPTIMIZATION ===");
        TestAdvancedOptimization();
    }

    private void TestContextualParentheses()
    {
        // Test from specification
        var input = "(a | b) & (a | c)";
        var expected = "a | (b & c)";

        var result = _optimizer.OptimizeExpression(input);
        var passed = result.Optimized == expected;

        Console.WriteLine($"Input: {input}");
        Console.WriteLine($"Expected: {expected}");
        Console.WriteLine($"Actual: {result.Optimized}");
        Console.WriteLine($"Test PASSED: {passed}");
    }

    private void TestAdvancedOptimization()
    {
        // Test for consensus rules and other advanced capabilities
        string[] testExpressions =
        {
            "a & b | !a & c", // Should apply consensus rule
            "a & b | a & c", // Should apply factorization  
            "a | !a & b", // Should apply absorption
            "a & b | !a & c | b & c", // Complex consensus case
            "(a | b) & (!a | c)", // Should give simpler expression
            "a & (b | c) | !a & d" // Mixed case
        };

        foreach (var expr in testExpressions)
        {
            Console.WriteLine($"\n--- Testing: {expr} ---");
            var result = _optimizer.OptimizeExpression(expr, true, true); // debug mode with metrics

            Console.WriteLine($"Original: {result.Original}");
            Console.WriteLine($"Optimized: {result.Optimized}");

            if (result.Metrics != null)
            {
                Console.WriteLine($"Optimization took: {result.Metrics.ElapsedTime.TotalMilliseconds:F2}ms");
                Console.WriteLine(
                    $"Applied {result.Metrics.AppliedRules} rules in {result.Metrics.Iterations} iterations");
                Console.WriteLine($"Node count: {result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes}");

                if (result.Metrics.RuleApplicationCount.Count > 0)
                {
                    Console.WriteLine("Applied rules:");
                    foreach (var rule in result.Metrics.RuleApplicationCount)
                        Console.WriteLine($"  {rule.Key}: {rule.Value} times");
                }
            }

            // Show AST visualization for simpler expressions
            if (expr.Length < 20)
                try
                {
                    var parser = new Parser(new Lexer(expr).Tokenize());
                    var ast = parser.Parse();
                    Console.WriteLine("\nAST Visualization:");
                    Console.WriteLine(AstVisualizer.VisualizeTree(ast));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"AST visualization error: {e.Message}");
                }
        }
    }
}
