using Xunit;

namespace LogicalOptimizer.Tests;


public class EquivalenceCheckerImplementationsTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    public static IEnumerable<object[]> Checkers()
    {
        yield return new object[] { new HybridEquivalenceChecker() };
        yield return new object[] { new BddEquivalenceChecker() };
    }

    [Theory]
    [MemberData(nameof(Checkers))]
    public void Check_EquivalentPair_True(IEquivalenceChecker checker)
    {
        var result = checker.Check(Parse("!(a & b)"), Parse("!a | !b"));
        Assert.True(result.AreEquivalent);
        Assert.Null(result.Counterexample);
    }

    [Theory]
    [MemberData(nameof(Checkers))]
    public void Check_NonEquivalentPair_CounterexampleEvaluatesDifferently(IEquivalenceChecker checker)
    {
        var left = Parse("a | b & c");
        var right = Parse("(a | b) & c");

        var result = checker.Check(left, right);

        Assert.False(result.AreEquivalent);
        Assert.NotNull(result.Counterexample);
        var assignment = result.Counterexample!.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.NotEqual(TruthTable.Evaluate(left, assignment), TruthTable.Evaluate(right, assignment));
    }

    [Fact]
    public void BddChecker_TinyBudget_Unknown()
    {
        var expression = string.Join(" | ", Enumerable.Range(1, 8).Select(i => $"a{i} & b{i}"));
        var checker = new BddEquivalenceChecker(nodeBudget: 8);

        Assert.Null(checker.Check(Parse(expression), Parse("x | " + expression)).AreEquivalent);
    }
}
