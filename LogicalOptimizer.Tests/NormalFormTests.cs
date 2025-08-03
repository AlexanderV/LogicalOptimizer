using Xunit;

namespace LogicalOptimizer.Tests;

public class NormalFormTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a | b", "a | b")]
    [InlineData("a & b", "a & b")]
    [InlineData("a & (b | c)", "a & (b | c)")]
    [InlineData("a | (b & c)", "(a | b) & (a | c)")]
    public void NormalForms_CNF_ShouldConvertCorrectly(string input, string expectedCNF)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expectedCNF, result.CNF);

        // Also verify equivalence with original
        TruthTableAssert.AssertEquivalence(input, result.CNF);
    }

    [Theory]
    [InlineData("a | b", "a | b")]
    [InlineData("a & b", "a & b")]
    [InlineData("(a | b) & c", "c & a | c & b")] // Updated result thanks to smart commutativity
    [InlineData("a & (b | c)", "a & b | a & c")]
    public void NormalForms_DNF_ShouldConvertCorrectly(string input, string expectedDNF)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expectedDNF, result.DNF);

        // Also verify equivalence with original
        TruthTableAssert.AssertEquivalence(input, result.DNF);
    }
}
