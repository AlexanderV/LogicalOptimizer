using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Specialized tests for checking correctness of truth table creation
/// </summary>
public class TruthTableGenerationTests
{
    [Fact]
    public void TruthTable_SingleVariable_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Single(truthTable.Variables);
        Assert.Equal("a", truthTable.Variables[0]);
        Assert.Equal(2, truthTable.Results.Count);
        Assert.Equal(2, truthTable.Rows.Count);

        // Check each row
        Assert.False(truthTable.Rows[0]["a"]); // F
        Assert.False(truthTable.Results[0]); // F

        Assert.True(truthTable.Rows[1]["a"]); // T
        Assert.True(truthTable.Results[1]); // T

        Assert.Equal("01", truthTable.GetResultsString());
    }

    [Fact]
    public void TruthTable_TwoVariables_AND_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a & b";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(2, truthTable.Variables.Count);
        Assert.Contains("a", truthTable.Variables);
        Assert.Contains("b", truthTable.Variables);
        Assert.Equal(4, truthTable.Results.Count);
        Assert.Equal(4, truthTable.Rows.Count);

        // Check each row: a=F,b=F -> F; a=F,b=T -> F; a=T,b=F -> F; a=T,b=T -> T
        var sortedVars = truthTable.Variables.OrderBy(v => v).ToList();
        for (var i = 0; i < 4; i++)
        {
            var row = truthTable.Rows[i];
            var aVal = row[sortedVars[0]]; // "a"
            var bVal = row[sortedVars[1]]; // "b"
            var expectedResult = aVal && bVal;
            Assert.Equal(expectedResult, truthTable.Results[i]);
        }

        Assert.Equal("0001", truthTable.GetResultsString());
    }

    [Fact]
    public void TruthTable_TwoVariables_OR_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a | b";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(2, truthTable.Variables.Count);
        Assert.Equal(4, truthTable.Results.Count);

        // Check each row: a=F,b=F -> F; a=F,b=T -> T; a=T,b=F -> T; a=T,b=T -> T
        var sortedVars = truthTable.Variables.OrderBy(v => v).ToList();
        for (var i = 0; i < 4; i++)
        {
            var row = truthTable.Rows[i];
            var aVal = row[sortedVars[0]]; // "a"
            var bVal = row[sortedVars[1]]; // "b"
            var expectedResult = aVal || bVal;
            Assert.Equal(expectedResult, truthTable.Results[i]);
        }

        Assert.Equal("0111", truthTable.GetResultsString());
    }

    [Fact]
    public void TruthTable_Negation_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "!a";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Single(truthTable.Variables);
        Assert.Equal("a", truthTable.Variables[0]);
        Assert.Equal(2, truthTable.Results.Count);

        // a=F -> T; a=T -> F
        Assert.False(truthTable.Rows[0]["a"]);
        Assert.True(truthTable.Results[0]);

        Assert.True(truthTable.Rows[1]["a"]);
        Assert.False(truthTable.Results[1]);

        Assert.Equal("10", truthTable.GetResultsString());
    }

    [Fact]
    public void TruthTable_ThreeVariables_ComplexExpression_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "(a & b) | c";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(3, truthTable.Variables.Count);
        Assert.Contains("a", truthTable.Variables);
        Assert.Contains("b", truthTable.Variables);
        Assert.Contains("c", truthTable.Variables);
        Assert.Equal(8, truthTable.Results.Count);
        Assert.Equal(8, truthTable.Rows.Count);

        // Check each row
        var sortedVars = truthTable.Variables.OrderBy(v => v).ToList();
        for (var i = 0; i < 8; i++)
        {
            var row = truthTable.Rows[i];
            var aVal = row[sortedVars[0]]; // "a"
            var bVal = row[sortedVars[1]]; // "b"
            var cVal = row[sortedVars[2]]; // "c"
            var expectedResult = (aVal && bVal) || cVal;
            Assert.Equal(expectedResult, truthTable.Results[i]);
        }

        // F,F,F->F; F,F,T->T; F,T,F->F; F,T,T->T; T,F,F->F; T,F,T->T; T,T,F->T; T,T,T->T
        Assert.Equal("01010111", truthTable.GetResultsString());
    }

    [Fact]
    public void TruthTable_Constants_ShouldGenerateCorrectTable()
    {
        // Test constant true
        var truthTableTrue = TruthTable.Generate("1");
        Assert.Empty(truthTableTrue.Variables);
        Assert.Single(truthTableTrue.Results);
        Assert.True(truthTableTrue.Results[0]);
        Assert.Equal("1", truthTableTrue.GetResultsString());

        // Test constant false
        var truthTableFalse = TruthTable.Generate("0");
        Assert.Empty(truthTableFalse.Variables);
        Assert.Single(truthTableFalse.Results);
        Assert.False(truthTableFalse.Results[0]);
        Assert.Equal("0", truthTableFalse.GetResultsString());
    }

    [Fact]
    public void TruthTable_MixedWithConstants_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a & 1"; // Should be equivalent to just "a"

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Single(truthTable.Variables);
        Assert.Equal("a", truthTable.Variables[0]);
        Assert.Equal(2, truthTable.Results.Count);

        // a=F -> F; a=T -> T (because a & 1 = a)
        Assert.Equal("01", truthTable.GetResultsString());
    }

    [Fact]
    public void TruthTable_ComplexNestedExpression_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "((a & b) | (!a & c)) & (d | !e)";

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(5, truthTable.Variables.Count);
        Assert.Contains("a", truthTable.Variables);
        Assert.Contains("b", truthTable.Variables);
        Assert.Contains("c", truthTable.Variables);
        Assert.Contains("d", truthTable.Variables);
        Assert.Contains("e", truthTable.Variables);
        Assert.Equal(32, truthTable.Results.Count); // 2^5 = 32
        Assert.Equal(32, truthTable.Rows.Count);

        // Check correctness of calculation for several random rows
        var sortedVars = truthTable.Variables.OrderBy(v => v).ToList();
        for (var i = 0; i < truthTable.Rows.Count; i++)
        {
            var row = truthTable.Rows[i];
            var aVal = row[sortedVars[0]]; // "a"
            var bVal = row[sortedVars[1]]; // "b"
            var cVal = row[sortedVars[2]]; // "c"
            var dVal = row[sortedVars[3]]; // "d"
            var eVal = row[sortedVars[4]]; // "e"

            var leftPart = (aVal && bVal) || (!aVal && cVal);
            var rightPart = dVal || !eVal;
            var expectedResult = leftPart && rightPart;

            Assert.Equal(expectedResult, truthTable.Results[i]);
        }
    }

    [Fact]
    public void TruthTable_EdgeCase_EmptyExpression_ShouldThrowException()
    {
        // Assert
        Assert.Throws<ArgumentException>(() => TruthTable.Generate(""));
        Assert.Throws<ArgumentException>(() => TruthTable.Generate("   "));
        Assert.Throws<ArgumentException>(() => TruthTable.Generate((string)null!));
    }

    [Fact]
    public void TruthTable_EdgeCase_InvalidExpression_ShouldThrowException()
    {
        // Arrange
        string[] invalidExpressions =
        [
            "a &",
            "| b",
            "a & & b",
            "((a)",
            "a & b)"
        ];

        foreach (var invalidExpression in invalidExpressions)
            // Assert (FormulaParseException is an ArgumentException)
            Assert.ThrowsAny<ArgumentException>(() => TruthTable.Generate(invalidExpression));
    }

    [Fact]
    public void TruthTable_SingleVariableMultipleOccurrences_ShouldGenerateCorrectTable()
    {
        // Arrange
        var expression = "a & a"; // Should be equivalent to just "a"

        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Single(truthTable.Variables);
        Assert.Equal("a", truthTable.Variables[0]);
        Assert.Equal(2, truthTable.Results.Count);

        // a=F -> F; a=T -> T (because a & a = a)
        Assert.Equal("01", truthTable.GetResultsString());
    }

    [Theory]
    [InlineData("a", 1, 2)]
    [InlineData("a & b", 2, 4)]
    [InlineData("a & b & c", 3, 8)]
    [InlineData("a & b & c & d", 4, 16)]
    [InlineData("x1 & x2 & x3 & x4", 4, 16)]
    public void TruthTable_VariableCount_ShouldDetermineTableSize(string expression, int expectedVarCount,
        int expectedRowCount)
    {
        // Act
        var truthTable = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(expectedVarCount, truthTable.Variables.Count);
        Assert.Equal(expectedRowCount, truthTable.Results.Count);
        Assert.Equal(expectedRowCount, truthTable.Rows.Count);
    }
}
