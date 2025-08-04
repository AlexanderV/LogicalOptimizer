using Xunit;
using LogicalOptimizer;
using LogicalOptimizer.Optimizers;
using System.Linq;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests specifically for CommutativityOptimizer in isolation
/// </summary>
public class CommutativityOptimizerTests
{
    private readonly CommutativityOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & b", "a & b")]
    [InlineData("b & a", "a & b")]
    [InlineData("a | b", "a | b")]
    [InlineData("b | a", "a | b")]
    [InlineData("a & b & c", "a & b & c")]
    [InlineData("c & b & a", "a & b & c")]
    public void CommutativityOptimizer_ShouldSortTermsConsistently(string input, string expected)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var optimized = _optimizer.Optimize(ast, null);

        // Assert
        Assert.Equal(expected, optimized.ToString());
    }
}
