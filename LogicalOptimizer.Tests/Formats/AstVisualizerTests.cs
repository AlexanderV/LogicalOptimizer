using System;
using System.Text.RegularExpressions;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the AstVisualizer component - AST tree visualization functionality
/// </summary>
public class AstVisualizerTests
{
    private AstNode ParseExpression(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Fact]
    public void VisualizeTree_SimpleVariable_ShouldReturnCorrectVisualization()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("└─", result);
    }

    [Fact]
    public void VisualizeTree_SimpleAnd_ShouldReturnCorrectVisualization()
    {
        // Arrange
        var ast = ParseExpression("a & b");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("AND (&)", result);
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("Variable: 'b'", result);
        Assert.Contains("└─", result);
        Assert.Contains("├─", result);
    }

    [Fact]
    public void VisualizeTree_SimpleOr_ShouldReturnCorrectVisualization()
    {
        // Arrange
        var ast = ParseExpression("a | b");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("OR (|)", result);
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("Variable: 'b'", result);
    }

    [Fact]
    public void VisualizeTree_NotExpression_ShouldReturnCorrectVisualization()
    {
        // Arrange
        var ast = ParseExpression("!a");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("NOT (!)", result);
        Assert.Contains("Variable: 'a'", result);
    }

    [Fact]
    public void GetCompactVisualization_SimpleExpression_ShouldReturnFormattedString()
    {
        // Arrange
        var ast = ParseExpression("a & b");

        // Act
        var result = AstVisualizer.GetCompactVisualization(ast);

        // Assert
        Assert.StartsWith("AST: ", result);
        Assert.Contains("Tree:", result);
        Assert.Contains("AND (&)", result);
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("Variable: 'b'", result);
    }

    [Fact]
    public void VisualizeTree_WithCustomPrefix_ShouldUsePrefix()
    {
        // Arrange
        var ast = ParseExpression("a");
        var customPrefix = ">>>";

        // Act
        var result = AstVisualizer.VisualizeTree(ast, customPrefix, true);

        // Assert
        Assert.Contains(customPrefix, result);
        Assert.Contains("└─", result);
    }

    [Fact]
    public void VisualizeTree_WithIsLastFalse_ShouldUseBranchSymbol()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var result = AstVisualizer.VisualizeTree(ast, "", false);

        // Assert
        Assert.Contains("├─", result);
    }

    [Fact]
    public void VisualizeTree_DeepNesting_ShouldHandleCorrectly()
    {
        // Arrange
        var ast = ParseExpression("a & (b | (c & d))");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("AND (&)", result);
        Assert.Contains("OR (|)", result);
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("Variable: 'b'", result);
        Assert.Contains("Variable: 'c'", result);
        Assert.Contains("Variable: 'd'", result);
        // Should have proper tree structure with multiple levels
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 6); // At least 6 nodes in the tree
    }

    [Fact]
    public void VisualizeTree_MultipleVariables_ShouldShowAllVariables()
    {
        // Arrange
        var ast = ParseExpression("x1 & x2 & x3");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert: "x1 & x2 & x3" is ONE flat n-ary AND with three direct variable children.
        // A nested-binary regression (AND(AND(x1,x2),x3)) would render TWO "AND (&)" nodes
        // and would indent x1/x2 one level deeper, so both checks below would fail.
        Assert.Single(Regex.Matches(result, @"AND \(&\)"));

        // The three variables are DIRECT children of the single AND (prefix "   ", one
        // indent level below the root), pinning the flat n-ary shape.
        Assert.Contains("   ├─ Variable: 'x1'", result);
        Assert.Contains("   ├─ Variable: 'x2'", result);
        Assert.Contains("   └─ Variable: 'x3'", result);
    }
}
