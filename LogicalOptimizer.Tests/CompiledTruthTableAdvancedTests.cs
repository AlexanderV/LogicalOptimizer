using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Additional comprehensive tests for CompiledTruthTable to improve coverage
/// </summary>
public class CompiledTruthTableAdvancedTests
{
    private static AstNode ParseExpression(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Fact]
    public void CompiledTruthTable_DefaultConstructor_ShouldInitializeCorrectly()
    {
        // Act
        var table = new CompiledTruthTable();

        // Assert
        Assert.NotNull(table.Rows);
        Assert.Empty(table.Rows);
        Assert.NotNull(table.Variables);
        Assert.Empty(table.Variables);
        Assert.Equal("", table.Expression);
    }

    [Fact]
    public void Generate_WithExpressionText_ShouldSetExpressionProperty()
    {
        // Arrange
        var ast = ParseExpression("a & b");
        var expressionText = "a & b";

        // Act
        var table = CompiledTruthTable.Generate(ast, expressionText);

        // Assert
        Assert.Equal(expressionText, table.Expression);
    }

    [Fact]
    public void Generate_WithoutExpressionText_ShouldSetEmptyExpression()
    {
        // Arrange
        var ast = ParseExpression("a & b");

        // Act
        var table = CompiledTruthTable.Generate(ast);

        // Assert
        Assert.Equal("", table.Expression);
    }

    [Fact]
    public void Generate_SingleVariable_ShouldGenerateCorrectTable()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a");

        // Assert
        Assert.Single(table.Variables);
        Assert.Contains("a", table.Variables);
        Assert.Equal(2, table.Rows.Count);
        
        // Check specific rows
        var falseRow = table.Rows.First(r => !r.Variables["a"]);
        var trueRow = table.Rows.First(r => r.Variables["a"]);
        
        Assert.False(falseRow.Result);
        Assert.True(trueRow.Result);
    }

    [Fact]
    public void Generate_TwoVariables_ShouldGenerateFourRows()
    {
        // Arrange
        var ast = ParseExpression("a | b");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a | b");

        // Assert
        Assert.Equal(2, table.Variables.Count);
        Assert.Contains("a", table.Variables);
        Assert.Contains("b", table.Variables);
        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public void Generate_ThreeVariables_ShouldGenerateEightRows()
    {
        // Arrange
        var ast = ParseExpression("a & b & c");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a & b & c");

        // Assert
        Assert.Equal(3, table.Variables.Count);
        Assert.Equal(8, table.Rows.Count);
    }

    [Fact]
    public void Generate_ComplexExpression_ShouldGenerateCorrectResults()
    {
        // Arrange
        var ast = ParseExpression("(a & b) | (!a & c)");

        // Act
        var table = CompiledTruthTable.Generate(ast, "(a & b) | (!a & c)");

        // Assert
        Assert.Equal(3, table.Variables.Count);
        Assert.Equal(8, table.Rows.Count);
        
        // Verify some specific combinations
        var allFalse = table.Rows.FirstOrDefault(r => 
            !r.Variables["a"] && !r.Variables["b"] && !r.Variables["c"]);
        Assert.NotNull(allFalse);
        Assert.False(allFalse.Result); // (!False & False) | (True & False) = False
        
        var aTrueOthersFalse = table.Rows.FirstOrDefault(r => 
            r.Variables["a"] && !r.Variables["b"] && !r.Variables["c"]);
        Assert.NotNull(aTrueOthersFalse);
        Assert.False(aTrueOthersFalse.Result); // (True & False) | (False & False) = False
    }

    [Fact]
    public void Generate_NotExpression_ShouldGenerateCorrectResults()
    {
        // Arrange
        var ast = ParseExpression("!a");

        // Act
        var table = CompiledTruthTable.Generate(ast, "!a");

        // Assert
        Assert.Single(table.Variables);
        Assert.Equal(2, table.Rows.Count);
        
        var aFalse = table.Rows.First(r => !r.Variables["a"]);
        var aTrue = table.Rows.First(r => r.Variables["a"]);
        
        Assert.True(aFalse.Result);  // !False = True
        Assert.False(aTrue.Result);  // !True = False
    }

    [Fact]
    public void Generate_NestedExpression_ShouldGenerateCorrectResults()
    {
        // Arrange
        var ast = ParseExpression("!(a & b)");

        // Act
        var table = CompiledTruthTable.Generate(ast, "!(a & b)");

        // Assert
        Assert.Equal(2, table.Variables.Count);
        Assert.Equal(4, table.Rows.Count);
        
        // !(a & b) should be true except when both a and b are true
        var bothTrue = table.Rows.First(r => r.Variables["a"] && r.Variables["b"]);
        Assert.False(bothTrue.Result);
        
        var otherRows = table.Rows.Where(r => !(r.Variables["a"] && r.Variables["b"]));
        Assert.All(otherRows, row => Assert.True(row.Result));
    }

    [Fact]
    public void Generate_VariableOrder_ShouldBeConsistent()
    {
        // Arrange
        var ast1 = ParseExpression("a & b");
        var ast2 = ParseExpression("b & a");

        // Act
        var table1 = CompiledTruthTable.Generate(ast1);
        var table2 = CompiledTruthTable.Generate(ast2);

        // Assert
        Assert.Equal(table1.Variables.Count, table2.Variables.Count);
        Assert.Equal(table1.Rows.Count, table2.Rows.Count);
        
        // Both should contain the same variables (order may differ)
        Assert.Contains("a", table1.Variables);
        Assert.Contains("b", table1.Variables);
        Assert.Contains("a", table2.Variables);
        Assert.Contains("b", table2.Variables);
    }

    [Fact]
    public void Generate_DuplicateVariables_ShouldHandleCorrectly()
    {
        // Arrange
        var ast = ParseExpression("a & a");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a & a");

        // Assert
        Assert.Single(table.Variables);
        Assert.Contains("a", table.Variables);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void Generate_XorPattern_ShouldGenerateCorrectResults()
    {
        // Arrange
        var ast = ParseExpression("(a & !b) | (!a & b)");

        // Act
        var table = CompiledTruthTable.Generate(ast, "(a & !b) | (!a & b)");

        // Assert
        Assert.Equal(2, table.Variables.Count);
        Assert.Equal(4, table.Rows.Count);
        
        // XOR should be true only when exactly one variable is true
        var aTrueBFalse = table.Rows.First(r => r.Variables["a"] && !r.Variables["b"]);
        var aFalseBTrue = table.Rows.First(r => !r.Variables["a"] && r.Variables["b"]);
        var bothTrue = table.Rows.First(r => r.Variables["a"] && r.Variables["b"]);
        var bothFalse = table.Rows.First(r => !r.Variables["a"] && !r.Variables["b"]);
        
        Assert.True(aTrueBFalse.Result);
        Assert.True(aFalseBTrue.Result);
        Assert.False(bothTrue.Result);
        Assert.False(bothFalse.Result);
    }

    [Fact]
    public void Generate_Tautology_ShouldAllBeTrue()
    {
        // Arrange
        var ast = ParseExpression("a | !a");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a | !a");

        // Assert
        Assert.Single(table.Variables);
        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.True(row.Result));
    }

    [Fact]
    public void Generate_Contradiction_ShouldAllBeFalse()
    {
        // Arrange
        var ast = ParseExpression("a & !a");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a & !a");

        // Assert
        Assert.Single(table.Variables);
        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.False(row.Result));
    }

    [Fact]
    public void Generate_FourVariables_ShouldGenerateSixteenRows()
    {
        // Arrange
        var ast = ParseExpression("a & b & c & d");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a & b & c & d");

        // Assert
        Assert.Equal(4, table.Variables.Count);
        Assert.Equal(16, table.Rows.Count);
        
        // Only one row should be true (all variables true)
        var trueRows = table.Rows.Where(r => r.Result).ToList();
        Assert.Single(trueRows);
        
        var trueRow = trueRows.First();
        Assert.All(trueRow.Variables.Values, Assert.True);
    }

    [Fact]
    public void Generate_LongVariableNames_ShouldHandleCorrectly()
    {
        // Arrange
        var ast = ParseExpression("variable1 & variable2");

        // Act
        var table = CompiledTruthTable.Generate(ast, "variable1 & variable2");

        // Assert
        Assert.Equal(2, table.Variables.Count);
        Assert.Contains("variable1", table.Variables);
        Assert.Contains("variable2", table.Variables);
        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public void Generate_EmptyExpression_ShouldNotThrow()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act & Assert
        var exception = Record.Exception(() => CompiledTruthTable.Generate(ast, ""));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    [InlineData("a & b | c")]
    [InlineData("(a | b) & c")]
    [InlineData("!(a & b)")]
    [InlineData("a & !b")]
    public void Generate_VariousExpressions_ShouldNotThrow(string expression)
    {
        // Arrange
        var ast = ParseExpression(expression);

        // Act & Assert
        var exception = Record.Exception(() => CompiledTruthTable.Generate(ast, expression));
        Assert.Null(exception);
    }

    [Fact]
    public void Generate_ResultConsistency_ShouldMatchManualCalculation()
    {
        // Arrange
        var ast = ParseExpression("a & b | c");

        // Act
        var table = CompiledTruthTable.Generate(ast, "a & b | c");

        // Assert - manually verify some key combinations
        var testCase1 = table.Rows.First(r => 
            r.Variables["a"] && r.Variables["b"] && !r.Variables["c"]);
        Assert.True(testCase1.Result); // True & True | False = True
        
        var testCase2 = table.Rows.First(r => 
            !r.Variables["a"] && !r.Variables["b"] && !r.Variables["c"]);
        Assert.False(testCase2.Result); // False & False | False = False
        
        var testCase3 = table.Rows.First(r => 
            !r.Variables["a"] && !r.Variables["b"] && r.Variables["c"]);
        Assert.True(testCase3.Result); // False & False | True = True
    }

    [Fact]
    public void AreEquivalent_DifferentRowCount_ShouldReturnFalse()
    {
        // Arrange - Create tables with different row counts by using different variable counts
        var singleVar = new VariableNode("a");
        var doubleVar = new AndNode(new VariableNode("a"), new VariableNode("b"));
        var table1 = CompiledTruthTable.Generate(singleVar);
        var table2 = CompiledTruthTable.Generate(doubleVar);

        // Act
        var result = CompiledTruthTable.AreEquivalent(table1, table2);

        // Assert
        Assert.False(result);
        Assert.Equal(2, table1.Rows.Count);
        Assert.Equal(4, table2.Rows.Count);
    }

    [Fact]
    public void CompareExpressions_WithoutExpressionText_ShouldStillWork()
    {
        // Arrange
        var original = new VariableNode("a");
        var optimized = new VariableNode("a");

        // Act
        var result = CompiledTruthTable.CompareExpressions(original, optimized);

        // Assert
        Assert.Contains("Truth Table Comparison:", result);
        Assert.Contains("Original:", result);
        Assert.Contains("Optimized:", result);
        Assert.Contains("Equivalent: True", result);
    }

    [Fact]
    public void CompareExpressions_ComplexExpression_ShouldFormatTableCorrectly()
    {
        // Arrange
        var original = new AndNode(new VariableNode("variable_with_very_long_name"), new VariableNode("b"));
        var optimized = new AndNode(new VariableNode("b"), new VariableNode("variable_with_very_long_name"));

        // Act
        var result = CompiledTruthTable.CompareExpressions(original, optimized, "long_var & b", "b & long_var");

        // Assert
        Assert.Contains("Original: long_var & b", result);
        Assert.Contains("Optimized: b & long_var", result);
        Assert.Contains("variable_with_very_long_name", result);
        Assert.Contains("Original", result);
        Assert.Contains("Optimized", result);
        Assert.Contains("Match", result);
        Assert.Contains("✓", result); // Should show matches
        Assert.Contains("Equivalent: True", result);
        
        // Check table formatting
        Assert.Contains("|", result);
        Assert.Contains("-", result);
    }

    [Fact]
    public void ToCsv_ThreeVariableExpression_ShouldGenerateCorrectFormat()
    {
        // Arrange
        var expr = new OrNode(
            new AndNode(new VariableNode("x"), new VariableNode("y")), 
            new VariableNode("z"));
        var table = CompiledTruthTable.Generate(expr);

        // Act
        var csv = table.ToCsv();

        // Assert
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(9, lines.Length); // Header + 8 data rows

        // Check header
        var header = lines[0];
        Assert.Contains("x", header);
        Assert.Contains("y", header);
        Assert.Contains("z", header);
        Assert.Contains("Result", header);

        // Check that each line has correct number of commas
        foreach (var line in lines)
        {
            var commaCount = line.Count(c => c == ',');
            Assert.Equal(3, commaCount); // 3 variables + result = 3 commas
        }
    }

    [Fact]
    public void ToString_EmptyTable_ShouldHandleGracefully()
    {
        // Arrange
        var table = new CompiledTruthTable
        {
            Variables = new List<string> { "a", "b" },
            Expression = "test expression",
            Rows = new List<CompiledTruthTable.TruthTableRow>()
        };

        // Act
        var output = table.ToString();

        // Assert
        Assert.Contains("Expression: test expression", output);
        Assert.Contains("a", output);
        Assert.Contains("b", output);
        Assert.Contains("Result", output);
        Assert.Contains("|", output);
        Assert.Contains("-", output);
    }

    [Fact]
    public void TruthTableRow_ComplexVariables_ShouldFormatCorrectly()
    {
        // Arrange
        var row = new CompiledTruthTable.TruthTableRow
        {
            Variables = new Dictionary<string, bool> 
            { 
                { "variable_a", true }, 
                { "variable_b", false },
                { "variable_c", true }
            },
            Result = false
        };

        // Act
        var output = row.ToString();

        // Assert
        Assert.Contains("variable_a=True", output);
        Assert.Contains("variable_b=False", output);
        Assert.Contains("variable_c=True", output);
        Assert.Contains("=> False", output);
        Assert.StartsWith("[", output);
        Assert.Contains("] => False", output);
    }

    [Fact]
    public void Generate_SingleVariableWithLongName_ShouldHandleCorrectly()
    {
        // Arrange
        var variable = new VariableNode("very_long_variable_name_for_testing");

        // Act
        var table = CompiledTruthTable.Generate(variable, "very_long_variable_name_for_testing");

        // Assert
        Assert.Single(table.Variables);
        Assert.Equal("very_long_variable_name_for_testing", table.Variables[0]);
        Assert.Equal("very_long_variable_name_for_testing", table.Expression);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void ToString_LongVariableNames_ShouldAdjustColumnWidths()
    {
        // Arrange
        var expr = new AndNode(
            new VariableNode("very_long_variable_name"), 
            new VariableNode("x"));
        var table = CompiledTruthTable.Generate(expr, "long & short");

        // Act
        var output = table.ToString();

        // Assert
        Assert.Contains("very_long_variable_name", output);
        Assert.Contains("x", output);
        
        // Check that the formatting looks reasonable
        var lines = output.Split('\n');
        var headerLine = lines.First(l => l.Contains("very_long_variable_name"));
        var separatorLine = lines.First(l => l.Contains("---"));
        
        // Both should have proper table structure
        Assert.True(headerLine.Count(c => c == '|') >= 3); // At least variable1 | variable2 | Result |
        Assert.True(separatorLine.Count(c => c == '|') >= 3);
    }
}
