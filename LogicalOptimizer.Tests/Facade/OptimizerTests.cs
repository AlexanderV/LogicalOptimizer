using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the basic optimization functionality 
/// </summary>
public class OptimizerTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    // NOTE: Idempotent/constant/negation/DeMorgan/factorization rows and the two
    // extended-absorption rows previously here were exact duplicates (same input →
    // same pinned output, same AssertOptimizationEquivalence strength) of
    // OptimizerTruthTableTests, which is their single home. Only cases unique to this
    // file are kept below.

    [Theory]
    [InlineData("a | a & b", "a")]
    [InlineData("a & (a | b)", "a")]
    public void Optimizer_AbsorptionRules_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void Optimizer_ParityStyleDnf_FactorizationBeatsFlatSop()
    {
        // v1 quality pin: the 3-variable parity DNF (12 literals as flat SOP, which is
        // already the minimal two-level form) must factor into a 10-literal multi-level
        // form. Guards the rewrite-engine growth guard: factoring trades node count for
        // literal count, so a nodes-only guard would wrongly roll it back under the
        // n-ary cost model (node = 1 per n-ary gate).
        var result = _optimizer.OptimizeExpression("a & !b & !c | !a & b & !c | !a & !b & c | a & b & c");

        var optimizedAst = new FormulaFactory().Parse(result.Optimized);
        var literals = AstMetrics.CountLiterals(optimizedAst);
        Assert.Equal(10, literals);
        TruthTableAssert.AssertEquivalence(result.Original, result.Optimized);
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
