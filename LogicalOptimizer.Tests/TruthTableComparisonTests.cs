using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for verifying TruthTable comparison functionality and tabular output
/// </summary>
public class TruthTableComparisonTests
{
    [Fact]
    public void TruthTable_ShouldShowTabularFormat()
    {
        // Arrange
        var expression = "a & b";

        // Act
        var truthTable = TruthTable.Generate(expression);
        var tableString = truthTable.ToString();

        // Assert
        Assert.Contains("| a | b | Result |", tableString);
        Assert.Contains("| F | F | F |", tableString);
        Assert.Contains("| T | T | T |", tableString);
    }

    [Fact]
    public void TruthTable_CompareExpressions_EquivalentExpressions()
    {
        // Arrange
        var original = "a & b";
        var optimized = "b & a"; // Should be equivalent

        // Act
        var areEquivalent = TruthTable.AreEquivalent(original, optimized);
        var comparison = TruthTable.CompareExpressions(original, optimized);

        // Assert
        Assert.True(areEquivalent);
        Assert.Contains("Equivalent: True", comparison);
        Assert.Contains("| a | b | Expr1 | Expr2 | Match |", comparison);
    }

    [Fact]
    public void TruthTable_CompareExpressions_DifferentExpressions()
    {
        // Arrange
        var original = "a & b";
        var optimized = "a | b"; // Different logic

        // Act
        var areEquivalent = TruthTable.AreEquivalent(original, optimized);
        var comparison = TruthTable.CompareExpressions(original, optimized);

        // Assert
        Assert.False(areEquivalent);
        Assert.Contains("Equivalent: False", comparison);
        Assert.Contains("✗", comparison); // Should show mismatch symbols
    }

    [Fact]
    public void TruthTable_VerifyOptimizationEquivalence_ComplexExpression()
    {
        // Arrange  
        var original = "a & b | a & c";
        var optimized = "a & (b | c)"; // Should be equivalent factorization

        // Act
        var table1 = TruthTable.Generate(original);
        var table2 = TruthTable.Generate(optimized);
        var areEquivalent = table1.IsEquivalentTo(table2);

        // Assert
        Assert.True(areEquivalent);
        Assert.True(TruthTable.AreEquivalent(original, optimized));
    }

    [Fact]
    public void TruthTable_TestAllLogicalProperties()
    {
        // Test tautology
        var tautology = TruthTable.Generate("a | !a");
        Assert.True(tautology.IsTautology());
        Assert.True(tautology.IsSatisfiable());
        Assert.False(tautology.IsContradiction());

        // Test contradiction
        var contradiction = TruthTable.Generate("a & !a");
        Assert.True(contradiction.IsContradiction());
        Assert.False(contradiction.IsSatisfiable());
        Assert.False(contradiction.IsTautology());

        // Test satisfiable but not tautology
        var satisfiable = TruthTable.Generate("a & b");
        Assert.True(satisfiable.IsSatisfiable());
        Assert.False(satisfiable.IsTautology());
        Assert.False(satisfiable.IsContradiction());
    }

    [Fact]
    public void TruthTable_ComparisonReportFormat()
    {
        // Arrange
        var expr1 = "a & b";
        var expr2 = "a | b";

        // Act
        var comparison = TruthTable.CompareExpressions(expr1, expr2);

        // Assert
        Assert.Contains("=== Truth Table Comparison ===", comparison);
        Assert.Contains($"Expression 1: {expr1}", comparison);
        Assert.Contains($"Expression 2: {expr2}", comparison);
        Assert.Contains("| a | b | Expr1 | Expr2 | Match |", comparison);
        Assert.Contains("| F | F | F | F | ✓ |", comparison); // Both false when a=F, b=F
        Assert.Contains("| T | T | T | T | ✓ |", comparison); // Both true when a=T, b=T
        Assert.Contains("| F | T | F | T | ✗ |", comparison); // Different when a=F, b=T
        Assert.Contains("| T | F | F | T | ✗ |", comparison); // Different when a=T, b=F
    }
}