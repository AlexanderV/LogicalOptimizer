using Xunit;

namespace LogicalOptimizer.Tests;

public class ConsoleTestedCasesTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    // Only rows UNIQUE to this console-verified corpus remain; every other case here
    // duplicated OptimizerTests / OptimizerTruthTableTests / EdgeCaseTests verbatim at
    // equal strength and was removed. Each expected string below is confirmed against
    // the CLI output.
    [Theory]
    // ("(a | b) & (a | c)" → "a | b & c" is pinned in OptimizerTruthTableTests.Optimizer_Factorization,
    //  and "a & (b | c) & d" → "a & d & (b | c)" in OptimizerTests.Optimizer_SmartCommutativity —
    //  both were verbatim copies at equal strength, so they live there, not here.)
    [InlineData("x | !x & y & z", "x | y & z")] // Extended absorption within a larger AND term
    [InlineData("p & (!p | q | r)", "p & (q | r)")] // Reverse extended absorption within an OR term
    public void ConsoleTested_UniqueCases_ShouldMatchExpectedResults(string input, string expected)
    {
        // Cases verified through the console that are not covered elsewhere

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }
}
