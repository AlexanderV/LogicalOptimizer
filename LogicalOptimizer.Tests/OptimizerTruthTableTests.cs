using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Extended optimization tests with truth table verification
/// </summary>
public class OptimizerTruthTableTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & a", "a")]
    [InlineData("a | a", "a")]
    public void Optimizer_IdempotentLaws_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 1", "a")]
    [InlineData("a | 0", "a")]
    [InlineData("1 & a", "a")]
    [InlineData("0 | a", "a")]
    public void Optimizer_NeutralElements_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 0", "0")]
    [InlineData("a | 1", "1")]
    [InlineData("0 & a", "0")]
    [InlineData("1 | a", "1")]
    public void Optimizer_AbsorbingElements_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & !a", "0")]
    [InlineData("a | !a", "1")]
    [InlineData("!a & a", "0")]
    [InlineData("!a | a", "1")]
    public void Optimizer_ComplementLaws_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!!a", "a")]
    [InlineData("!!!a", "!a")]
    [InlineData("!!!!a", "a")]
    public void Optimizer_DoubleNegation_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!(a & b)", "!a | !b")]
    [InlineData("!(a | b)", "!a & !b")]
    [InlineData("!(!a & !b)", "a | b")]
    [InlineData("!(!a | !b)", "a & b")]
    public void Optimizer_DeMorganLaws_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (a | b)", "a")]
    [InlineData("a | (a & b)", "a")]
    [InlineData("(a | b) & a", "a")]
    [InlineData("(a & b) | a", "a")]
    public void Optimizer_AbsorptionLaws_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c")]
    [InlineData("(a | b) & (a | c)")]
    [InlineData("x & y | x & z")]
    [InlineData("(p | q) & (p | r)")]
    public void Optimizer_Factorization_ShouldMaintainEquivalence(string input)
    {
        // Act & Assert - only check equivalence, not exact match
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);
    }

    [Theory]
    [InlineData("a | b | !a | c", "1")] // Tautology
    [InlineData("a & b & !a & c", "0")] // Contradiction
    [InlineData("x | !x", "1")] // Simple tautology
    [InlineData("x & !x", "0")] // Simple contradiction
    public void Optimizer_TautologiesAndContradictions_ShouldOptimizeWithTruthTableEquivalence(string input,
        string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | !a & b", "a | b")] // Extended absorption
    [InlineData("a & (!a | b)", "a & b")] // Reverse extended absorption
    [InlineData("a | b & !a", "a | b")] // Commutative version
    [InlineData("a & (b | !a)", "a & b")] // Commutative version
    public void Optimizer_ExtendedAbsorption_ShouldOptimizeWithTruthTableEquivalence(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (b | c) & d")] // Complex expression with commutativity
    [InlineData("(a & b) | (c & d) | (e & f)")] // Multiple operations
    [InlineData("!(a & b) | (c & d)")] // Mixed operations
    public void Optimizer_ComplexExpressions_ShouldMaintainEquivalence(string input)
    {
        // Act & Assert - only check equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);
    }

    [Fact]
    public void Optimizer_ConsensusRule_ShouldMaintainEquivalence()
    {
        // Arrange
        var input = "a & b | !a & c | b & c";

        // Act
        var result = _optimizer.OptimizeExpression(input, true);

        // Assert - check equivalence through truth tables
        Assert.True(result.IsEquivalent(),
            $"Optimized expression '{result.Optimized}' is not equivalent to original '{result.Original}'");
    }

    [Fact]
    public void Optimizer_AllBasicLaws_ShouldMaintainTruthTableEquivalence()
    {
        // Arrange - set of various expressions for testing
        var testCases = new[]
        {
            "a & a", "a | a", // Idempotence
            "a & 1", "a | 0", // Neutral elements
            "a & 0", "a | 1", // Absorbing elements
            "a & !a", "a | !a", // Complementarity
            "!!a", "!!!a", // Double negation
            "!(a & b)", "!(a | b)", // De Morgan's laws
            "a & (a | b)", "a | (a & b)", // Absorption
            "a & b | a & c", "(a | b) & (a | c)" // Factorization
        };

        foreach (var testCase in testCases)
            // Act & Assert
            TruthTableAssert.AssertOptimizationEquivalenceOnly(testCase, _optimizer);
    }

    [Theory]
    [InlineData("a", false)] // Variable is not a tautology
    [InlineData("a | !a", true)] // Tautology
    [InlineData("a & b | !a | !b", true)] // Complex tautology
    [InlineData("1", true)] // True constant
    public void TruthTable_TautologyDetection_ShouldWorkCorrectly(string expression, bool expectedTautology)
    {
        // Act
        var result = _optimizer.OptimizeExpression(expression, true); // Include tables

        // Assert
        Assert.Equal(expectedTautology, result.OriginalTruthTable?.IsTautology());
    }

    [Theory]
    [InlineData("a", false)] // Variable is not a contradiction
    [InlineData("a & !a", true)] // Contradiction
    [InlineData("a & b & (!a | !b)", true)] // Complex contradiction
    [InlineData("0", true)] // False constant
    public void TruthTable_ContradictionDetection_ShouldWorkCorrectly(string expression, bool expectedContradiction)
    {
        // Act
        var result = _optimizer.OptimizeExpression(expression, true); // Include tables

        // Assert
        Assert.Equal(expectedContradiction, result.OriginalTruthTable?.IsContradiction());
    }

    [Fact]
    public void TruthTable_ComplexEquivalence_ShouldBeDetected()
    {
        // Arrange - check equivalence of complex expressions
        var expr1 = "(a & b) | (!a & c) | (b & c)";
        var expr2 = "(a & b) | (!a & c)"; // After applying consensus rule

        // Act & Assert
        Assert.True(TruthTable.AreEquivalent(expr1, expr2),
            "Complex expressions should be equivalent after consensus rule application");
    }
}