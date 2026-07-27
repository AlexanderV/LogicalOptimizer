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

    [Fact]
    public void ToDimacs_OrExpression_ShouldEmitExactHeaderAndClause()
    {
        // Act: "a | b" is a single 2-literal clause over 2 variables (a=1, b=2)
        var result = BooleanExpressionExporter.ToDimacs("a | b");

        // Assert: pin the exact problem line and clause body, not just the "p cnf" prefix.
        Assert.Contains("c Boolean expression: a | b", result);
        Assert.Contains("p cnf 2 1", result);
        Assert.Contains("1 2 0", result);
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

    [Fact]
    public void ToBlif_NaryAnd_ShouldEmitSingleThreeInputGate()
    {
        // "a & b & c" parses to a flat 3-operand AndNode → one multi-input BLIF gate.
        var result = BooleanExpressionExporter.ToBlif("a & b & c");

        // Assert: all three inputs on ONE .names line + a 3-wide cover row (nested binary
        // would instead emit two 2-input AND gates).
        Assert.Contains(".inputs a b c", result);
        Assert.Contains(".names a b c a0", result);
        Assert.Contains("111 1", result);
    }

    [Fact]
    public void ToBlif_NaryOr_ShouldEmitThreeInputGateWithPerInputCoverRows()
    {
        // "a | b | c" parses to a flat 3-operand OrNode → one 3-input OR gate with one
        // cover row per input.
        var result = BooleanExpressionExporter.ToBlif("a | b | c");

        Assert.Contains(".names a b c o0", result);
        Assert.Contains("1-- 1", result);
        Assert.Contains("-1- 1", result);
        Assert.Contains("--1 1", result);
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

    [Fact]
    public void ToVerilog_NaryAnd_ShouldEmitAllThreeInputsInOneAssign()
    {
        // "a & b & c" → flat 3-operand AndNode → a single multi-input assign.
        var result = BooleanExpressionExporter.ToVerilog("a & b & c");

        // Assert: three inputs joined in ONE assign (nested binary would need two assigns).
        Assert.Contains("assign w0 = a & b & c;", result);
    }

    [Fact]
    public void ToVerilog_NaryOr_ShouldEmitAllThreeInputsInOneAssign()
    {
        // "a | b | c" → flat 3-operand OrNode → a single multi-input assign.
        var result = BooleanExpressionExporter.ToVerilog("a | b | c");

        Assert.Contains("assign w0 = a | b | c;", result);
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
        var lines = result.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("a,b,Result", lines[0]);
        Assert.Equal(5, lines.Length); // Header + 4 data rows
        Assert.Contains("0,0,0", result);
        Assert.Contains("1,1,1", result);
    }
}
