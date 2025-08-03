using Xunit;

namespace LogicalOptimizer.Tests;

public class EdgeCaseTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a", "a")]
    [InlineData("!a", "!a")]
    public void EdgeCases_SingleVariables_ShouldRemainUnchanged(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("!1", "0")]
    [InlineData("!0", "1")]
    public void EdgeCases_Constants_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("variable123", "variable123")]
    [InlineData("var_with_underscores", "var_with_underscores")]
    public void EdgeCases_LongVariableNames_ShouldRemainUnchanged(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("((((a))))", "a")]
    [InlineData("!(!(!(!a)))", "a")]
    public void EdgeCases_DeepNesting_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & a & a & a", "a")]
    [InlineData("a | a | a | a", "a")]
    public void EdgeCases_LongChains_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & !a & b", "0")]
    [InlineData("(a & !a) | b", "b")]
    [InlineData("a & b & !a & c", "0")]
    public void EdgeCases_ContradictoryExpressions_ShouldOptimizeToZero(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | !a | b", "1")]
    [InlineData("(a | !a) & b", "b")]
    [InlineData("a | b | !a | c", "1")]
    public void EdgeCases_Tautologies_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Smart commutativity changed order
    [InlineData("a | !a & b", "a | b")] // Extended absorption now works!
    [InlineData("!!a", "a")] // Double negation
    [InlineData("!!!a", "!a")] // Triple negation
    [InlineData("!(!a & !b)", "a | b")] // De Morgan's law
    [InlineData("!(a | b)", "!a & !b")] // De Morgan's law
    public void EdgeCases_AdditionalOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 1", "a")] // Neutral element for AND
    [InlineData("a & 0", "0")] // Absorbing element for AND
    [InlineData("a | 1", "1")] // Absorbing element for OR  
    [InlineData("a | 0", "a")] // Neutral element for OR
    [InlineData("1 & a", "a")] // Commutativity of neutral element
    [InlineData("0 | a", "a")] // Commutativity of neutral element
    public void EdgeCases_ConstantOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void EdgeCases_NoDoubleParenthesesInAnyOptimization_ShouldPass()
    {
        // Arrange
        var testCases = new[]
        {
            "a & b | a & c",
            "x & y | x & z",
            "(p | q) & (p | r)",
            "a & (b | c | d)"
        };

        foreach (var testCase in testCases)
        {
            // Act & Assert - check both optimization and equivalence
            TruthTableAssert.AssertOptimizationEquivalenceOnly(testCase, _optimizer);

            var result = _optimizer.OptimizeExpression(testCase);
            Assert.DoesNotContain("((", result.Optimized);
            Assert.DoesNotContain("))", result.Optimized);
        }
    }
}
