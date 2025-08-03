using Xunit;

namespace LogicalOptimizer.Tests;

public class ConsoleTestedCasesTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Fact]
    public void ConsoleTested_DoubleParenthesesFix_ShouldWork()
    {
        // This test was added after fixing the double parentheses issue
        // Previously: "a & b | a & c" gave "a & ((b | c))"
        // Now should give: "a & (b | c)"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a & b | a & c", "a & (b | c)", _optimizer);
    }

    [Fact]
    public void ConsoleTested_ReverseFactorization_ShouldWork()
    {
        // This test checks reverse factorization
        // "(a | b) & (a | c)" should become "a | (b & c)"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("(a | b) & (a | c)", "a | (b & c)", _optimizer);
    }

    [Fact]
    public void ConsoleTested_TautologyDetection_ShouldWork()
    {
        // This test was added after fixing tautology recognition
        // "a | b | !a | c" should become "1"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a | b | !a | c", "1", _optimizer);
    }

    [Fact]
    public void ConsoleTested_ContradictionDetection_ShouldWork()
    {
        // This test was added after fixing contradiction recognition
        // "a & b & !a & c" should become "0"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a & b & !a & c", "0", _optimizer);
    }

    [Fact]
    public void ConsoleTested_ComplexExpressionPreservation_ShouldWork()
    {
        // This test checks that complex expressions are correctly optimized
        // "a & (b | c) & d" now becomes "a & d & (b | c)" thanks to smart commutativity

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a & (b | c) & d", "a & d & (b | c)", _optimizer);
    }

    [Fact]
    public void ConsoleTested_PartialAbsorption_ShouldWork()
    {
        // This test is for the case "a | !a & b" 
        // Now should be optimized to "a | b" thanks to extended absorption

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a | !a & b", "a | b", _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    [InlineData("a | b | !a | c", "1")]
    [InlineData("a & b & !a & c", "0")]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Updated result
    [InlineData("a | !a & b", "a | b")] // New improvement: extended absorption
    [InlineData("a & (!a | b)", "a & b")] // New improvement: reverse extended absorption
    [InlineData("a | b & !a", "a | b")] // New improvement: commutative version
    public void ConsoleTested_AllCases_ShouldMatchExpectedResults(string input, string expected)
    {
        // Combined test of all cases verified through console

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | !a & b", "a | b")]
    [InlineData("a & (!a | b)", "a & b")]
    [InlineData("a | b & !a", "a | b")]
    [InlineData("a & (b | !a)", "a & b")]
    [InlineData("x | !x & y & z", "x | y & z")]
    [InlineData("p & (!p | q | r)", "p & (q | r)")]
    public void ConsoleTested_ExtendedAbsorption_ShouldWork(string input, string expected)
    {
        // Tests for extended absorption: A | (!A & B) → A | B and A & (!A | B) → A & B

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }
}
