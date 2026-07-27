using Xunit;

namespace LogicalOptimizer.Tests;

public class TransformationsTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    [Theory]
    [InlineData("a & b | a", "a")]
    [InlineData("a | a & b | c & d & a | b", "a | b")]
    [InlineData("a & b | c", "c | a & b")] // nothing absorbed (canonical order: literal first)
    public void SubsumeDnf_DropsAbsorbedTerms(string input, string expected)
    {
        Assert.Equal(expected, Transformations.SubsumeDnf(Parse(input)).ToString());
    }

    [Theory]
    [InlineData("(a | b) & a", "a")]
    [InlineData("(a | b | c) & (a | b) & (d | a | b)", "a | b")]
    public void SubsumeCnf_DropsAbsorbedClauses(string input, string expected)
    {
        Assert.Equal(expected, Transformations.SubsumeCnf(Parse(input)).ToString());
    }

    [Fact]
    public void Subsume_NonMatchingShapes_ReturnedUnchanged()
    {
        var literal = Parse("a");
        Assert.Same(literal, Transformations.SubsumeDnf(literal));
        Assert.Same(literal, Transformations.SubsumeCnf(literal));
    }

    [Fact]
    public void Subsume_WorksAtLargeScale()
    {
        // 40 variables, absorbed pairs: no truth table could ever be built here
        var terms = Enumerable.Range(1, 20).Select(i => $"x{i:D2} & y{i:D2} | x{i:D2}");
        var input = Parse(string.Join(" | ", terms));

        var result = Transformations.SubsumeDnf(input);

        Assert.Equal(20, AstMetrics.CountLiterals(result));
        Assert.True(EquivalenceChecker.Check(input, result).AreEquivalent);
    }
}
