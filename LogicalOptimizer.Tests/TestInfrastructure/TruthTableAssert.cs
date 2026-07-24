using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Helper class for testing with truth table verification
/// </summary>
public static class TruthTableAssert
{
    /// <summary>
    ///     Verifies that the optimized expression is equivalent to the original through truth tables
    /// </summary>
    public static void AssertOptimizationEquivalence(string input, string expected,
        BooleanExpressionOptimizer optimizer)
    {
        // Perform optimization
        var result = optimizer.OptimizeExpression(input);

        // Check expected result (if it's not empty)
        if (!string.IsNullOrEmpty(expected)) Assert.Equal(expected, result.Optimized);

        // Check equivalence through truth tables
        Assert.True(result.IsEquivalent(),
            $"Optimized expression '{result.Optimized}' is not equivalent to original '{result.Original}'.\n" +
            $"Original truth table: {result.OriginalTruthTable?.GetResultsString()}\n" +
            $"Optimized truth table: {result.OptimizedTruthTable?.GetResultsString()}");
    }

    /// <summary>
    ///     Verifies only expression equivalence through truth tables (without checking exact match)
    /// </summary>
    public static void AssertEquivalence(string expression1, string expression2)
    {
        var isEquivalent = TruthTable.AreEquivalent(expression1, expression2);
        Assert.True(isEquivalent,
            $"Expressions '{expression1}' and '{expression2}' are not equivalent.");
    }

    /// <summary>
    ///     Checks equivalence of optimized expression to original without exact match verification
    /// </summary>
    public static void AssertOptimizationEquivalenceOnly(string input, BooleanExpressionOptimizer optimizer)
    {
        var result = optimizer.OptimizeExpression(input);

        Assert.True(result.IsEquivalent(),
            $"Optimized expression '{result.Optimized}' is not equivalent to original '{result.Original}'.\n" +
            $"Original truth table: {result.OriginalTruthTable?.GetResultsString()}\n" +
            $"Optimized truth table: {result.OptimizedTruthTable?.GetResultsString()}");
    }
}
