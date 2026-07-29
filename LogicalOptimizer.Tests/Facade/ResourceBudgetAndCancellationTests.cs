using Xunit;

namespace LogicalOptimizer.Tests;

public class ResourceBudgetAndCancellationTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    [Fact]
    public void OptimizeExpression_PreCancelledToken_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new BooleanExpressionOptimizer().OptimizeExpression("a & b | c & d",
                new OptimizationOptions { CancellationToken = source.Token }));
    }

    [Fact]
    public void SatSolver_PreCancelledToken_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var solver = new SatSolver(2);
        solver.AddClause(1, 2);

        Assert.Throws<OperationCanceledException>(() => solver.Solve(cancellationToken: source.Token));
    }

    [Fact]
    public void MinimalSop_PreCancelledToken_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TruthTableMinimizer.MinimalSop(new List<string> { "a" }, new HashSet<int> { 1 },
                cancellationToken: source.Token));
    }

    [Fact]
    public void BddBuild_PreCancelledToken_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            BinaryDecisionDiagram.Build(Parse("a & b | c"), cancellationToken: source.Token));
    }

    [Fact]
    public void TightQmBudget_FallsBackWithoutError()
    {
        // 11 variables with a 1-comparison budget: exact path must give up, result stays sound
        var expression = "a & b | a & c | " + string.Join(" | ",
            Enumerable.Range(1, 9).Select(i => $"x{i} & a"));

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions
            {
                Budget = new ResourceBudget { QmPairComparisonLimit = 1 },
                ComputeAdvancedForms = false
            });

        Assert.True(EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent);
    }

    [Fact]
    public void DefaultBudget_HasDocumentedValues()
    {
        // Pin the documented defaults: changing any of these alters the scale contract
        // (see ResourceBudget/PerformanceValidator docs) and must be a conscious decision
        var budget = ResourceBudget.Default;
        Assert.Equal(5_000_000, budget.QmPairComparisonLimit);
        Assert.Equal(200_000, budget.CoverStepLimit);
        Assert.Equal(200_000, budget.SatConflictLimit);
        Assert.Equal(20_000, budget.SoundnessGuardConflictLimit);
        Assert.Equal(1_000_000, budget.BddNodeLimit);
        Assert.Equal(50_000_000, budget.ParseTokenLimit);
    }
}

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
