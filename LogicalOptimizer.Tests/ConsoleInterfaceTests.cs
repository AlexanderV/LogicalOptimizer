using Xunit;

namespace LogicalOptimizer.Tests;

public class ConsoleInterfaceTests
{
    [Fact]
    public void BooleanExpressionOptimizer_WithVeryLongExpression_ThrowsArgumentException()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        var longExpression = string.Join(" | ", Enumerable.Range(1, 2000).Select(i => $"var{i}"));

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => optimizer.OptimizeExpression(longExpression));
        Assert.Contains("too long", exception.Message);
    }

    [Fact]
    public void BooleanExpressionOptimizer_WithManyVariables_ThrowsArgumentException()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        // Create expression with 101 variables
        var variables = Enumerable.Range(1, 101).Select(i => $"v{i}");
        var expression = string.Join(" | ", variables);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => optimizer.OptimizeExpression(expression));
        Assert.Contains("variables", exception.Message);
    }

    [Fact]
    public void BooleanExpressionOptimizer_WithDeepNesting_ThrowsArgumentException()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        // Create expression with deep nesting (51 levels)
        var deepExpression = new string('(', 51) + "a" + new string(')', 51);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => optimizer.OptimizeExpression(deepExpression));
        Assert.Contains("nesting", exception.Message);
    }

    [Fact]
    public void BooleanExpressionOptimizer_WithVerboseMode_IncludesTruthTables()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();

        // Act
        var result = optimizer.OptimizeExpression("a & b", true);

        // Assert
        Assert.NotNull(result.OriginalTruthTable);
        Assert.NotNull(result.OptimizedTruthTable);
        Assert.NotNull(result.Metrics);
    }

    [Fact]
    public void BooleanExpressionOptimizer_WithStandardMode_DoesNotIncludeTruthTables()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();

        // Act
        var result = optimizer.OptimizeExpression("a & b");

        // Assert
        Assert.Null(result.OriginalTruthTable);
        Assert.Null(result.OptimizedTruthTable);
        Assert.Null(result.Metrics);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("!(a & b)", "!a | !b")]
    [InlineData("!!a", "a")]
    public void BooleanExpressionOptimizer_StandardFormat_ProducesCorrectOutput(string input, string expectedOptimized)
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expectedOptimized, optimizer);

        var result = optimizer.OptimizeExpression(input);
        Assert.Equal(input, result.Original);
        Assert.Equal(expectedOptimized, result.Optimized);
        Assert.NotNull(result.CNF);
        Assert.NotNull(result.DNF);
        Assert.NotEmpty(result.Variables);
    }

    [Fact]
    public void ExpressionOptimizer_WithTooManyIterations_ThrowsInvalidOperationException()
    {
        // This test is difficult to write without creating a pathological case,
        // but we can check that the validator works
        Assert.Throws<InvalidOperationException>(() =>
            PerformanceValidator.ValidateIterations(51));
    }

    [Fact]
    public void PerformanceValidator_Constants_HaveCorrectValues()
    {
        // Check that constants match specification
        Assert.Equal(10000, PerformanceValidator.MAX_EXPRESSION_LENGTH);
        Assert.Equal(100, PerformanceValidator.MAX_VARIABLES);
        Assert.Equal(50, PerformanceValidator.MAX_PARENTHESES_DEPTH);
        Assert.Equal(50, PerformanceValidator.MAX_OPTIMIZATION_ITERATIONS);
        Assert.Equal(30, PerformanceValidator.MAX_PROCESSING_TIME_SECONDS);
    }
}