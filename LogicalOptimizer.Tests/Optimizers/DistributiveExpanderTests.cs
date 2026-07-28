using LogicalOptimizer.Rewrite;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for DistributiveExpander (v1: DistributiveOptimizer) — distribution of AND
/// over OR and its dual. Not part of the rewrite pipeline; used by expand-reduce and
/// the normal-form converter. Expectations are canonical strings because both the
/// parser and the expander build through FormulaFactory.
/// </summary>
public class DistributiveExpanderTests
{
    private static AstNode Expand(string input, FormulaFactory factory)
    {
        var ast = new Parser(new Lexer(input).Tokenize(), factory).Parse();
        return new DistributiveExpander(factory).ExpandOnce(ast);
    }

    [Theory]
    [InlineData("a & (b | c)", "a & b | a & c")]
    [InlineData("a | (b & c)", "(a | b) & (a | c)")]
    [InlineData("(b | c) & a", "a & b | a & c")]
    [InlineData("(b & c) | a", "(a | b) & (a | c)")]
    public void ExpandOnce_ShouldApplyDistributiveLaws(string input, string expected)
    {
        var result = Expand(input, new FormulaFactory());

        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    public void ExpandOnce_ShouldNotChangeSimpleExpressions(string input)
    {
        var result = Expand(input, new FormulaFactory());

        Assert.Equal(input, result.ToString());
    }

    [Fact]
    public void ToDnf_ShouldFullyDistributeAndOverOr()
    {
        var factory = new FormulaFactory();
        var ast = new Parser(new Lexer("(a | b) & (c | d)").Tokenize(), factory).Parse();

        var result = new DistributiveExpander(factory).ToDnf(ast);

        Assert.Equal("a & c | a & d | b & c | b & d", result.ToString());
    }

    [Fact]
    public void ToCnf_ShouldFullyDistributeOrOverAnd()
    {
        var factory = new FormulaFactory();
        var ast = new Parser(new Lexer("a & b | c & d").Tokenize(), factory).Parse();

        var result = new DistributiveExpander(factory).ToCnf(ast);

        Assert.Equal("(a | c) & (a | d) & (b | c) & (b | d)", result.ToString());
    }

    [Fact]
    public void ToDnf_WhenDistributionBudgetExceeded_ShouldThrow()
    {
        var factory = new FormulaFactory();
        var ast = new Parser(new Lexer("(a | b) & (c | d)").Tokenize(), factory).Parse();

        // A budget of a single distribution step cannot cover the 2x2 cross product.
        var expander = new DistributiveExpander(factory, maxDistributionSteps: 1);

        Assert.Throws<NormalFormTooLargeException>(() => expander.ToDnf(ast));
    }
}
