using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for OptimizationResult class - equivalence checking and ToString formatting
/// </summary>
public class OptimizationResultTests
{
    [Fact]
    public void OptimizationResult_DefaultConstructor_ShouldInitializeProperties()
    {
        // Act
        var result = new OptimizationResult();

        // Assert
        Assert.Equal("", result.Original);
        Assert.Equal("", result.Optimized);
        Assert.Equal("", result.CNF);
        Assert.Equal("", result.DNF);
        Assert.Equal("", result.Advanced);
        Assert.Empty(result.Variables);
        Assert.Null(result.Metrics);
        Assert.Null(result.OriginalTruthTable);
        Assert.Null(result.OptimizedTruthTable);
    }

    [Fact]
    public void IsEquivalent_WithoutTruthTables_ShouldUseTruthTableAreEquivalent()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            OriginalTruthTable = null,
            OptimizedTruthTable = null
        };

        // Act
        var isEquivalent = result.IsEquivalent();

        // Assert
        Assert.True(isEquivalent); // Same expressions should be equivalent
    }

    [Fact]
    public void IsEquivalent_WithTruthTables_ShouldUseTruthTableComparison()
    {
        // Arrange
        var variables = new List<string> { "a", "b" };
        var results = new List<bool> { false, false, false, true }; // AND truth table
        var originalTable = new TruthTable(variables, results);
        var optimizedTable = new TruthTable(variables, results);

        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            OriginalTruthTable = originalTable,
            OptimizedTruthTable = optimizedTable
        };

        // Act
        var isEquivalent = result.IsEquivalent();

        // Assert
        Assert.True(isEquivalent); // Same truth tables should be equivalent
    }

    [Fact]
    public void IsEquivalent_WithDifferentTruthTables_ShouldReturnFalse()
    {
        // Arrange
        var variables = new List<string> { "a", "b" };
        var andResults = new List<bool> { false, false, false, true }; // AND truth table
        var orResults = new List<bool> { false, true, true, true }; // OR truth table
        var originalTable = new TruthTable(variables, andResults);
        var optimizedTable = new TruthTable(variables, orResults);

        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a | b",
            OriginalTruthTable = originalTable,
            OptimizedTruthTable = optimizedTable
        };

        // Act
        var isEquivalent = result.IsEquivalent();

        // Assert
        Assert.False(isEquivalent); // Different truth tables should not be equivalent
    }

    [Fact]
    public void IsEquivalent_WithNullOriginalTruthTable_ShouldUseFallbackMethod()
    {
        // Arrange
        var variables = new List<string> { "a", "b" };
        var results = new List<bool> { false, false, false, true }; // AND truth table
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "b & a", // Equivalent but different order
            OriginalTruthTable = null,
            OptimizedTruthTable = new TruthTable(variables, results)
        };

        // Act
        var isEquivalent = result.IsEquivalent();

        // Assert
        Assert.True(isEquivalent); // Should fall back to TruthTable.AreEquivalent method
    }

    [Fact]
    public void IsEquivalent_WithNullOptimizedTruthTable_ShouldUseFallbackMethod()
    {
        // Arrange
        var variables = new List<string> { "a", "b" };
        var results = new List<bool> { false, false, false, true }; // AND truth table
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "b & a", // Equivalent but different order
            OriginalTruthTable = new TruthTable(variables, results),
            OptimizedTruthTable = null
        };

        // Act
        var isEquivalent = result.IsEquivalent();

        // Assert
        Assert.True(isEquivalent); // Should fall back to TruthTable.AreEquivalent method
    }

    private static string Conjunction(IEnumerable<int> indices) =>
        string.Join(" & ", indices.Select(i => $"x{i:D2}"));

    [Fact]
    public void IsEquivalent_BeyondTwentyVariables_UsesSatCheckerInsteadOfThrowing()
    {
        // Past the 20-variable truth-table limit IsEquivalent must route through the scalable
        // SAT checker (miter/UNSAT proof), not throw ArgumentException. 25 variables, the same
        // conjunction with its operands reversed → provably equivalent.
        var result = new OptimizationResult
        {
            Original = Conjunction(Enumerable.Range(1, 25)),
            Optimized = Conjunction(Enumerable.Range(1, 25).Reverse())
        };

        Assert.True(result.IsEquivalent());
    }

    [Fact]
    public void IsEquivalent_BeyondTwentyVariables_NonEquivalent_ReturnsFalse()
    {
        // Dropping x25 changes the function; the SAT miter finds the differing assignment.
        var result = new OptimizationResult
        {
            Original = Conjunction(Enumerable.Range(1, 25)),
            Optimized = Conjunction(Enumerable.Range(1, 24))
        };

        Assert.False(result.IsEquivalent());
    }

    [Fact]
    public void CheckEquivalence_IsThreeValued_ExposesProofAndCounterexample()
    {
        // CheckEquivalence preserves the full verdict that IsEquivalent's bool collapses.
        var equivalent = new OptimizationResult
        {
            Original = Conjunction(Enumerable.Range(1, 25)),
            Optimized = Conjunction(Enumerable.Range(1, 25).Reverse())
        };
        Assert.Equal(true, equivalent.CheckEquivalence().AreEquivalent);

        var refuted = new OptimizationResult
        {
            Original = Conjunction(Enumerable.Range(1, 25)),
            Optimized = Conjunction(Enumerable.Range(1, 24))
        };
        var verdict = refuted.CheckEquivalence();
        Assert.Equal(false, verdict.AreEquivalent);
        // A refutation carries a concrete assignment where the two sides differ.
        Assert.NotNull(verdict.Counterexample);
        Assert.True(verdict.Counterexample!.Count > 0);
    }

    [Fact]
    public void ToString_WithoutTruthTables_ShouldReturnBasicInfo()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            CNF = "a & b",
            DNF = "a & b",
            Variables = new List<string> { "a", "b" },
            OriginalTruthTable = null,
            OptimizedTruthTable = null
        };

        // Act
        var stringResult = result.ToString();

        // Assert
        Assert.Contains("Original: a & b", stringResult);
        Assert.Contains("Optimized: a & b", stringResult);
        Assert.Contains("CNF: a & b", stringResult);
        Assert.Contains("DNF: a & b", stringResult);
        Assert.Contains("Variables: [a, b]", stringResult);
        Assert.DoesNotContain("Equivalent:", stringResult);
        Assert.DoesNotContain("Truth Table:", stringResult);
    }

    [Fact]
    public void ToString_WithTruthTables_ShouldIncludeTruthTableInfo()
    {
        // Arrange
        var variables = new List<string> { "a", "b" };
        var results = new List<bool> { false, false, false, true }; // AND truth table
        var originalTable = new TruthTable(variables, results);
        var optimizedTable = new TruthTable(variables, results);

        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            CNF = "a & b",
            DNF = "a & b",
            Variables = new List<string> { "a", "b" },
            OriginalTruthTable = originalTable,
            OptimizedTruthTable = optimizedTable
        };

        // Act
        var stringResult = result.ToString();

        // Assert
        Assert.Contains("Original: a & b", stringResult);
        Assert.Contains("Optimized: a & b", stringResult);
        Assert.Contains("CNF: a & b", stringResult);
        Assert.Contains("DNF: a & b", stringResult);
        Assert.Contains("Variables: [a, b]", stringResult);
        Assert.Contains("Equivalent: True", stringResult);
        Assert.Contains("Original Truth Table:", stringResult);
        Assert.Contains("Optimized Truth Table:", stringResult);
    }

    [Fact]
    public void ToString_WithEmptyVariables_ShouldHandleCorrectly()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "1",
            Optimized = "1",
            CNF = "1",
            DNF = "1",
            Variables = new List<string>(),
            OriginalTruthTable = null,
            OptimizedTruthTable = null
        };

        // Act
        var stringResult = result.ToString();

        // Assert
        Assert.Contains("Variables: []", stringResult);
    }
}
