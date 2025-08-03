using System;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the Lexer component - tokenization and basic lexical analysis
/// </summary>
public class LexerTests
{
    [Theory]
    [InlineData("a", new[] {"a"})]
    [InlineData("a & b", new[] {"a", "&", "b"})]
    [InlineData("a | b", new[] {"a", "|", "b"})]
    [InlineData("!a", new[] {"!", "a"})]
    [InlineData("(a)", new[] {"(", "a", ")"})]
    [InlineData("a123", new[] {"a123"})]
    [InlineData("var_name", new[] {"var_name"})]
    public void Lexer_BasicTokenization_ShouldReturnCorrectTokens(string input, string[] expectedTokens)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var tokens = lexer.Tokenize();
        var actualTokens = tokens.Where(t => t.Type != TokenType.End).Select(t => t.Value).ToArray();

        // Assert
        Assert.Equal(expectedTokens, actualTokens);
    }

    [Theory]
    [InlineData("a & b | !c", new[] {"a", "&", "b", "|", "!", "c"})]
    [InlineData("!(a & b)", new[] {"!", "(", "a", "&", "b", ")"})]
    [InlineData("a&b|c", new[] {"a", "&", "b", "|", "c"})]
    public void Lexer_ComplexExpressions_ShouldReturnCorrectTokens(string input, string[] expectedTokens)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var tokens = lexer.Tokenize();
        var actualTokens = tokens.Where(t => t.Type != TokenType.End).Select(t => t.Value).ToArray();

        // Assert
        Assert.Equal(expectedTokens, actualTokens);
    }

    [Theory]
    [InlineData("a @ b")]
    [InlineData("123")]
    public void Lexer_InvalidInput_ShouldThrowException(string input)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => lexer.Tokenize());
    }

    [Fact]
    public void Lexer_EmptyInput_ShouldReturnEndToken()
    {
        // Arrange
        var lexer = new Lexer("");

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenType.End, tokens.First().Type);
    }
}
