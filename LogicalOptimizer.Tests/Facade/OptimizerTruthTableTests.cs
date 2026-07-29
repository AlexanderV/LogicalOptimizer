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
    [InlineData("a & b | a & c", "a & (b | c)")] // Pinned factored output
    [InlineData("(a | b) & (a | c)", "a | b & c")] // dual: factor across the AND-of-ORs
    [InlineData("x & y | x & z", "x & (y | z)")]
    [InlineData("(p | q) & (p | r)", "p | q & r")]
    public void Optimizer_Factorization_ShouldMaintainEquivalence(string input, string expected)
    {
        // Act & Assert - the factored form is deterministic and knowable; pinning it (not just
        // equivalence) is what catches a factorization that silently regressed to flat SOP
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
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
    // ("a & (b | c) & d" lives in OptimizerTests.Optimizer_SmartCommutativity — not duplicated here)
    [InlineData("(a & b) | (c & d) | (e & f)", "a & b | c & d | e & f")] // Multiple operations
    [InlineData("!(a & b) | (c & d)", "!a | !b | c & d")] // Mixed operations (De Morgan spread)
    public void Optimizer_ComplexExpressions_ShouldMaintainEquivalence(string input, string expected)
    {
        // Act & Assert - the canonical optimized form is deterministic here, so pin it: an
        // equivalence-only check would accept a worse-but-equivalent rewrite
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void Optimizer_ConsensusRule_ShouldMaintainEquivalence()
    {
        // Classic consensus: a & b | !a & c | b & c => a & b | !a & c. Pinning the exact
        // canonical output (verified via CLI: "a & b | c & !a") subsumes both the
        // equivalence check and the consensus-removal intent (the redundant "b & c" term
        // is gone), unlike a bare substring-absence check.
        TruthTableAssert.AssertOptimizationEquivalence("a & b | !a & c | b & c", "a & b | c & !a", _optimizer);
    }

    [Fact]
    public void Optimizer_TruthTables_ShouldBePopulatedAndFlagTautologyContradiction()
    {
        // Tautology: table is populated and flagged
        var tautology = _optimizer.OptimizeExpression("a | !a", true);
        Assert.NotNull(tautology.OriginalTruthTable);
        Assert.True(tautology.OriginalTruthTable!.IsTautology());
        Assert.False(tautology.OriginalTruthTable!.IsContradiction());

        // Contradiction: table is populated and flagged
        var contradiction = _optimizer.OptimizeExpression("a & !a", true);
        Assert.NotNull(contradiction.OriginalTruthTable);
        Assert.True(contradiction.OriginalTruthTable!.IsContradiction());
        Assert.False(contradiction.OriginalTruthTable!.IsTautology());

        // Ordinary expression: table is populated, neither flag set
        var ordinary = _optimizer.OptimizeExpression("a", true);
        Assert.NotNull(ordinary.OriginalTruthTable);
        Assert.False(ordinary.OriginalTruthTable!.IsTautology());
        Assert.False(ordinary.OriginalTruthTable!.IsContradiction());
    }
}
