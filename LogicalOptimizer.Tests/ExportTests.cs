using System;
using Xunit;

namespace LogicalOptimizer.Tests;

public class ExportTests
{
    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    public void ToDimacs_BasicExpressions_ShouldContainCorrectHeaders(string input)
    {
        // Act
        var result = BooleanExpressionExporter.ToDimacs(input);

        // Assert
        Assert.Contains($"c Boolean expression: {input}", result);
        Assert.Contains("p cnf", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    public void ToBlif_BasicExpressions_ShouldContainCorrectStructure(string input)
    {
        // Act
        var result = BooleanExpressionExporter.ToBlif(input);

        // Assert
        Assert.Contains(".model boolean_expr", result);
        Assert.Contains(".inputs", result);
        Assert.Contains(".outputs out", result);
        Assert.Contains(".end", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    public void ToVerilog_BasicExpressions_ShouldContainModuleStructure(string input)
    {
        // Act
        var result = BooleanExpressionExporter.ToVerilog(input);

        // Assert
        Assert.Contains("module boolean_expr(", result);
        Assert.Contains("input", result);
        Assert.Contains("output out", result);
        Assert.Contains("endmodule", result);
    }

    [Theory]
    [InlineData("a & b", "a ∧ b")]
    [InlineData("a | b", "a ∨ b")]
    [InlineData("!a", "¬a")]
    [InlineData("!(a & b)", "¬(a ∧ b)")]
    public void ToMathematicalNotation_BasicExpressions_ShouldReturnCorrectSymbols(string input, string expected)
    {
        // Act
        var result = BooleanExpressionExporter.ToMathematicalNotation(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("a & b", "\\land")]
    [InlineData("a | b", "\\lor")]
    [InlineData("!a", "\\neg")]
    [InlineData("!(a & b)", "\\neg")]
    public void ToLatex_BasicExpressions_ShouldReturnCorrectLatexCommands(string input, string expectedCommand)
    {
        // Act
        var result = BooleanExpressionExporter.ToLatex(input);

        // Assert
        Assert.Contains(expectedCommand, result);
    }

    [Theory]
    [InlineData("a & b", "a \\land b")]
    [InlineData("a | b", "a \\lor b")]
    [InlineData("!a", "\\neg a")]
    [InlineData("!(a & b)", "\\neg (a \\land b)")]
    public void ToLatex_BasicExpressions_ShouldReturnCorrectFormat(string input, string expected)
    {
        // Act
        var result = BooleanExpressionExporter.ToLatex(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TruthTableToCsv_SimpleExpression_ShouldReturnCorrectCsvFormat()
    {
        // Arrange
        var expression = "a & b";

        // Act
        var result = BooleanExpressionExporter.TruthTableToCsv(expression);

        // Assert
        var lines = result.Trim().Split(new[] {"\r\n", "\n"}, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("a,b,Result", lines[0]);
        Assert.Equal(5, lines.Length); // Header + 4 data rows
        Assert.Contains("0,0,0", result);
        Assert.Contains("1,1,1", result);
    }
}
