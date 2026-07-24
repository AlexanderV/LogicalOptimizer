using Xunit;

namespace LogicalOptimizer.Tests;

public class ConsoleTestedCasesTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")] // Double parentheses fix: previously gave "a & ((b | c))"
    [InlineData("(a | b) & (a | c)", "a | (b & c)")] // Reverse factorization
    [InlineData("a | b | !a | c", "1")] // Tautology detection
    [InlineData("a & b & !a & c", "0")] // Contradiction detection
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Complex expression preservation with smart commutativity
    [InlineData("a | !a & b", "a | b")] // Partial/extended absorption
    [InlineData("a & (!a | b)", "a & b")] // Reverse extended absorption
    [InlineData("a | b & !a", "a | b")] // Extended absorption, commutative version
    public void ConsoleTested_AllCases_ShouldMatchExpectedResults(string input, string expected)
    {
        // Combined test of all cases verified through console

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
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
