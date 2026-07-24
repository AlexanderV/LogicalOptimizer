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
        Assert.Equal("1", Minimize(Parse("a | !a | b"))!.ToString());
        Assert.Equal("0", Minimize(Parse("a & !a"))!.ToString());
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

public class MultiOutputSharingTests
{
    [Fact]
    public void Minimize_SharedCube_ReusedAcrossOutputs()
    {
        // X = a&b | c&d, Y = a&b | !c&!d: the a&b cube must be shared, total distinct
        // cubes 3 instead of 4
        var csv = "a,b,c,d,X,Y\n" + string.Join("\n",
            Enumerable.Range(0, 16).Select(m =>
            {
                bool A = (m & 1) != 0, B = (m & 2) != 0, C = (m & 4) != 0, D = (m & 8) != 0;
                var x = A && B || C && D;
                var y = A && B || !C && !D;
                return $"{(A ? 1 : 0)},{(B ? 1 : 0)},{(C ? 1 : 0)},{(D ? 1 : 0)},{(x ? 1 : 0)},{(y ? 1 : 0)}";
            }));

        var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(csv, new[] { "X", "Y" });
        var results = MultiOutputMinimizer.Minimize(table, null, ResourceBudget.DefaultCoverStepLimit);

        Assert.Equal(2, results.Count);
        foreach (var (name, expression) in results)
        {
            var index = name == "X" ? 0 : 1;
            var onSet = new HashSet<int>(table.Outputs[index].OnSet);
            for (var m = 0; m < 16; m++)
            {
                var assignment = new Dictionary<string, bool>
                {
                    ["a"] = (m & 1) != 0,
                    ["b"] = (m & 2) != 0,
                    ["c"] = (m & 4) != 0,
                    ["d"] = (m & 8) != 0
                };
                Assert.Equal(onSet.Contains(m), TruthTable.Evaluate(expression, assignment));
            }

            Assert.Contains("a & b", expression.ToString());
        }
    }
}
