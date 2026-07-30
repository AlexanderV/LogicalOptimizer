using Xunit;

namespace LogicalOptimizer.Tests;

public class EquivalenceCheckerTests
{
    [Theory]
    [InlineData("a & b | a & !b", "a", true)]
    [InlineData("!(a & b)", "!a | !b", true)]
    [InlineData("a | b", "a & b", false)]
    [InlineData("a", "b", false)]
    public void Check_SmallExpressions_TruthTablePath(string left, string right, bool equivalent)
    {
        var result = EquivalenceChecker.Check(left, right);

        Assert.Equal(equivalent, result.AreEquivalent);
        if (!equivalent) Assert.NotNull(result.Counterexample);
    }

    [Fact]
    public void Check_DeMorganOverTwentyVariables_SatPathProvesEquivalence()
    {
        // 20 variables is beyond the truth-table guard range: this exercises the SAT miter
        var names = Enumerable.Range(1, 20).Select(i => $"v{i:D2}").ToList();
        var conjunction = string.Join(" & ", names);
        var negatedDisjunction = string.Join(" | ", names.Select(n => $"!{n}"));

        var result = EquivalenceChecker.Check($"!({conjunction})", negatedDisjunction);

        Assert.True(result.AreEquivalent);
    }

    [Fact]
    public void Check_LargeNonEquivalent_ProducesValidCounterexample()
    {
        var names = Enumerable.Range(1, 18).Select(i => $"v{i:D2}").ToList();
        var left = string.Join(" | ", names.Select((n, i) => i % 2 == 0 ? n : $"!{n}"));
        var right = string.Join(" | ", names); // differs when all odd-indexed are true, rest false

        var result = EquivalenceChecker.Check(left, right);

        Assert.False(result.AreEquivalent);
        Assert.NotNull(result.Counterexample);

        var leftAst = new Parser(new Lexer(left).Tokenize()).Parse();
        var rightAst = new Parser(new Lexer(right).Tokenize()).Parse();
        var assignment = result.Counterexample!.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.NotEqual(TruthTable.Evaluate(leftAst, assignment), TruthTable.Evaluate(rightAst, assignment));
    }

    [Fact]
    public void CheckWithSat_RandomSmallExpressions_AgreesWithTruthTable()
    {
        var generator = new Random(424242);
        var variables = new[] { "a", "b", "c", "d", "e" };

        for (var i = 0; i < 200; i++)
        {
            var left = RandomExpression(generator, variables, 4);
            var right = generator.Next(3) == 0 ? left : RandomExpression(generator, variables, 4);

            var leftAst = new Parser(new Lexer(left).Tokenize()).Parse();
            var rightAst = new Parser(new Lexer(right).Tokenize()).Parse();

            var expected = TruthTable.AreEquivalent(leftAst, rightAst);
            var satVerdict = EquivalenceChecker.CheckWithSat(leftAst, rightAst, 100_000);

            Assert.True(expected == satVerdict.AreEquivalent,
                $"'{left}' vs '{right}': table={expected}, sat={satVerdict.AreEquivalent}");
        }
    }

    private static string RandomExpression(Random random, string[] variables, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            var name = variables[random.Next(variables.Length)];
            return random.Next(2) == 0 ? name : $"!{name}";
        }

        var left = RandomExpression(random, variables, depth - 1);
        var right = RandomExpression(random, variables, depth - 1);
        return random.Next(2) == 0 ? $"({left} & {right})" : $"({left} | {right})";
    }

    [Fact]
    public void Optimizer_BeyondTruthTableRange_OutputSatVerifiedEquivalent()
    {
        // 16 variables: soundness guard runs on the SAT path inside Optimize; verify
        // the final result independently here as well
        var terms = Enumerable.Range(1, 8).Select(i => $"p{i:D2} & q{i:D2} | p{i:D2} & !q{i:D2}");
        var expression = string.Join(" | ", terms);

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false });

        var check = EquivalenceChecker.Check(expression, result.Optimized);
        Assert.True(check.AreEquivalent);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Optimizer_ThirtyVariableXorPairs_OutputEquivalent()
    {
        // Scale smoke test (salvaged from the deleted AST-advanced-forms stress test):
        // a 30-variable disjunction of XOR pairs must survive the full pipeline
        // semantically intact — verified by the SAT-based equivalence checker
        var terms = Enumerable.Range(0, 15)
            .Select(i => $"(v{2 * i + 1:D2} & !v{2 * i + 2:D2}) | (!v{2 * i + 1:D2} & v{2 * i + 2:D2})");
        var expression = string.Join(" | ", terms);

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression);

        Assert.True(EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent);
    }
}
