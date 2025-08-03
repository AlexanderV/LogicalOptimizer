using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for checking truth table functionality
/// </summary>
public class TruthTableTests
{
    [Fact]
    public void TruthTable_SimpleVariable_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Single(truthTable.Variables);
        Assert.Equal("a", truthTable.Variables[0]);
        Assert.Equal(2, truthTable.Results.Count);
        Assert.Equal("01", truthTable.GetResultsString()); // F, T
    }

    [Fact]
    public void TruthTable_AndOperation_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a & b";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(2, truthTable.Variables.Count);
        Assert.Contains("a", truthTable.Variables);
        Assert.Contains("b", truthTable.Variables);
        Assert.Equal("0001", truthTable.GetResultsString()); // FF, FT, TF, TT -> F, F, F, T
    }

    [Fact]
    public void TruthTable_OrOperation_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a | b";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(2, truthTable.Variables.Count);
        Assert.Equal("0111", truthTable.GetResultsString()); // FF, FT, TF, TT -> F, T, T, T
    }

    [Fact]
    public void TruthTable_NotOperation_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "!a";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Single(truthTable.Variables);
        Assert.Equal("10", truthTable.GetResultsString()); // F -> T, T -> F
    }

    [Fact]
    public void TruthTable_EquivalentExpressions_ShouldBeEqual()
    {
        // Arrange
        var expr1 = "a & b";
        var expr2 = "b & a"; // Commutativity

        // Act
        var table1 = TruthTable.Generate(expr1);
        var table2 = TruthTable.Generate(expr2);

        // Assert
        Assert.True(table1.IsEquivalentTo(table2));
        Assert.True(TruthTable.AreEquivalent(expr1, expr2));
    }

    [Fact]
    public void TruthTable_DeMorganLaw_ShouldBeEquivalent()
    {
        // Arrange
        var expr1 = "!(a & b)";
        var expr2 = "!a | !b";

        // Act & Assert
        Assert.True(TruthTable.AreEquivalent(expr1, expr2));
    }

    [Fact]
    public void TruthTable_Tautology_ShouldBeDetected()
    {
        // Arrange
        var expression = "a | !a";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.True(truthTable.IsTautology());
        Assert.Equal("11", truthTable.GetResultsString()); // Always true
    }

    [Fact]
    public void TruthTable_Contradiction_ShouldBeDetected()
    {
        // Arrange
        var expression = "a & !a";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.True(truthTable.IsContradiction());
        Assert.Equal("00", truthTable.GetResultsString()); // Always false
    }

    [Fact]
    public void TruthTable_ComplexExpression_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "(a & b) | (!a & c)";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(3, truthTable.Variables.Count);
        Assert.Contains("a", truthTable.Variables);
        Assert.Contains("b", truthTable.Variables);
        Assert.Contains("c", truthTable.Variables);
        // F F F -> F, F F T -> T, F T F -> F, F T T -> T
        // T F F -> F, T F T -> F, T T F -> T, T T T -> T
        Assert.Equal("01010011", truthTable.GetResultsString());
    }
}