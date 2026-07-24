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

        // Assert
        Assert.NotNull(result);
        Assert.Contains("↔", result.Advanced);
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

        // Assert
        Assert.NotNull(result);
        Assert.Contains("↔", result.Advanced);
        Assert.Contains("c", result.Advanced);
    }

    [Theory]
    [InlineData("x & y | !x & !y", "x ↔ y")]
    [InlineData("p & q | !p & !q", "p ↔ q")]
    [InlineData("!m & !n | m & n", "↔")] // Either "m ↔ n" or "n ↔ m" is valid
    [InlineData("a & b | !a & !b", "↔")]
    public void EqvPattern_VariousInputs_DetectedCorrectly(string input, string expectedContains)
    {
        // Test various EQV patterns through string API

        // Arrange
        var detector = new AdvancedPatternDetector();

        // Act
        var result = detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.Contains(expectedContains, result);
    }
}
