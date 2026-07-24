using System;
using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the AdvancedPatternDetector component - XOR, IMP and EQV pattern detection
/// </summary>
public class AdvancedPatternDetectorTests
{
    private readonly AdvancedPatternDetector _detector = new();

    private static AstNode ParseExpression(string input)
    {
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #region XOR pattern detection

    [Theory]
    [InlineData("(a & !b) | (!a & b)", "XOR")]
    [InlineData("(a & !b) | (!a & b)", "a XOR b")]
    [InlineData("a & !b | !a & b", "XOR")]
    [InlineData("(x & !y) | (!x & y)", "XOR")]
    [InlineData("(var1 & !var2) | (!var1 & var2)", "XOR")]
    [InlineData("(!p & q) | (p & !q)", "XOR")]
    public void DetectXorPattern_StandardXorPattern_ShouldDetectXor(string input, string expectedPattern)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains(expectedPattern, result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    [InlineData("a")]
    [InlineData("a & !a")] // Contradiction
    [InlineData("a | !a")] // Tautology (an IMP pattern, but not XOR)
    [InlineData("a & b | a & c")] // Factorization candidate, not XOR
    public void DetectXorPattern_NonXorPattern_ShouldReturnEmpty(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b) | c")]
    [InlineData("d | (x & !y) | (!x & y)")]
    [InlineData("(a & !b) | (!a & b) | (c & d)")]
    public void DetectXorPattern_XorWithAdditionalTerms_ShouldDetectXorPart(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains("XOR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b) | (c & !d) | (!c & d)")]
    [InlineData("(x1 & !x2) | (!x1 & x2) | (x3 & !x4) | (!x3 & x4)")]
    [InlineData("(v1 & !v2) | (!v1 & v2) | (v3 & !v4) | (!v3 & v4) | (v5 & !v6) | (!v5 & v6)")]
    public void DetectXorPattern_MultipleXorPatterns_ShouldDetectAtLeastOne(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains("XOR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectXorPattern_NullInput_ShouldReturnEmpty()
    {
        // Act & Assert
        var result = _detector.DetectXorPattern(null!);
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region IMP pattern detection

    [Theory]
    [InlineData("!a | b")]
    [InlineData("b | !a")]
    [InlineData("!x | y")]
    [InlineData("y | !x")]
    [InlineData("!var1 | var2")]
    [InlineData("var2 | !var1")]
    [InlineData("!p | q")]
    public void DetectImplicationPattern_StandardImplication_ShouldDetectImplication(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.Contains("→", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("a")]
    [InlineData("a & b & c")] // Only AND
    [InlineData("a | b | c")] // Only OR, no negated terms
    [InlineData("!a & !b & !c")] // Only negations under AND
    public void DetectImplicationPattern_NonImplicationPattern_ShouldReturnEmpty(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("!a | b | c")] // Implication with additional terms
    [InlineData("(!a | b) | (c & d)")] // Complex OR with implication
    [InlineData("a | (!b | c) | d")] // Nested implication patterns
    public void DetectImplicationPattern_ComplexImplicationPatterns_ShouldDetectCorrectly(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.Contains("→", result);
    }

    [Theory]
    [InlineData("(!a | b) | (!c | d)")] // Multiple implication patterns
    [InlineData("!x | y | !z | w")] // Chain of implications
    public void DetectImplicationPattern_MultipleImplicationPatterns_ShouldHandleMultiplePatterns(string input)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.Contains("→", result);
    }

    [Fact]
    public void DetectImplicationPattern_NullInput_ShouldReturnEmpty()
    {
        // Act & Assert
        var result = _detector.DetectImplicationPattern(null!);
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region ConvertToAdvancedForms

    [Theory]
    [InlineData("(a & !b) | (!a & b)", "XOR")]
    [InlineData("(x & !y) | (!x & y)", "XOR")]
    [InlineData("!a | b", "→")]
    [InlineData("!x | y", "→")]
    public void ConvertToAdvancedForms_VariousPatterns_ShouldConvert(string input, string expectedContent)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.Contains(expectedContent, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertToAdvancedForms_NonPatternExpression_ShouldReturnUnchanged()
    {
        // Act
        var result = _detector.ConvertToAdvancedForms("a & b");

        // Assert
        Assert.Equal("a & b", result);
    }

    [Fact]
    public void ConvertToAdvancedForms_EmptyExpression_ShouldReturnOriginal()
    {
        // Act
        var result = _detector.ConvertToAdvancedForms("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void ConvertToAdvancedForms_InvalidExpression_ShouldReturnOriginal()
    {
        // Arrange
        string invalidExpr = "a & & b";

        // Act
        var result = _detector.ConvertToAdvancedForms(invalidExpr);

        // Assert
        Assert.Equal(invalidExpr, result);
    }

    [Fact]
    public void ConvertToAdvancedForms_NullInput_ShouldReturnEmpty()
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(null!);

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Theory]
    [InlineData("((a & !b) | (!a & b)) & (c | d) | (!e | f)")] // Complex mixed expression
    [InlineData("((a & !b) | (!a & b)) & ((c & !d) | (!c & d))")] // Nested XOR patterns under AND
    [InlineData("((a & !b) | (!a & b)) | ((c & !d) | (!c & d)) | ((e & !f) | (!e & f))")] // Many XOR patterns
    [InlineData("(a & !b) | (!a & b) | c")] // XOR with additional term
    [InlineData("(!a | b) & (c | d)")] // IMP inside conjunction
    [InlineData("!(!a | b)")] // Negated implication
    [InlineData("!!a")] // Double negation
    [InlineData("a & !a")] // Contradiction
    [InlineData("a | !a")] // Tautology
    [InlineData("a & b & c")] // Conjunction chain
    [InlineData("a | b | c")] // Disjunction chain
    [InlineData("a & (b | c)")] // Distributive candidate
    [InlineData("(a | b) & (a | c)")] // Factorization candidate
    public void ConvertToAdvancedForms_ArbitraryExpressions_ShouldProduceNonEmptyResult(string input)
    {
        // Act - smoke test: conversion must not throw and must return something usable
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ConvertToAdvancedForms_ManyXorPatterns_ShouldCompleteQuicklyAndDetectXor()
    {
        // Arrange - 50 XOR patterns (100 OR terms)
        var largeExpression = string.Join(" | ",
            Enumerable.Range(0, 50).Select(i => $"(x{i} & !y{i}) | (!x{i} & y{i})"));

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = _detector.ConvertToAdvancedForms(largeExpression);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("XOR", result);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000,
            $"ConvertToAdvancedForms took {stopwatch.ElapsedMilliseconds} ms, expected < 5000 ms");
    }

    #endregion

    #region EQV pattern detection

    [Fact]
    public void DetectAdvancedForms_ValidEqvPattern_ReturnsEqvNode()
    {
        // Arrange - (a & b) | (!a & !b)
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var leftAnd = new AndNode(a, b);
        var rightAnd = new AndNode(new NotNode(a), new NotNode(b));
        var orNode = new OrNode(leftAnd, rightAnd);

        // Act
        var result = _detector.DetectAdvancedForms(orNode);

        // Assert
        Assert.NotNull(result);
        var eqvNode = Assert.IsType<EqvNode>(result);
        Assert.Equal("a", ((VariableNode)eqvNode.Left).Name);
        Assert.Equal("b", ((VariableNode)eqvNode.Right).Name);
    }

    [Fact]
    public void TryFindDirectEqvPattern_ReversedPattern_ReturnsEqvNode()
    {
        // Arrange - reversed pattern: (!a & !b) | (a & b)
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var leftAnd = new AndNode(new NotNode(a), new NotNode(b));
        var rightAnd = new AndNode(a, b);
        var orNode = new OrNode(leftAnd, rightAnd);

        // Act
        var result = _detector.TryFindDirectEqvPattern(orNode);

        // Assert
        Assert.NotNull(result);
        var eqvNode = Assert.IsType<EqvNode>(result);
        Assert.Equal("a", ((VariableNode)eqvNode.Left).Name);
        Assert.Equal("b", ((VariableNode)eqvNode.Right).Name);
    }

    [Fact]
    public void TryFindDirectEqvPattern_InvalidPattern_ReturnsNull()
    {
        // Arrange - invalid pattern: (a & b) | (a & c)
        var a = new VariableNode("a");
        var leftAnd = new AndNode(a, new VariableNode("b"));
        var rightAnd = new AndNode(a, new VariableNode("c"));
        var orNode = new OrNode(leftAnd, rightAnd);

        // Act
        var result = _detector.TryFindDirectEqvPattern(orNode);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void DetectEquivalencePatternInAst_FindsEqvPattern()
    {
        // Arrange - ((a & b) | (!a & !b)) | c
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var eqvPattern = new OrNode(new AndNode(a, b), new AndNode(new NotNode(a), new NotNode(b)));
        var outerOr = new OrNode(eqvPattern, new VariableNode("c"));

        // Act
        var result = _detector.DetectEquivalencePatternInAst(outerOr);

        // Assert
        Assert.NotNull(result);
        var orResult = Assert.IsType<OrNode>(result);
        var eqvNode = Assert.IsType<EqvNode>(orResult.Left);
        Assert.Equal("a", ((VariableNode)eqvNode.Left).Name);
        Assert.Equal("b", ((VariableNode)eqvNode.Right).Name);
        Assert.Equal("c", ((VariableNode)orResult.Right).Name);
    }

    [Fact]
    public void DetectEquivalencePatternInAst_NoEqvPattern_ReturnsOriginal()
    {
        // Arrange
        var orNode = new OrNode(new VariableNode("a"), new VariableNode("b"));

        // Act
        var result = _detector.DetectEquivalencePatternInAst(orNode);

        // Assert
        Assert.Same(orNode, result);
    }

    [Theory]
    [InlineData("x", "y")]
    [InlineData("var1", "var2")]
    [InlineData("a", "b")]
    [InlineData("p", "q")]
    public void TryFindDirectEqvPattern_ValidPattern_TheoryTest(string var1Name, string var2Name)
    {
        // Arrange - (var1 & var2) | (!var1 & !var2)
        var var1 = new VariableNode(var1Name);
        var var2 = new VariableNode(var2Name);
        var positiveCase = new AndNode(var1, var2);
        var negativeCase = new AndNode(new NotNode(var1), new NotNode(var2));
        var eqvPattern = new OrNode(positiveCase, negativeCase);

        // Act
        var result = _detector.TryFindDirectEqvPattern(eqvPattern);

        // Assert
        Assert.NotNull(result);
        var eqvNode = Assert.IsType<EqvNode>(result);
        Assert.Equal(var1Name, ((VariableNode)eqvNode.Left).Name);
        Assert.Equal(var2Name, ((VariableNode)eqvNode.Right).Name);
    }

    [Fact]
    public void EqvPattern_WithCommutativeAnd_DetectedCorrectly()
    {
        // Arrange - pattern with commuted first AND: (b & a) | (!a & !b)
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var leftAnd = new AndNode(b, a); // Commuted
        var rightAnd = new AndNode(new NotNode(a), new NotNode(b));
        var eqvPattern = new OrNode(leftAnd, rightAnd);

        // Act
        var result = _detector.TryFindDirectEqvPattern(eqvPattern);

        // Assert - should still detect EQV pattern despite commutation
        Assert.NotNull(result);
        Assert.IsType<EqvNode>(result);
    }

    [Fact]
    public void EqvPattern_NestedInComplexExpression_DetectedCorrectly()
    {
        // Arrange - c & ((a & b) | (!a & !b))
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var eqvSubPattern = new OrNode(new AndNode(a, b), new AndNode(new NotNode(a), new NotNode(b)));
        var complexExpression = new AndNode(new VariableNode("c"), eqvSubPattern);

        // Act
        var result = _detector.DetectEquivalencePatternInAst(complexExpression);

        // Assert
        Assert.NotNull(result);
        var andResult = Assert.IsType<AndNode>(result);
        Assert.Equal("c", ((VariableNode)andResult.Left).Name);
        Assert.IsType<EqvNode>(andResult.Right);
    }

    #endregion
}
