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
        Assert.True(TruthTable.Generate("a | b | (!a & !b)").IsTautology());

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
        Assert.True(TruthTable.Generate("a & b & !a").IsContradiction());

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
        Assert.True(TruthTable.Generate("a | b").IsSatisfiable());
        Assert.True(TruthTable.Generate("!a").IsSatisfiable());
        Assert.True(TruthTable.Generate("(a & b) | c").IsSatisfiable());
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
    public void TruthTable_AreEquivalent_WithInvalidExpression_ShouldThrow()
    {
        // A parse error must surface as an exception, not read as "not equivalent"
        Assert.ThrowsAny<ArgumentException>(() => TruthTable.AreEquivalent("a &", "a"));
        Assert.ThrowsAny<ArgumentException>(() => TruthTable.AreEquivalent("a", "b &"));
        Assert.ThrowsAny<ArgumentException>(() => TruthTable.AreEquivalent("invalid @#", "also invalid @#"));
    }

    [Fact]
    public void TruthTable_Constructor_EnforcesInvariants()
    {
        // A constant table (0 variables) must have exactly one result
        Assert.Throws<ArgumentException>(() => new TruthTable(new List<string>(), new List<bool>()));
        // Result count must be exactly 2^n
        Assert.Throws<ArgumentException>(() => new TruthTable(new List<string> { "a" }, new List<bool> { true }));
        // Variable names must be unique
        Assert.Throws<ArgumentException>(() =>
            new TruthTable(new List<string> { "a", "a" }, new List<bool> { false, false, false, true }));
    }

    [Fact]
    public void TruthTable_ConstantTable_ShouldFormatAsConstant()
    {
        var constantFalse = new TruthTable(new List<string>(), new List<bool> { false });
        var constantTrue = new TruthTable(new List<string>(), new List<bool> { true });

        Assert.Equal("Constant: False", constantFalse.ToString());
        Assert.Equal("Constant: True", constantTrue.ToString());
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

        // Evaluate every row against a C# oracle
        Func<bool, bool, bool, bool, bool, bool> oracle = (a, b, c, d, e) => (a && b) || (c && d) || e;
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var expected = oracle(row["a"], row["b"], row["c"], row["d"], row["e"]);
            Assert.Equal(expected, table.Results[i]);
        }
    }
}

/// <summary>
///     Tests for TruthTable comparison functionality and tabular output
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
        Assert.Contains("| 0 | 0 | 0      |", tableString);
        Assert.Contains("| 1 | 1 | 1      |", tableString);
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
    public void TruthTable_CompareExpressions_DifferentExpressions_ReportsMismatch()
    {
        // Arrange
        var expr1 = "a & b";
        var expr2 = "a | b"; // Different logic

        // Act
        var areEquivalent = TruthTable.AreEquivalent(expr1, expr2);
        var comparison = TruthTable.CompareExpressions(expr1, expr2);

        // Assert
        Assert.False(areEquivalent);
        Assert.Contains("Equivalent: False", comparison);

        // Report format
        Assert.Contains("=== Truth Table Comparison ===", comparison);
        Assert.Contains($"Expression 1: {expr1}", comparison);
        Assert.Contains($"Expression 2: {expr2}", comparison);
        Assert.Contains("| a | b | Expr1 | Expr2 | Match |", comparison);
        Assert.Contains("| 0 | 0 | 0     | 0     | ✓     |", comparison); // Both false when a=0, b=0
        Assert.Contains("| 1 | 1 | 1     | 1     | ✓     |", comparison); // Both true when a=1, b=1
        Assert.Contains("| 0 | 1 | 0     | 1     | ✗     |", comparison); // Different when a=0, b=1
        Assert.Contains("| 1 | 0 | 0     | 1     | ✗     |", comparison); // Different when a=1, b=0
    }
}
