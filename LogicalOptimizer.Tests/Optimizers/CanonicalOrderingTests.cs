using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Canonical operand ordering (v1: CommutativityOptimizer, v2: construction-time
/// sorting in FormulaFactory). Parsing builds through the factory, so parser output
/// is already sorted by the canonical key (complexity score, then stable string key).
/// </summary>
public class CanonicalOrderingTests
{
    [Theory]
    [InlineData("a & b", "a & b")]
    [InlineData("b & a", "a & b")]
    [InlineData("a | b", "a | b")]
    [InlineData("b | a", "a | b")]
    [InlineData("a & b & c", "a & b & c")]
    [InlineData("c & b & a", "a & b & c")]
    public void Factory_SortsOperandsConsistently(string input, string expected)
    {
        var ast = new Parser(new Lexer(input).Tokenize()).Parse();

        Assert.Equal(expected, ast.ToString());
    }

    [Theory]
    [InlineData("x2 & x3 | x1 | !x4", "x1 | !x4 | x2 & x3")]
    [InlineData("!b & a | c", "c | a & !b")]
    // Ordinal tie-break among two negations of equal complexity (Not-vs-Not key)
    [InlineData("!b & !a", "!a & !b")]
    // Ordinal tie-break among two equal-complexity composites, plus the variable
    // (lowest complexity) sorting first: exercises the CreateNaryKey sort.
    [InlineData("c & d | a & b | e", "e | a & b | c & d")]
    public void Factory_OrdersByComplexityThenKey(string input, string expected)
    {
        // Simple literals (variables before negations) come before composite operands
        var ast = new Parser(new Lexer(input).Tokenize()).Parse();

        Assert.Equal(expected, ast.ToString());
    }

    [Fact]
    public void Factory_SortingMakesConstructionOrderIrrelevant()
    {
        var f = new FormulaFactory();
        var forward = f.And(f.Variable("a"), f.Variable("b"), f.Variable("c"));
        var backward = f.And(f.Variable("c"), f.Variable("b"), f.Variable("a"));

        Assert.Same(forward, backward);
        Assert.Equal("a & b & c", forward.ToString());
    }
}
