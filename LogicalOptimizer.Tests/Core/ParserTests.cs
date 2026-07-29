using System;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the Parser component. The parser builds through FormulaFactory, so its
/// output is canonical: associativity chains are flattened into a single n-ary node
/// and operands are sorted by the canonical key (complexity score, then string key).
/// </summary>
public class ParserTests
{
    private static AstNode Parse(string input)
    {
        return new Parser(new Lexer(input).Tokenize()).Parse();
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

        // Act & Assert (FormulaParseException is an ArgumentException)
        Assert.ThrowsAny<ArgumentException>(() => parser.Parse());
    }

    [Fact]
    public void Parser_SimpleVariable_ShouldCreateVariableNode()
    {
        // Act
        var ast = Parse("a");

        // Assert
        Assert.IsType<VariableNode>(ast);
        Assert.Equal("a", ((VariableNode)ast).Name);
    }

    [Fact]
    public void Parser_Negation_ShouldCreateNotNode()
    {
        // Act
        var ast = Parse("!a");

        // Assert
        Assert.IsType<NotNode>(ast);
        var notNode = (NotNode)ast;
        Assert.IsType<VariableNode>(notNode.Operand);
        Assert.Equal("a", ((VariableNode)notNode.Operand).Name);
    }

    [Fact]
    public void Parser_AndOperation_ShouldCreateAndNode()
    {
        // Act
        var ast = Parse("a & b");

        // Assert
        var andNode = Assert.IsType<AndNode>(ast);
        Assert.Equal(2, andNode.Operands.Count);
        Assert.Equal("a", ((VariableNode)andNode.Operands[0]).Name);
        Assert.Equal("b", ((VariableNode)andNode.Operands[1]).Name);
    }

    [Fact]
    public void Parser_OrOperation_ShouldCreateOrNode()
    {
        // Act
        var ast = Parse("a | b");

        // Assert
        var orNode = Assert.IsType<OrNode>(ast);
        Assert.Equal(2, orNode.Operands.Count);
        Assert.Equal("a", ((VariableNode)orNode.Operands[0]).Name);
        Assert.Equal("b", ((VariableNode)orNode.Operands[1]).Name);
    }

    [Fact]
    public void Parser_AssociativityChain_ShouldFlattenIntoSingleNaryNode()
    {
        // Act - a & b & c is ONE n-ary node, not a nested binary chain
        var ast = Parse("a & b & c");

        // Assert
        var andNode = Assert.IsType<AndNode>(ast);
        Assert.Equal(3, andNode.Operands.Count);
        Assert.All(andNode.Operands, o => Assert.IsType<VariableNode>(o));
        Assert.Equal(new[] { "a", "b", "c" },
            andNode.Operands.Select(o => ((VariableNode)o).Name));
    }

    [Fact]
    public void Parser_CommutedOperands_ShouldSortCanonically()
    {
        // Act - parser output is canonical, so "c & a" and "a & c" are identical
        var commuted = Parse("c & a");

        // Assert
        var andNode = Assert.IsType<AndNode>(commuted);
        Assert.Equal(new[] { "a", "c" },
            andNode.Operands.Select(o => ((VariableNode)o).Name));
        Assert.Equal(Parse("a & c"), commuted);
    }

    [Fact]
    public void Parser_ComplexExpression_ShouldRespectPrecedence()
    {
        // Act - AND has higher precedence than OR
        var ast = Parse("a | b & c");

        // Assert - Or(a, And(b, c)); the simple literal sorts before the AND term
        var orNode = Assert.IsType<OrNode>(ast);
        Assert.Equal(2, orNode.Operands.Count);
        Assert.Equal("a", ((VariableNode)orNode.Operands[0]).Name);

        var innerAnd = Assert.IsType<AndNode>(orNode.Operands[1]);
        Assert.Equal(new[] { "b", "c" },
            innerAnd.Operands.Select(o => ((VariableNode)o).Name));
    }

    [Theory]
    [InlineData("a & b | c")] // AND binds tighter than OR
    [InlineData("(a & b) | c")] // Redundant parentheses produce the same structure
    public void Parser_AndOrPrecedence_ShouldPlaceAndBelowOr(string input)
    {
        // Act
        var ast = Parse(input);

        // Assert - Or(c, And(a, b)): canonical order puts the literal first
        var orNode = Assert.IsType<OrNode>(ast);
        Assert.Equal(2, orNode.Operands.Count);
        Assert.Equal("c", ((VariableNode)orNode.Operands[0]).Name);

        var innerAnd = Assert.IsType<AndNode>(orNode.Operands[1]);
        Assert.Equal(new[] { "a", "b" },
            innerAnd.Operands.Select(o => ((VariableNode)o).Name));
    }

    [Fact]
    public void Parser_ParenthesesOnRight_ShouldGroupOrUnderAnd()
    {
        // Act
        var ast = Parse("a & (b | c)");

        // Assert - And(a, Or(b, c))
        var andNode = Assert.IsType<AndNode>(ast);
        Assert.Equal(2, andNode.Operands.Count);
        Assert.Equal("a", ((VariableNode)andNode.Operands[0]).Name);

        var innerOr = Assert.IsType<OrNode>(andNode.Operands[1]);
        Assert.Equal(new[] { "b", "c" },
            innerOr.Operands.Select(o => ((VariableNode)o).Name));
    }

    [Fact]
    public void Parser_ParenthesesExpression_ShouldRespectGrouping()
    {
        // Act
        var ast = Parse("(a | b) & c");

        // Assert - And(c, Or(a, b)): the literal sorts before the OR group
        var andNode = Assert.IsType<AndNode>(ast);
        Assert.Equal(2, andNode.Operands.Count);
        Assert.Equal("c", ((VariableNode)andNode.Operands[0]).Name);

        var innerOr = Assert.IsType<OrNode>(andNode.Operands[1]);
        Assert.Equal(new[] { "a", "b" },
            innerOr.Operands.Select(o => ((VariableNode)o).Name));
    }
}
