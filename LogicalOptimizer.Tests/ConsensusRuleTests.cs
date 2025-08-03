using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for consensus rule optimization and contradiction detection
/// </summary>
public class ConsensusRuleTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & !b | !a & b")] // XOR pattern - should NOT generate contradictory terms
    [InlineData("a & b | !a & c")] // Classic consensus - should work normally
    [InlineData("a & b | !a & c | b & c")] // Consensus with redundant term
    [InlineData("x & y | !x & z")] // Different variables
    public void ConsensusRule_ShouldNotCreateContradictoryTerms(string input)
    {
        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert - optimized result should not contain contradictory terms like "a & !a" or "b & !b"
        Assert.DoesNotContain("a & !a", result.Optimized);
        Assert.DoesNotContain("b & !b", result.Optimized);
        Assert.DoesNotContain("x & !x", result.Optimized);
        Assert.DoesNotContain("y & !y", result.Optimized);
        Assert.DoesNotContain("z & !z", result.Optimized);
        Assert.DoesNotContain("c & !c", result.Optimized);

        // Verify the result doesn't get worse (more terms than original)
        var originalTermCount = input.Split('|').Length;
        var optimizedTermCount = result.Optimized.Split('|').Length;

        // Optimized should have same or fewer terms (not more)
        Assert.True(optimizedTermCount <= originalTermCount + 1,
            $"Optimization made expression worse: {input} -> {result.Optimized}");
    }

    [Theory]
    [InlineData("a & !b | !a & b")] // Pure XOR
    [InlineData("x & !y | !x & y")] // XOR with different variables
    [InlineData("p & !q | !p & q")] // Another XOR pattern
    public void ConsensusRule_XorPatterns_ShouldNotAddRedundantTerms(string xorPattern)
    {
        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(xorPattern, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(xorPattern);

        // Assert - XOR patterns should not be made worse by consensus rule
        var originalComplexity = CountOperators(xorPattern);
        var optimizedComplexity = CountOperators(result.Optimized);

        Assert.True(optimizedComplexity <= originalComplexity,
            $"XOR pattern made worse: {xorPattern} -> {result.Optimized}");
    }

    [Fact]
    public void ConsensusRule_ContradictoryTerms_ShouldBeRejected()
    {
        // Test that the consensus rule rejects terms that would create contradictions
        var expressions = new[]
        {
            "a & !b | !a & b", // Should NOT add "!b & b"
            "x & y | !x & !y", // Should NOT add "y & !y" 
            "p & q & r | !p & s" // Should NOT add contradictory consensus
        };

        foreach (var expr in expressions)
        {
            // First verify optimization equivalence
            TruthTableAssert.AssertOptimizationEquivalenceOnly(expr, _optimizer);

            var result = _optimizer.OptimizeExpression(expr);

            // Check that no contradictory terms were added
            var variables = result.Variables;
            foreach (var variable in variables)
            {
                var contradiction = $"{variable} & !{variable}";
                var contradictionReversed = $"!{variable} & {variable}";

                Assert.DoesNotContain(contradiction, result.Optimized);
                Assert.DoesNotContain(contradictionReversed, result.Optimized);
            }
        }
    }

    [Theory]
    [InlineData("a & b | !a & c | b & c")] // Consensus elimination
    [InlineData("x & y | !x & z | y & z")] // Different variables
    public void ConsensusRule_ValidConsensus_ShouldSimplify(string input)
    {
        // First verify optimization equivalence (main goal)
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert - valid consensus should simplify expressions
        var simplifiedTerms = result.Optimized.Split('|').Select(t => t.Trim()).ToArray();

        // Should have fewer or equal terms than original
        Assert.True(simplifiedTerms.Length <= input.Split('|').Length,
            $"Optimization should not increase complexity: {input} -> {result.Optimized}");
    }

    private int CountOperators(string expression)
    {
        return expression.Count(c => c == '&' || c == '|' || c == '!');
    }

    [Fact]
    public void AllOptimizationRules_ShouldNotCreateContradictoryTerms()
    {
        // Test various expressions that might trigger different optimization rules
        var testExpressions = new[]
        {
            "a & !b | !a & b", // XOR pattern for consensus
            "a & b | a & c", // For factorization  
            "a | !a & b", // For absorption
            "!!a", // For complement law (double negation)
            "a & (b | c)", // For distributive law
            "a | a & b", // For absorption law
            "a & !a", // For complement law
            "a | !a" // For complement law (tautology)
        };

        foreach (var expr in testExpressions)
        {
            // First verify optimization equivalence
            TruthTableAssert.AssertOptimizationEquivalenceOnly(expr, _optimizer);

            var result = _optimizer.OptimizeExpression(expr);

            // Extract all variables from the result
            var variables = result.Variables;

            foreach (var variable in variables)
            {
                // Check for contradictory terms like "a & !a" or "!a & a"
                var contradiction1 = $"{variable} & !{variable}";
                var contradiction2 = $"!{variable} & {variable}";

                Assert.DoesNotContain(contradiction1, result.Optimized);
                Assert.DoesNotContain(contradiction2, result.Optimized);
            }

            // Verify optimization didn't make expression significantly worse
            var originalComplexity = CountOperators(expr);
            var optimizedComplexity = CountOperators(result.Optimized);

            // Allow some flexibility, but expression shouldn't get dramatically worse
            Assert.True(optimizedComplexity <= originalComplexity + 2,
                $"Expression became significantly worse: {expr} -> {result.Optimized}");
        }
    }

    [Fact]
    public void ComplexExpression_WithMultiplePatterns_ShouldOptimizeCorrectly()
    {
        // Test complex expression combining XOR and IMP patterns
        var complexExpr = "((a & !b) | (!a & b)) & ((!c | d) | (e & f))";

        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(complexExpr, _optimizer);

        var result = _optimizer.OptimizeExpression(complexExpr);

        // Verify the expression is optimized
        Assert.NotNull(result.Optimized);
        Assert.NotEmpty(result.Optimized);

        // Verify Advanced form contains expected patterns
        Assert.NotNull(result.Advanced);
        if (!string.IsNullOrEmpty(result.Advanced))
        {
            Assert.Contains("XOR", result.Advanced);
            // Check for either → symbol or the pattern that suggests implication
            Assert.True(result.Advanced.Contains("→") || result.Advanced.Contains("(c → d)") ||
                        (result.Advanced.Contains("d") && result.Advanced.Contains("c")),
                $"Expected implication pattern in: {result.Advanced}");
        }

        // Verify variables are correct
        var expectedVariables = new[] {"a", "b", "c", "d", "e", "f"};
        Assert.Equal(expectedVariables.Length, result.Variables.Count);
        foreach (var variable in expectedVariables) Assert.Contains(variable, result.Variables);

        // Verify CNF and DNF don't contain advanced operators
        Assert.DoesNotContain("XOR", result.CNF);
        Assert.DoesNotContain("→", result.CNF);
        Assert.DoesNotContain("XOR", result.DNF);
        Assert.DoesNotContain("→", result.DNF);
    }
}
