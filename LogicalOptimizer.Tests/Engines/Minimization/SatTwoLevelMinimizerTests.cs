using Xunit;

namespace LogicalOptimizer.Tests;

public class SatTwoLevelMinimizerTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static AstNode? Minimize(AstNode formula)
    {
        return SatTwoLevelMinimizer.TryMinimize(formula,
            PerformanceValidator.SAT_MINIMIZATION_CUBE_LIMIT,
            PerformanceValidator.SAT_MINIMIZATION_QUERY_CONFLICTS);
    }

    [Theory]
    [InlineData("a & b | a & !b", 1)] // collapses to a
    [InlineData("(a | b) & (a | c)", 3)] // a | b & c
    [InlineData("a & b | !a & c | b & c", 4)] // consensus term is redundant
    public void TryMinimize_SmallFunctions_MatchesKnownMinimum(string expression, int expectedLiterals)
    {
        var result = Minimize(Parse(expression));

        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(Parse(expression), result!));
        Assert.Equal(expectedLiterals, AstMetrics.CountLiterals(result!));
    }

    [Fact]
    public void TryMinimize_TautologyAndContradiction()
    {
        // Raw constructors: the parser folds complements to constants at build time,
        // and TryMinimize declines constant inputs (no variables) — the tautology /
        // contradiction handling being pinned here only triggers on real trees
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var tautology = new OrNode(new AstNode[] { a, new NotNode(a), b });
        var contradiction = new AndNode(a, new NotNode(a));

        Assert.Equal("1", Minimize(tautology)!.ToString());
        Assert.Equal("0", Minimize(contradiction)!.ToString());
    }

    [Fact]
    public void TryMinimize_RandomFunctions_NeverWorseThanExactByMuch()
    {
        // Против QM-еталона: результат еквівалентний і в межах 20% від точного мінімуму
        var random = new Random(345);
        var variables = new[] { "a", "b", "c", "d", "e", "f" };

        for (var i = 0; i < 50; i++)
        {
            var expression = RandomExpression(random, variables, 4);
            var ast = Parse(expression);
            if (ast is ConstantNode) continue; // parse-time folding; TryMinimize declines constants
            var satResult = Minimize(ast);
            Assert.NotNull(satResult);
            Assert.True(TruthTable.AreEquivalent(ast, satResult!), $"Non-equivalent for '{expression}'");

            var ordered = ast.GetVariables().OrderBy(v => v).ToList();
            if (ordered.Count == 0) continue;
            var onSet = new HashSet<int>();
            var assignment = new Dictionary<string, bool>();
            for (var mask = 0; mask < 1 << ordered.Count; mask++)
            {
                for (var j = 0; j < ordered.Count; j++)
                    assignment[ordered[j]] = (mask & (1 << j)) != 0;
                if (TruthTable.Evaluate(ast, assignment)) onSet.Add(mask);
            }

            var exact = TruthTableMinimizer.MinimalSop(ordered, onSet);
            var exactLiterals = AstMetrics.CountLiterals(exact);
            var satLiterals = AstMetrics.CountLiterals(satResult!);
            Assert.True(satLiterals <= Math.Max(exactLiterals + 2, exactLiterals * 12 / 10),
                $"'{expression}': sat={satLiterals}, exact={exactLiterals}");
        }
    }

    [Fact]
    public void TryMinimize_TwentyVariables_CollapsesRedundantStructure()
    {
        // (x1 & y1 | x1 & !y1) | ... : each pair collapses to x_i — 10 literals total
        var expression = string.Join(" | ",
            Enumerable.Range(1, 10).Select(i => $"x{i:D2} & y{i:D2} | x{i:D2} & !y{i:D2}"));
        var ast = Parse(expression);

        var result = Minimize(ast);

        Assert.NotNull(result);
        Assert.Equal(10, AstMetrics.CountLiterals(result!));
        Assert.True(EquivalenceChecker.Check(ast, result!).AreEquivalent);
    }

    [Fact]
    public void OptimizeExpression_MidRange_UsesSatCover()
    {
        // 14 variables: past the exact gate; the SAT cover must collapse the redundancy
        // and provide a computed DNF instead of "-"
        var expression = string.Join(" | ",
            Enumerable.Range(1, 7).Select(i => $"p{i} & q{i} | p{i} & !q{i}"));

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeAdvancedForms = false });

        Assert.Equal(7, AstMetrics.CountLiterals(Parse(result.Optimized)));
        Assert.Equal(ComputationStatus.Computed, result.DnfStatus);
        Assert.NotEqual("-", result.DNF);
        Assert.Equal(MinimizationStatus.Heuristic, result.MinimizationStatus);
        Assert.True(EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent);
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
}
