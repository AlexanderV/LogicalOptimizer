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
    [InlineData("a & b", "a & b")]
    [InlineData("a | b", "a | b")]
    [InlineData("a & b & c", "a & b & c")]
    public void Optimizer_CommutativeProperty_ShouldMaintainLogicalEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void Optimizer_SmartCommutativity_ShouldRearrangeForBetterFactorization()
    {
        // Test case where commutativity helps factorization
        var input = "b & a | c & a";  // Should be rearranged to enable factorization
        
        // The optimizer should recognize the common factor 'a' and factorize
        var result = _optimizer.OptimizeExpression(input);
        
        // Should result in factorized form: a & (b | c) or similar optimized form
        Assert.True(result.Optimized.Contains("a &") || result.Optimized.Contains("& a"));
        Assert.True(result.Optimized.Length < input.Length); // Should be shorter
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

    [Fact]
    public void Optimizer_AlreadyOptimal_ShouldRemainUnchanged()
    {
        // Arrange
        var expressions = new[] { "a", "a & b", "a | b", "!a" };

        foreach (var expr in expressions)
        {
            // Act & Assert
            TruthTableAssert.AssertOptimizationEquivalenceOnly(expr, _optimizer);
        }
    }
}
