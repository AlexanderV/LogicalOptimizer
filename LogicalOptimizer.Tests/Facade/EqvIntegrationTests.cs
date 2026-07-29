using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Integration tests for EQV functionality through public APIs
/// </summary>
public class EqvIntegrationTests
{
    [Fact]
    public void BooleanExpressionOptimizer_EqvPattern_OptimizesCorrectly()
    {
        // Test the complete optimization pipeline with EQV patterns

        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        var expression = "(a & b) | (!a & !b)"; // EQV pattern

        // Act
        var result = optimizer.OptimizeExpression(expression);

        // Assert - the advanced rendering of a bare EQV is deterministic; pin it exactly
        Assert.NotNull(result);
        Assert.Equal("a ↔ b", result.Advanced);
    }

    [Fact]
    public void ComplexExpression_WithEqvSubpattern_OptimizedCorrectly()
    {
        // Test EQV optimization in complex expressions

        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        var expression = "c & ((a & b) | (!a & !b))"; // c AND EQV(a,b)

        // Act
        var result = optimizer.OptimizeExpression(expression);

        // Assert - the conjunct survives and the EQV is folded; pin the full form so
        // Contains("c") (nearly always true) cannot mask a dropped/misplaced conjunct
        Assert.NotNull(result);
        Assert.Equal("c & (a ↔ b)", result.Advanced);
    }

    [Theory]
    [InlineData("x & y | !x & !y", "x ↔ y")]
    [InlineData("p & q | !p & !q", "p ↔ q")]
    [InlineData("!m & !n | m & n", "m ↔ n")] // Rendering is deterministic — pin the operand order
    [InlineData("a & b | !a & !b", "a ↔ b")]
    public void EqvPattern_VariousInputs_DetectedCorrectly(string input, string expected)
    {
        // Test various EQV patterns through string API

        // Arrange
        var detector = new AdvancedPatternDetector();

        // Act
        var result = detector.ConvertToAdvancedForms(input);

        // Assert - the detector output is a single deterministic string; a bare Contains("↔")
        // would pass on a wrong operand pairing, so assert the exact advanced form
        Assert.Equal(expected, result);
    }
}
