using System;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the Parser component - building AST from tokens
/// </summary>
public class ParserTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("!a")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("a & b | c")]
    [InlineData("(a & b) | c")]
    [InlineData("a & (b | c)")]
    public void Parser_ValidExpressions_ShouldParseWithoutErrors(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act & Assert
        var ast = parser.Parse();
        Assert.NotNull(ast);
    }

    [Theory]
    [InlineData("a &")]
    [InlineData("| b")]
    [InlineData("a & & b")]
    [InlineData("((a)")]
    [InlineData("a & b)")]
    public void Parser_InvalidExpressions_ShouldThrowException(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => parser.Parse());
    }

    [Fact]
    public void Parser_SimpleVariable_ShouldCreateVariableNode()
    {
        // Arrange
        var lexer = new Lexer("a");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act
        var ast = parser.Parse();

        // Assert
        Assert.IsType<VariableNode>(ast);
        Assert.Equal("a", ((VariableNode)ast).Name);
    }

    [Fact]
    public void Parser_Negation_ShouldCreateNotNode()
    {
        // Arrange
        var lexer = new Lexer("!a");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act
        var ast = parser.Parse();

        // Assert
        Assert.IsType<NotNode>(ast);
        var notNode = (NotNode)ast;
        Assert.IsType<VariableNode>(notNode.Operand);
        Assert.Equal("a", ((VariableNode)notNode.Operand).Name);
    }

    [Fact]
    public void Parser_AndOperation_ShouldCreateAndNode()
    {
        // Arrange
        var lexer = new Lexer("a & b");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act
        var ast = parser.Parse();

        // Assert
        Assert.IsType<AndNode>(ast);
        var andNode = (AndNode)ast;
        Assert.IsType<VariableNode>(andNode.Left);
        Assert.IsType<VariableNode>(andNode.Right);
        Assert.Equal("a", ((VariableNode)andNode.Left).Name);
        Assert.Equal("b", ((VariableNode)andNode.Right).Name);
    }

    [Fact]
    public void Parser_OrOperation_ShouldCreateOrNode()
    {
        // Arrange
        var lexer = new Lexer("a | b");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act
        var ast = parser.Parse();

        // Assert
        Assert.IsType<OrNode>(ast);
        var orNode = (OrNode)ast;
        Assert.IsType<VariableNode>(orNode.Left);
        Assert.IsType<VariableNode>(orNode.Right);
        Assert.Equal("a", ((VariableNode)orNode.Left).Name);
        Assert.Equal("b", ((VariableNode)orNode.Right).Name);
    }

    [Fact]
    public void Parser_ComplexExpression_ShouldRespectPrecedence()
    {
        // Arrange - AND has higher precedence than OR
        var lexer = new Lexer("a | b & c");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act
        var ast = parser.Parse();

        // Assert
        Assert.IsType<OrNode>(ast);
        var orNode = (OrNode)ast;
        Assert.IsType<VariableNode>(orNode.Left);
        Assert.IsType<AndNode>(orNode.Right);
        Assert.Equal("a", ((VariableNode)orNode.Left).Name);

        var rightAnd = (AndNode)orNode.Right;
        Assert.Equal("b", ((VariableNode)rightAnd.Left).Name);
        Assert.Equal("c", ((VariableNode)rightAnd.Right).Name);
    }

    [Fact]
    public void Parser_ParenthesesExpression_ShouldRespectGrouping()
    {
        // Arrange
        var lexer = new Lexer("(a | b) & c");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act
        var ast = parser.Parse();

        // Assert
        Assert.IsType<AndNode>(ast);
        var andNode = (AndNode)ast;
        Assert.IsType<OrNode>(andNode.Left);
        Assert.IsType<VariableNode>(andNode.Right);
        Assert.Equal("c", ((VariableNode)andNode.Right).Name);

        var leftOr = (OrNode)andNode.Left;
        Assert.Equal("a", ((VariableNode)leftOr.Left).Name);
        Assert.Equal("b", ((VariableNode)leftOr.Right).Name);
    }
}
