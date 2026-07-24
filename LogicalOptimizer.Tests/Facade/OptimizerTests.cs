using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the basic optimization functionality 
/// </summary>
public class OptimizerTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & a", "a")]
    [InlineData("a | a", "a")]
    [InlineData("a & !a", "0")]
    [InlineData("a | !a", "1")]
    public void Optimizer_BasicRules_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 1", "a")]
    [InlineData("a & 0", "0")]
    [InlineData("a | 1", "1")]
    [InlineData("a | 0", "a")]
    [InlineData("1 & a", "a")]
    [InlineData("0 | a", "a")]
    public void Optimizer_ConstantRules_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!!a", "a")]
    [InlineData("!!!a", "!a")]
    [InlineData("!(!a & !b)", "a | b")]
    [InlineData("!(a | b)", "!a & !b")]
    public void Optimizer_NegationRules_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    public void Optimizer_FactorizationRules_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | a & b", "a")]
    [InlineData("a & (a | b)", "a")]
    [InlineData("a | !a & b", "a | b")]
    [InlineData("a & (!a | b)", "a & b")]
    public void Optimizer_AbsorptionRules_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("b & a | c & a", "a & (b | c)")] // Rearranged to enable factorization of common factor 'a'
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Smart commutativity groups simple factors first
    public void Optimizer_SmartCommutativity_ShouldRearrangeForBetterFactorization(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("(a & b) & c", "a & b & c")]
    [InlineData("(a | b) | c", "a | b | c")]
    public void Optimizer_AssociativeOptimization_ShouldFlattenExpressions(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void Optimizer_ComplexExpression_ShouldOptimizeCorrectly()
    {
        // Arrange
        var input = "a & b | a & c | !a & d";
        var expected = "d & !a | a & (b | c)";

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("!a")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("a & b & c")]
    public void Optimizer_AlreadyOptimal_ShouldRemainUnchanged(string expression)
    {
        // Act & Assert - already-optimal expressions must come back verbatim
        TruthTableAssert.AssertOptimizationEquivalence(expression, expression, _optimizer);
    }
}
