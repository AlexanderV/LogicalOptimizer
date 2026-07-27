using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the ExpressionGenerator component - random expression generation and demonstration cases
/// </summary>
public class ExpressionGeneratorTests
{
    private static AstNode ParseExpression(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void GenerateRandomExpression_WithVariableCount_ShouldReturnValidExpression(int variableCount)
    {
        // Act
        var expression = ExpressionGenerator.GenerateRandomExpression(variableCount);

        // Assert
        Assert.NotNull(expression);

        if (variableCount < 2)
        {
            // Fewer than two variables produce zero terms, hence an empty expression
            Assert.Equal("", expression);
            return;
        }

        Assert.NotEmpty(expression);

        // Should be parseable
        var parseException = Record.Exception(() => ParseExpression(expression));
        Assert.Null(parseException);

        // Should contain variable patterns (v0, v1, etc.)
        var containsVariablePattern = Enumerable.Range(0, variableCount)
            .Any(i => expression.Contains($"v{i}"));
        Assert.True(containsVariablePattern, "Expression should contain at least one variable");
    }

    [Fact]
    public void GenerateRandomExpression_MultipleInvocations_ShouldGenerateDifferentExpressions()
    {
        // Act
        var expressions = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            expressions.Add(ExpressionGenerator.GenerateRandomExpression(4));
        }

        // Assert
        // Should generate at least some different expressions (not all identical)
        Assert.True(expressions.Count > 1, "Should generate some variation in expressions");
    }

    [Fact]
    public void DemonstrationAndBenchmarkExpressions_AllEntries_ShouldBeParseable()
    {
        // Arrange
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Assert
        Assert.NotEmpty(demonstrations);
        Assert.NotEmpty(benchmarks);

        foreach (var kvp in demonstrations.Concat(benchmarks))
        {
            Assert.False(string.IsNullOrEmpty(kvp.Value), $"Entry '{kvp.Key}' has an empty expression");
            var exception = Record.Exception(() => ParseExpression(kvp.Value));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void AllGeneratedExpressions_ShouldOptimizeEquivalently()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Act & Assert: optimizing must not throw AND must preserve semantics — an
        // optimizer that returned a non-equivalent result would pass a no-throw check.
        foreach (var expr in demonstrations.Values.Concat(benchmarks.Values))
        {
            var result = optimizer.OptimizeExpression(expr);
            Assert.True(result.IsEquivalent(),
                $"Optimized '{result.Optimized}' is not equivalent to original '{result.Original}'.");
        }
    }

    [Fact]
    public void RandomExpressions_ShouldOptimizeEquivalently()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();

        // Act & Assert: no throw AND semantic equivalence preserved for each random input.
        for (int i = 0; i < 5; i++)
        {
            var expr = ExpressionGenerator.GenerateRandomExpression(3);
            var result = optimizer.OptimizeExpression(expr);
            Assert.True(result.IsEquivalent(),
                $"Optimized '{result.Optimized}' is not equivalent to original '{result.Original}'.");
        }
    }
}
