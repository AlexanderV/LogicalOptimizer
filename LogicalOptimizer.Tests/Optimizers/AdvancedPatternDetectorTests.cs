using System;
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
    [InlineData("(a & !b) | (!a & b)", "a XOR b")]
    [InlineData("a & !b | !a & b", "a XOR b")]
    [InlineData("(x & !y) | (!x & y)", "x XOR y")]
    [InlineData("(var1 & !var2) | (!var1 & var2)", "var1 XOR var2")]
    [InlineData("(!p & q) | (p & !q)", "p XOR q")]
    public void DetectXorPattern_StandardXorPattern_ShouldDetectXor(string input, string expected)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert - the rendering is deterministic, so pin the exact XOR form (a bare
        // Contains("XOR") would pass on any misrendered operands or spurious extra terms)
        Assert.Equal(expected, result);
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
    [InlineData("(a & !b) | (!a & b) | c", "(a XOR b) | c")]
    [InlineData("d | (x & !y) | (!x & y)", "(x XOR y) | d")]
    [InlineData("(a & !b) | (!a & b) | (c & d)", "(a XOR b) | c & d")]
    public void DetectXorPattern_XorWithAdditionalTerms_ShouldDetectXorPart(string input, string expected)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert - pin the whole rewrite: the XOR is folded AND the residual terms are
        // rendered verbatim, so a rule that dropped/mangled the non-XOR part is caught
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("(a & !b) | (!a & b) | (c & !d) | (!c & d)", "(a XOR b) | (c XOR d)")]
    [InlineData("(x1 & !x2) | (!x1 & x2) | (x3 & !x4) | (!x3 & x4)", "(x1 XOR x2) | (x3 XOR x4)")]
    [InlineData("(v1 & !v2) | (!v1 & v2) | (v3 & !v4) | (!v3 & v4) | (v5 & !v6) | (!v5 & v6)",
        "(v1 XOR v2) | (v3 XOR v4) | (v5 XOR v6)")]
    public void DetectXorPattern_MultipleXorPatterns_ShouldDetectAll(string input, string expected)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectXorPattern(ast);

        // Assert - every XOR pair must be folded, not merely "at least one"
        Assert.Equal(expected, result);
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
    [InlineData("!a | b", "a → b")]
    [InlineData("b | !a", "a → b")]
    [InlineData("!x | y", "x → y")]
    [InlineData("y | !x", "x → y")]
    [InlineData("!var1 | var2", "var1 → var2")]
    [InlineData("var2 | !var1", "var1 → var2")]
    [InlineData("!p | q", "p → q")]
    public void DetectImplicationPattern_StandardImplication_ShouldDetectImplication(string input, string expected)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert - the antecedent/consequent order is deterministic; pin it exactly so a
        // swapped arrow (b → a) cannot slip through a bare Contains("→")
        Assert.Equal(expected, result);
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
    [InlineData("!a | b | c", "(a → b) | c")] // Implication with additional terms
    [InlineData("(!a | b) | (c & d)", "(a → b) | c & d")] // Complex OR with implication
    [InlineData("a | (!b | c) | d", "(b → a) | c | d")] // Nested implication patterns
    public void DetectImplicationPattern_ComplexImplicationPatterns_ShouldDetectCorrectly(string input, string expected)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert - pin the whole rewrite including which negated literal became the arrow
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("(!a | b) | (!c | d)", "(a → b) | (c → d)")] // Multiple implication patterns
    [InlineData("!x | y | !z | w", "(x → w) | (z → y)")] // Chain of implications
    public void DetectImplicationPattern_MultipleImplicationPatterns_ShouldHandleMultiplePatterns(string input,
        string expected)
    {
        // Arrange
        var ast = ParseExpression(input);

        // Act
        var result = _detector.DetectImplicationPattern(ast);

        // Assert - both arrows must be formed with the exact pairing the detector chose
        Assert.Equal(expected, result);
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
    [InlineData("(a & !b) | (!a & b)", "a XOR b")]
    [InlineData("(x & !y) | (!x & y)", "x XOR y")]
    [InlineData("!a | b", "a → b")]
    [InlineData("!x | y", "x → y")]
    public void ConvertToAdvancedForms_VariousPatterns_ShouldConvert(string input, string expected)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert - the string entry point renders deterministically, so pin the whole form. A
        // Contains("XOR")/Contains("→") check passed on swapped or extra operands.
        Assert.Equal(expected, result);
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
    public void ConvertToAdvancedForms_NullInput_ReturnsNull()
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(null!);

        // Assert - null in, null OUT (not ""). Which of the two comes back matters to a caller
        // that concatenates or compares the result, and the previous IsNullOrEmpty assertion hid
        // the difference: the method is pass-through for null, while ""
        // (ConvertToAdvancedForms_EmptyExpression_ShouldReturnOriginal) comes back as "".
        Assert.Null(result);
    }

    [Theory]
    [InlineData("((a & !b) | (!a & b)) & (c | d) | (!e | f)", "(e → f) | (c | d) & (a & !b | b & !a)")] // Complex mixed: IMP surfaces
    [InlineData("((a & !b) | (!a & b)) & ((c & !d) | (!c & d))", "(a XOR b) & (c XOR d)")] // Nested XOR patterns under AND
    [InlineData("((a & !b) | (!a & b)) | ((c & !d) | (!c & d)) | ((e & !f) | (!e & f))", "(a XOR b) | (c XOR d) | (e XOR f)")] // Many XOR patterns
    [InlineData("(a & !b) | (!a & b) | c", "(a XOR b) | c")] // XOR with additional term
    [InlineData("(!a | b) & (c | d)", "(c | d) & (a → b)")] // IMP inside conjunction
    [InlineData("!(!a | b)", "!(a → b)")] // Negated implication
    public void ConvertToAdvancedForms_PatternBearingExpressions_ProduceExpectedAdvancedForm(string input,
        string expected)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert - the advanced operator (XOR / →) is actually detected and the exact
        // rendered form is pinned, not merely "non-empty".
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("!!a", "a")] // Double negation folds
    [InlineData("a & !a", "0")] // Contradiction folds to constant
    [InlineData("a | !a", "1")] // Tautology folds to constant
    [InlineData("a & b & c", "a & b & c")] // Conjunction chain - no pattern
    [InlineData("a | b | c", "a | b | c")] // Disjunction chain - no pattern
    [InlineData("a & (b | c)", "a & (b | c)")] // Distributive candidate - no pattern
    [InlineData("(a | b) & (a | c)", "(a | b) & (a | c)")] // Factorization candidate - no pattern
    public void ConvertToAdvancedForms_NoPatternExpressions_ReturnCanonicalFormUnchanged(string input, string expected)
    {
        // Act
        var result = _detector.ConvertToAdvancedForms(input);

        // Assert - no advanced operator introduced; the canonical form is returned exactly.
        Assert.Equal(expected, result);
        Assert.DoesNotContain("XOR", result);
        Assert.DoesNotContain("→", result);
        Assert.DoesNotContain("↔", result);
    }

    [Fact]
    public void ConvertToAdvancedForms_ManyXorPatterns_ShouldDetectEveryXor()
    {
        // Arrange - 50 XOR patterns (100 OR terms)
        var largeExpression = string.Join(" | ",
            Enumerable.Range(0, 50).Select(i => $"(x{i} & !y{i}) | (!x{i} & y{i})"));

        // Act
        var result = _detector.ConvertToAdvancedForms(largeExpression);

        // Assert - every one of the 50 XOR pairs is recognised and rendered as "xN XOR yN",
        // not just "some XOR somewhere". This asserts correctness at scale under the
        // deterministic pattern-detection budget (no wall-clock timing).
        Assert.NotNull(result);
        for (var i = 0; i < 50; i++)
            Assert.Contains($"x{i} XOR y{i}", result);
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
        var eqvNode = Assert.Single(orResult.Operands.OfType<EqvNode>());
        Assert.Equal("a", ((VariableNode)eqvNode.Left).Name);
        Assert.Equal("b", ((VariableNode)eqvNode.Right).Name);
        Assert.Contains(orResult.Operands, o => o is VariableNode { Name: "c" });
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
    // One row is enough: the other three differed only in variable NAMES, and renaming
    // invariance is pinned globally by MetamorphicTests.Renaming_CommutesWithOptimization.
    [InlineData("x", "y")]
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

        // Assert - the EQV is detected despite commutation AND carries the right operands. The
        // order follows the FIRST conjunct of the first AND, so the commuted input "(b & a)"
        // yields b ↔ a; ↔ is symmetric, so that is correct, but it is deterministic and therefore
        // pinned (a bare IsType check accepted an EqvNode built from any pair at all).
        var eqvNode = Assert.IsType<EqvNode>(result);
        Assert.Equal("b", ((VariableNode)eqvNode.Left).Name);
        Assert.Equal("a", ((VariableNode)eqvNode.Right).Name);
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
        Assert.Contains(andResult.Operands, o => o is VariableNode { Name: "c" });
        Assert.Single(andResult.Operands.OfType<EqvNode>());
    }

    #endregion
}
