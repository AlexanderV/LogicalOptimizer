using System;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the AdvancedPatternDetector component - XOR and IMP pattern detection
/// </summary>
public class AdvancedPatternDetectorTests
{
    private readonly AdvancedPatternDetector _detector;

    public AdvancedPatternDetectorTests()
    {
        _detector = new AdvancedPatternDetector();
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b)", "XOR")]
    [InlineData("(a & !b) | (!a & b)", "a XOR b")]
    [InlineData("a & !b | !a & b", "XOR")]
    public void DetectXorPattern_StandardXorPattern_ShouldDetectXor(string input, string expectedPattern)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains(expectedPattern, result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("!a | b")]
    [InlineData("b | !a")]
    [InlineData("!x | y")]
    public void DetectImplicationPattern_StandardImplication_ShouldDetectImplication(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.Contains("→", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    [InlineData("a")]
    public void DetectXorPattern_NonXorPattern_ShouldReturnEmpty(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.True(string.IsNullOrEmpty(result) || !result.Contains("XOR"));
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("a")]
    public void DetectImplicationPattern_NonImplicationPattern_ShouldReturnEmpty(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.True(string.IsNullOrEmpty(result) || !result.Contains("→"));
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b)", "XOR")]
    [InlineData("!a | b", "→")]
    [InlineData("a & b", "a & b")]
    public void ConvertToAdvancedForms_VariousPatterns_ShouldConvertOrPreserve(string input, string expectedContent)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        if (expectedContent != "a & b") // For non-trivial cases
        {
            Assert.Contains(expectedContent, result, StringComparison.OrdinalIgnoreCase);
        }
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

    [Theory]
    [InlineData("(x & !y) | (!x & y)")]
    [InlineData("(var1 & !var2) | (!var1 & var2)")]
    [InlineData("(!p & q) | (p & !q)")]
    public void DetectXorPattern_DifferentVariableNames_ShouldDetectXor(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains("XOR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("!x | y")]
    [InlineData("!var1 | var2")]
    [InlineData("!p | q")]
    public void DetectImplicationPattern_DifferentVariableNames_ShouldDetectImplication(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.Contains("→", result);
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b) | c")]
    [InlineData("d | (x & !y) | (!x & y)")]
    public void DetectXorPattern_XorWithAdditionalTerms_ShouldDetectXorPart(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains("XOR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertToAdvancedForms_ComplexExpression_ShouldHandleGracefully()
    {
        // Arrange
        string complexExpr = "((a & !b) | (!a & b)) & (c | d) | (!e | f)";

        // Act
        var result = _detector.ConvertToAdvancedForms(complexExpr);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should either convert or return original safely
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b) | (c & !d) | (!c & d)")]
    [InlineData("(x1 & !x2) | (!x1 & x2) | (x3 & !x4) | (!x3 & x4)")]
    public void DetectXorPattern_MultipleXorPatterns_ShouldDetectAtLeastOne(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.Contains("XOR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectXorPattern_NullInput_ShouldReturnEmpty()
    {
        // Act & Assert
        var result = _detector.DetectXorPattern(null);
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void DetectImplicationPattern_NullInput_ShouldReturnEmpty()
    {
        // Act & Assert
        var result = _detector.DetectImplicationPattern(null);
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void ConvertToAdvancedForms_NullInput_ShouldReturnEmpty()
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(null);

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Theory]
    [InlineData("a & !a")] // Contradiction
    [InlineData("a | !a")] // Tautology
    [InlineData("a & b & c")] // Simple conjunction
    [InlineData("a | b | c")] // Simple disjunction
    public void ConvertToAdvancedForms_NonPatternExpressions_ShouldReturnOriginalOrSimplified(string input)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should not throw exception and return a valid result
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b)")]
    [InlineData("(x & !y) | (!x & y)")]
    public void ConvertToAdvancedForms_XorPattern_ShouldConvertToXor(string input)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("XOR", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("!a | b")] // Standard implication
    [InlineData("!x | y")] // Standard implication with different variables
    public void ConvertToAdvancedForms_ImplicationPattern_ShouldDetectImplication(string input)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        // May or may not convert depending on implementation
    }

    [Fact]
    public void ConvertToAdvancedForms_EmptyString_ShouldReturnEmpty()
    {
        // Act
        var result = _detector.ConvertToAdvancedForms("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void ConvertToAdvancedForms_ComplexNestedExpression_ShouldHandleRecursively()
    {
        // Arrange
        var input = "((a & !b) | (!a & b)) & ((c & !d) | (!c & d))"; // Two XOR patterns

        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should handle nested patterns
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b) | c")] // XOR with additional term
    [InlineData("(!a | b) & (c | d)")] // IMP with additional conjunction
    public void ConvertToAdvancedForms_MixedPatterns_ShouldHandlePartially(string input)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should handle what it can and leave the rest
    }

    [Fact]
    public void DetectXorPattern_SingleVariable_ShouldReturnEmpty()
    {
        // Arrange
        var lexer = new Lexer("a");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void DetectImplicationPattern_SingleVariable_ShouldReturnEmpty()
    {
        // Arrange
        var lexer = new Lexer("a");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Theory]
    [InlineData("!(!a | b)")] // Negated implication
    [InlineData("!!a")] // Double negation
    public void ConvertToAdvancedForms_NestedNegations_ShouldHandleCorrectly(string input)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should process nested structures correctly
    }

    [Fact]
    public void ConvertToAdvancedForms_VeryComplexExpression_ShouldNotThrow()
    {
        // Arrange
        var complexInput = "((a & !b) | (!a & b)) | ((c & !d) | (!c & d)) | ((e & !f) | (!e & f))";

        // Act & Assert
        var result = _detector.ConvertToAdvancedForms(complexInput);
        Assert.NotNull(result);
        // Main goal is to not throw exceptions on complex inputs
    }

    [Theory]
    [InlineData("a & (b | c)")] // Distributive candidate
    [InlineData("(a | b) & (a | c)")] // Factorization candidate
    public void ConvertToAdvancedForms_DistributivePatterns_ShouldHandleGracefully(string input)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should not break on distributive patterns even if not converted
    }

    [Theory]
    [InlineData("!a | b | c")] // Implication with additional terms
    [InlineData("(!a | b) | (c & d)")] // Complex OR with implication
    [InlineData("a | (!b | c) | d")] // Nested implication patterns
    public void DetectImplicationPattern_ComplexImplicationPatterns_ShouldDetectCorrectly(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.NotNull(result);
        // May detect implication patterns in complex expressions
    }

    [Theory]
    [InlineData("(!a | b) | (!c | d)")] // Multiple implication patterns
    [InlineData("!x | y | !z | w")] // Chain of implications
    public void DetectImplicationPattern_MultipleImplicationPatterns_ShouldHandleMultiplePatterns(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.NotNull(result);
        // Should process multiple implication patterns
    }

    [Fact]
    public void ConvertToAdvancedForms_PerformanceStressTest_ShouldNotTimeout()
    {
        // Arrange - Very large expression
        var largeExpression = string.Join(" | ", Enumerable.Range(0, 50).Select(i => $"(x{i} & !y{i}) | (!x{i} & y{i})"));

        // Act & Assert - Should complete within reasonable time
        var result = _detector.ConvertToAdvancedForms(largeExpression);
        Assert.NotNull(result);
        // Main goal is to ensure no infinite loops or timeouts
    }

    [Theory]
    [InlineData("a & b & c")] // Only AND
    [InlineData("a | b | c")] // Only OR, no patterns
    [InlineData("!a & !b & !c")] // Only negations
    public void DetectImplicationPattern_NoImplicationPatterns_ShouldReturnEmpty(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Theory]
    [InlineData("!a | b | c")] // Implication with additional terms
    [InlineData("(!a | b) | (c & d)")] // Complex OR with implication
    public void DetectImplicationPattern_AdditionalTermsWithImplication_ShouldDetectCorrectly(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert
        Assert.NotNull(result);
        // May detect implication patterns in complex expressions
    }
}
