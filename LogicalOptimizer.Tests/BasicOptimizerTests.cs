using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the expression optimization functionality - core optimization algorithms
/// </summary>
public class OptimizerTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    /// <summary>
    ///     Helper method to verify expressions using compiled truth tables
    /// </summary>
    private void VerifyExpressionsWithCompiledTruthTables(string original, string? expectedOptimized = null)
    {
        // Optimize the expression with metrics enabled to get truth tables
        var result = _optimizer.OptimizeExpression(original, true);

        // Verify compiled truth tables are generated
        Assert.NotNull(result.CompiledOriginalTruthTable);
        Assert.NotNull(result.CompiledOptimizedTruthTable);

        // Verify equivalence using compiled truth tables
        Assert.True(
            CompiledTruthTable.AreEquivalent(result.CompiledOriginalTruthTable, result.CompiledOptimizedTruthTable),
            $"Compiled truth tables not equivalent for: {original} -> {result.Optimized}");

        // If expected result provided, verify it matches
        if (expectedOptimized != null) Assert.Equal(expectedOptimized, result.Optimized);

        // Output debug info for verification
        Console.WriteLine("=== Compiled Truth Table Verification ===");
        Console.WriteLine($"Original: {original}");
        Console.WriteLine($"Optimized: {result.Optimized}");
        Console.WriteLine(
            $"Equivalent: {CompiledTruthTable.AreEquivalent(result.CompiledOriginalTruthTable, result.CompiledOptimizedTruthTable)}");
        Console.WriteLine();
    }

    [Theory]
    [InlineData("a & a", "a")]
    [InlineData("a | a", "a")]
    public void Optimizer_IdempotentLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 1", "a")]
    [InlineData("a | 0", "a")]
    public void Optimizer_NeutralElements_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 0", "0")]
    [InlineData("a | 1", "1")]
    public void Optimizer_AbsorbingElements_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & !a", "0")]
    [InlineData("a | !a", "1")]
    public void Optimizer_ComplementLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!!a", "a")]
    [InlineData("!!!a", "!a")]
    public void Optimizer_DoubleNegation_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!(a & b)", "!a | !b")]
    [InlineData("!(a | b)", "!a & !b")]
    [InlineData("!(!a & !b)", "a | b")]
    public void Optimizer_DeMorganLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (a | b)", "a")]
    [InlineData("a | (a & b)", "a")]
    [InlineData("(a | b) & a", "a")]
    public void Optimizer_AbsorptionLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    public void Optimizer_Factorization_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & !b", "a")]
    [InlineData("(a & b) | (!a & b)", "b")]
    [InlineData("a & (b | !b)", "a")]
    [InlineData("a | (b & !b)", "a")]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Updated result with smart commutativity
    public void Optimizer_ComplexOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | b | !a | c", "1")] // Tautology: a | !a is always true
    [InlineData("a & b & !a & c", "0")] // Contradiction: a & !a is always false
    [InlineData("x | !x", "1")] // Simple tautology
    [InlineData("x & !x", "0")] // Simple contradiction
    [InlineData("a | b | !b", "1")] // Tautology with b | !b
    [InlineData("a & b & !b", "0")] // Contradiction with b & !b
    public void Optimizer_TautologiesAndContradictions_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")] // Direct factorization - should be WITHOUT double parentheses
    [InlineData("x & y | x & z", "x & (y | z)")] // Another case of direct factorization
    [InlineData("(a | b) & (a | c)", "a | (b & c)")] // Reverse factorization
    [InlineData("(x | y) & (x | z)", "x | (y & z)")] // Another case of reverse factorization
    public void Optimizer_FactorizationWithCorrectParentheses_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Smart commutativity changed order
    [InlineData("(a & b) | (a & c) | (a & d)", "a & (b | c | d)")] // Multiple factorization
    [InlineData("x | y | x", "x | y")] // Remove duplicates
    public void Optimizer_ComplexExpressions_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void Optimizer_FactorizationResult_ShouldNotHaveDoubleParentheses()
    {
        // Arrange
        var input = "a & b | a & c";
        var expected = "a & (b | c)";

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);

        // Also verify exact string format
        var result = _optimizer.OptimizeExpression(input);
        Assert.Equal(expected, result.Optimized);
        // Ensure there are no double parentheses
        Assert.DoesNotContain("((", result.Optimized);
        Assert.DoesNotContain("))", result.Optimized);
    }

    [Fact]
    public void Optimizer_ReverseFactorizationResult_ShouldHaveCorrectParentheses()
    {
        // Arrange
        var input = "(a | b) & (a | c)";
        var expected = "a | (b & c)";

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);

        // Also verify exact string format
        var result = _optimizer.OptimizeExpression(input);
        Assert.Equal(expected, result.Optimized);
        // Check correctness of parentheses: should only be around (b & c)
        Assert.Contains("(b & c)", result.Optimized);
        Assert.DoesNotContain("((", result.Optimized);
    }
}
