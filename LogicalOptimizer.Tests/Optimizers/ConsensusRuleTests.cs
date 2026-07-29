using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for consensus rule optimization and contradiction detection
/// </summary>
public class ConsensusRuleTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & !b | !a & b", "a & !b | b & !a")] // Pure XOR
    [InlineData("x & !y | !x & y", "x & !y | y & !x")] // XOR with different variables
    [InlineData("p & !q | !p & q", "p & !q | q & !p")] // Another XOR pattern
    public void ConsensusRule_XorPatterns_ShouldNotAddRedundantTerms(string xorPattern, string expected)
    {
        // First verify optimization equivalence (independent oracle)
        TruthTableAssert.AssertOptimizationEquivalenceOnly(xorPattern, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(xorPattern);

        // Assert - a two-literal XOR is a fixed point of the SOP pipeline: the canonical
        // two-term form is preserved exactly, with no redundant consensus term introduced.
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & b | !a & c | b & c", "a & b | c & !a")] // Consensus elimination
    [InlineData("x & y | !x & z | y & z", "x & y | z & !x")] // Different variables
    public void ConsensusRule_ValidConsensus_ShouldSimplify(string input, string expected)
    {
        // First verify optimization equivalence (main goal, independent oracle)
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert - the consensus term (b & c / y & z) is eliminated, leaving exactly the
        // two essential terms in canonical form (the exact-string pin already implies the
        // two-term shape, so a separate split-count assert would be redundant).
        Assert.Equal(expected, result.Optimized);
    }

    [Fact]
    public void ComplexExpression_WithMultiplePatterns_ShouldOptimizeCorrectly()
    {
        // Test complex expression combining XOR and IMP patterns
        var complexExpr = "((a & !b) | (!a & b)) & ((!c | d) | (e & f))";

        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(complexExpr, _optimizer);

        var result = _optimizer.OptimizeExpression(complexExpr);

        // The pattern cap now matches the overall input limit, so the 6-variable
        // expression DOES get its XOR recognized (previously silently ""), and the
        // double-parentheses printing bug around forced-parens children is fixed
        // Pin the full advanced rendering: the 6-variable XOR is folded, the residual
        // clause is rendered verbatim, and (regression guard) the forced-parens child is
        // NOT double-wrapped as "((a XOR b))".
        Assert.Equal("(d | !c | e & f) & (a XOR b)", result.Advanced);

        var inCapResult = _optimizer.OptimizeExpression("((a & !b) | (!a & b)) & (!c | d)");
        Assert.Equal("(c → d) & (a XOR b)", inCapResult.Advanced);

        // Verify variables are correct
        var expectedVariables = new[] { "a", "b", "c", "d", "e", "f" };
        Assert.Equal(expectedVariables.Length, result.Variables.Count);
        foreach (var variable in expectedVariables) Assert.Contains(variable, result.Variables);

        // Verify CNF and DNF don't contain advanced operators
        Assert.DoesNotContain("XOR", result.CNF);
        Assert.DoesNotContain("→", result.CNF);
        Assert.DoesNotContain("XOR", result.DNF);
        Assert.DoesNotContain("→", result.DNF);
    }
}
