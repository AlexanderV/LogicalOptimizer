using Xunit;
using LogicalOptimizer.Optimizers;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for DistributiveOptimizer
/// </summary>
public class DistributiveOptimizerTests
{
    private readonly DistributiveOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & (b | c)", "a & b | a & c")]
    [InlineData("a | (b & c)", "(a | b) & (a | c)")]
    [InlineData("(b | c) & a", "b & a | c & a")]
    [InlineData("(b & c) | a", "(b | a) & (c | a)")]
    public void DistributiveOptimizer_ShouldApplyDistributiveLaws(string input, string expected)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _optimizer.Optimize(ast);

        // Assert
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    public void DistributiveOptimizer_ShouldNotChangeSimpleExpressions(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _optimizer.Optimize(ast);

        // Assert
        Assert.Equal(input, result.ToString());
    }
}
