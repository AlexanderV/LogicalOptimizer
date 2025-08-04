using System;
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
    public void VisualizeTree_ComplexExpression_ShouldReturnCorrectVisualization()
    {
        // Arrange
        var ast = ParseExpression("(a & b) | c");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("OR (|)", result);
        Assert.Contains("AND (&)", result);
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("Variable: 'b'", result);
        Assert.Contains("Variable: 'c'", result);
    }

    [Fact]
    public void VisualizeTree_NestedExpression_ShouldReturnCorrectVisualization()
    {
        // Arrange
        var ast = ParseExpression("!(a & b)");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.Contains("NOT (!)", result);
        Assert.Contains("AND (&)", result);
        Assert.Contains("Variable: 'a'", result);
        Assert.Contains("Variable: 'b'", result);
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

        // Assert
        Assert.Contains("Variable: 'x1'", result);
        Assert.Contains("Variable: 'x2'", result);
        Assert.Contains("Variable: 'x3'", result);
        Assert.Contains("AND (&)", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    [InlineData("a & (b | c)")]
    [InlineData("(a | b) & (c | d)")]
    public void VisualizeTree_VariousExpressions_ShouldNotThrow(string expression)
    {
        // Arrange
        var ast = ParseExpression(expression);

        // Act & Assert
        var exception = Record.Exception(() => AstVisualizer.VisualizeTree(ast));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a & b")]
    [InlineData("a | b | c")]
    [InlineData("!(a & b)")]
    public void GetCompactVisualization_VariousExpressions_ShouldNotThrow(string expression)
    {
        // Arrange
        var ast = ParseExpression(expression);

        // Act & Assert
        var exception = Record.Exception(() => AstVisualizer.GetCompactVisualization(ast));
        Assert.Null(exception);
    }

    [Fact]
    public void VisualizeTree_ResultShouldBeNonEmpty()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var result = AstVisualizer.VisualizeTree(ast);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetCompactVisualization_ResultShouldBeNonEmpty()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var result = AstVisualizer.GetCompactVisualization(ast);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }
}
