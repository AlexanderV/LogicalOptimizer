using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for checking correctness of TruthTable class methods
/// </summary>
public class TruthTableMethodTests
{
    [Fact]
    public void TruthTable_GetResultsString_ShouldReturnCorrectFormat()
    {
        // Arrange
        var table = TruthTable.Generate("a & b");

        // Act
        var result = table.GetResultsString();

        // Assert
        Assert.Equal("0001", result);
        Assert.Equal(4, result.Length);
        Assert.All(result, c => Assert.True(c == '0' || c == '1'));
    }

    [Fact]
    public void TruthTable_IsTautology_ShouldDetectCorrectly()
    {
        // Arrange & Act & Assert
        Assert.True(TruthTable.Generate("a | !a").IsTautology());
        Assert.True(TruthTable.Generate("1").IsTautology());
        Assert.True(TruthTable.Generate("(a & b) | (!a | !b)").IsTautology());

        Assert.False(TruthTable.Generate("a").IsTautology());
        Assert.False(TruthTable.Generate("a & b").IsTautology());
        Assert.False(TruthTable.Generate("0").IsTautology());
    }

    [Fact]
    public void TruthTable_IsContradiction_ShouldDetectCorrectly()
    {
        // Arrange & Act & Assert
        Assert.True(TruthTable.Generate("a & !a").IsContradiction());
        Assert.True(TruthTable.Generate("0").IsContradiction());
        Assert.True(TruthTable.Generate("(a | b) & (!a & !b)").IsContradiction());

        Assert.False(TruthTable.Generate("a").IsContradiction());
        Assert.False(TruthTable.Generate("a & b").IsContradiction());
        Assert.False(TruthTable.Generate("1").IsContradiction());
    }

    [Fact]
    public void TruthTable_IsSatisfiable_ShouldDetectCorrectly()
    {
        // Arrange & Act & Assert
        Assert.True(TruthTable.Generate("a").IsSatisfiable());
        Assert.True(TruthTable.Generate("a & b").IsSatisfiable());
        Assert.True(TruthTable.Generate("a | !a").IsSatisfiable()); // Tautology is satisfiable
        Assert.True(TruthTable.Generate("1").IsSatisfiable());

        Assert.False(TruthTable.Generate("a & !a").IsSatisfiable()); // Contradiction is not satisfiable
        Assert.False(TruthTable.Generate("0").IsSatisfiable());
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_SameTable_ShouldReturnTrue()
    {
        // Arrange
        var table1 = TruthTable.Generate("a & b");
        var table2 = TruthTable.Generate("a & b");

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
        Assert.True(table2.IsEquivalentTo(table1)); // Symmetry
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_DifferentVariableOrder_ShouldReturnTrue()
    {
        // Arrange
        var table1 = TruthTable.Generate("a & b");
        var table2 = TruthTable.Generate("b & a");

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
        Assert.True(table2.IsEquivalentTo(table1));
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_DifferentExpressionsSameResult_ShouldReturnTrue()
    {
        // Arrange
        var table1 = TruthTable.Generate("a | (a & b)");
        var table2 = TruthTable.Generate("a");

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_DifferentResults_ShouldReturnFalse()
    {
        // Arrange
        var table1 = TruthTable.Generate("a & b");
        var table2 = TruthTable.Generate("a | b");

        // Act & Assert
        Assert.False(table1.IsEquivalentTo(table2));
        Assert.False(table2.IsEquivalentTo(table1));
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_NullTable_ShouldReturnFalse()
    {
        // Arrange
        var table = TruthTable.Generate("a");

        // Act & Assert
        Assert.False(table.IsEquivalentTo(null!));
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_DifferentVariableSets_ShouldWork()
    {
        // Arrange
        var table1 = TruthTable.Generate("a"); // Only variable 'a'
        var table2 = TruthTable.Generate("a & 1"); // Variable 'a' and constant

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
    }

    [Fact]
    public void TruthTable_IsEquivalentTo_Constants_ShouldWork()
    {
        // Arrange
        var table1 = TruthTable.Generate("1");
        var table2 = TruthTable.Generate("a | !a");

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
    }

    [Fact]
    public void TruthTable_AreEquivalent_StaticMethod_ShouldWork()
    {
        // Act & Assert
        Assert.True(TruthTable.AreEquivalent("a & b", "b & a"));
        Assert.True(TruthTable.AreEquivalent("a | !a", "1"));
        Assert.True(TruthTable.AreEquivalent("a & !a", "0"));

        Assert.False(TruthTable.AreEquivalent("a & b", "a | b"));
        Assert.False(TruthTable.AreEquivalent("a", "!a"));
    }

    [Fact]
    public void TruthTable_AreEquivalent_WithInvalidExpression_ShouldReturnFalse()
    {
        // Act & Assert
        Assert.False(TruthTable.AreEquivalent("a &", "a"));
        Assert.False(TruthTable.AreEquivalent("a", "b &"));
        Assert.False(TruthTable.AreEquivalent("invalid", "also invalid"));
    }

    [Fact]
    public void TruthTable_ToString_ShouldContainAllElements()
    {
        // Arrange
        var table = TruthTable.Generate("a & b");

        // Act
        var result = table.ToString();

        // Assert
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("Result", result);
        Assert.Contains("F", result);
        Assert.Contains("T", result);
        Assert.Contains("---", result); // Separator

        // Should contain 4 data rows (2^2 variables)
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 6); // Header + separator + 4 data rows
    }

    [Fact]
    public void TruthTable_ToString_EmptyTable_ShouldReturnMessage()
    {
        // Arrange - create empty table (technically impossible via Generate, but we can test)
        var emptyTable = new TruthTable(new List<string>(), new List<bool>());

        // Act
        var result = emptyTable.ToString();

        // Assert
        Assert.Equal("Empty truth table", result);
    }

    [Fact]
    public void TruthTable_Variables_ShouldBeSorted()
    {
        // Arrange
        var table = TruthTable.Generate("z & a & m");

        // Act & Assert
        Assert.Equal(3, table.Variables.Count);
        Assert.Equal("a", table.Variables[0]);
        Assert.Equal("m", table.Variables[1]);
        Assert.Equal("z", table.Variables[2]);

        // Variables should be in alphabetical order
        var sortedVars = table.Variables.OrderBy(v => v).ToList();
        Assert.Equal(sortedVars, table.Variables);
    }

    [Fact]
    public void TruthTable_Rows_ShouldMatchVariableCount()
    {
        // Arrange
        var table = TruthTable.Generate("a & b & c");

        // Act & Assert
        Assert.Equal(3, table.Variables.Count);
        Assert.Equal(8, table.Rows.Count); // 2^3 = 8
        Assert.Equal(8, table.Results.Count);

        // Each row should have all variables
        foreach (var row in table.Rows)
        {
            Assert.Equal(3, row.Count);
            Assert.Contains("a", row.Keys);
            Assert.Contains("b", row.Keys);
            Assert.Contains("c", row.Keys);
        }
    }

    [Fact]
    public void TruthTable_Rows_ShouldCoverAllCombinations()
    {
        // Arrange
        var table = TruthTable.Generate("a & b");

        // Act
        var combinations = new HashSet<string>();
        foreach (var row in table.Rows)
        {
            var combination = $"{(row["a"] ? "T" : "F")}{(row["b"] ? "T" : "F")}";
            combinations.Add(combination);
        }

        // Assert
        Assert.Equal(4, combinations.Count);
        Assert.Contains("FF", combinations);
        Assert.Contains("FT", combinations);
        Assert.Contains("TF", combinations);
        Assert.Contains("TT", combinations);
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("a & b", 2)]
    [InlineData("a & b & c", 3)]
    [InlineData("x1 & x2 & x3 & x4", 4)]
    public void TruthTable_VariableCount_ShouldMatchExpression(string expression, int expectedCount)
    {
        // Act
        var table = TruthTable.Generate(expression);

        // Assert
        Assert.Equal(expectedCount, table.Variables.Count);
        Assert.Equal((int) Math.Pow(2, expectedCount), table.Results.Count);
        Assert.Equal((int) Math.Pow(2, expectedCount), table.Rows.Count);
    }

    [Fact]
    public void TruthTable_ConsistencyCheck_ResultsShouldMatchRowEvaluation()
    {
        // Arrange
        var expression = "(a & b) | (!a & c)";
        var table = TruthTable.Generate(expression);

        // Act & Assert
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var a = row["a"];
            var b = row["b"];
            var c = row["c"];

            var expectedResult = (a && b) || (!a && c);
            Assert.Equal(expectedResult, table.Results[i]);
        }
    }

    [Fact]
    public void TruthTable_EquivalenceReflexivity_ShouldWork()
    {
        // Arrange
        var table = TruthTable.Generate("a & b | c");

        // Act & Assert
        Assert.True(table.IsEquivalentTo(table)); // Reflexivity: A = A
    }

    [Fact]
    public void TruthTable_EquivalenceSymmetry_ShouldWork()
    {
        // Arrange
        var table1 = TruthTable.Generate("a & b");
        var table2 = TruthTable.Generate("b & a");

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
        Assert.True(table2.IsEquivalentTo(table1)); // Symmetry: if A = B then B = A
    }

    [Fact]
    public void TruthTable_EquivalenceTransitivity_ShouldWork()
    {
        // Arrange
        var table1 = TruthTable.Generate("a & b");
        var table2 = TruthTable.Generate("b & a");
        var table3 = TruthTable.Generate("a & b");

        // Act & Assert
        Assert.True(table1.IsEquivalentTo(table2));
        Assert.True(table2.IsEquivalentTo(table3));
        Assert.True(table1.IsEquivalentTo(table3)); // Transitivity: if A = B and B = C then A = C
    }

    [Fact]
    public void TruthTable_SpecialCases_ShouldHandleCorrectly()
    {
        // Test single variable repeated
        var table1 = TruthTable.Generate("a & a & a");
        var table2 = TruthTable.Generate("a");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test expression with only constants
        var table3 = TruthTable.Generate("1 & 0");
        var table4 = TruthTable.Generate("0");
        Assert.True(table3.IsEquivalentTo(table4));

        // Test mixed constants and variables
        var table5 = TruthTable.Generate("a & 1 & 0");
        var table6 = TruthTable.Generate("0");
        Assert.True(table5.IsEquivalentTo(table6));
    }

    [Fact]
    public void TruthTable_LargeExpression_ShouldMaintainAccuracy()
    {
        // Arrange - test with 5 variables (32 rows)
        var expression = "a & b | c & d | e";
        var table = TruthTable.Generate(expression);

        // Act & Assert
        Assert.Equal(5, table.Variables.Count);
        Assert.Equal(32, table.Results.Count);

        // Spot check a few specific cases
        var firstRow = table.Rows[0]; // All false
        Assert.True(table.Variables.All(v => !firstRow[v]));
        var firstResult = false || false || false; // a&b | c&d | e where all are false
        Assert.Equal(firstResult, table.Results[0]);

        var lastRow = table.Rows[31]; // All true
        Assert.True(table.Variables.All(v => lastRow[v]));
        var lastResult = true || true || true; // a&b | c&d | e where all are true
        Assert.Equal(lastResult, table.Results[31]);
    }
}