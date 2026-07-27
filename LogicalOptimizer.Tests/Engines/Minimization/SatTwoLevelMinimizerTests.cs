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

public class MultiOutputSharingTests
{
    [Fact]
    public void Minimize_SharedCube_ReplacesAnIndependentCube()
    {
        // X = !c&d | a&b&!c   (ON-set {3,8,9,10,11})
        // Y = c | d           (ON-set {4..15})
        //
        // Independent minimal covers: X = {!c&d, a&b&!c}, Y = {c, d} — 4 distinct cubes.
        // The X-only cube "!c & d" covers exactly Y's c=0,d=1 region, so the shared
        // re-cover reuses it inside Y and drops Y's "d" cube: Y becomes {!c&d, c}, and
        // the two outputs together use only 3 distinct cubes — strictly cheaper
        // ((literals 6, cubes 3) vs the independent (7, 4)), so TrySharedCovers wins.
        //
        // This is the observable sharing outcome the previous version missed: with the
        // shared path DISABLED, chosen == the independent covers, Y renders "c | d"
        // (no "!c & d") and the distinct-cube total is 4 — every assertion below flips.
        var csv = "a,b,c,d,X,Y\n" + string.Join("\n",
            Enumerable.Range(0, 16).Select(m =>
            {
                bool A = (m & 1) != 0, B = (m & 2) != 0, C = (m & 4) != 0, D = (m & 8) != 0;
                var x = !C && D || A && B && !C;
                var y = C || D;
                return $"{(A ? 1 : 0)},{(B ? 1 : 0)},{(C ? 1 : 0)},{(D ? 1 : 0)},{(x ? 1 : 0)},{(y ? 1 : 0)}";
            }));

        var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(csv, new[] { "X", "Y" });
        var results = MultiOutputMinimizer.Minimize(table, null, ResourceBudget.DefaultCoverStepLimit);

        Assert.Equal(2, results.Count);

        // Every output stays provably equivalent to its table (the sharing must not
        // change semantics, only reuse cubes).
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
        }

        var x = results.Single(r => r.Name == "X").Expression;
        var y = results.Single(r => r.Name == "Y").Expression;

        // Observable outcome 1: the cube "!c & d" — from X's cover — appears in Y, whose
        // OWN minimal cover ("c | d") would never contain it. Only the shared re-cover
        // puts it there.
        Assert.Contains("!c & d", y.ToString());

        // Observable outcome 2: the two expressions share a cube, so the number of
        // DISTINCT product terms across both is 3.
        var sharedDistinct = DistinctCubes(x).Concat(DistinctCubes(y)).Distinct().Count();
        Assert.Equal(3, sharedDistinct);

        // Independent-cover oracle (the product's own per-output minimum, computed
        // directly): 4 distinct cubes. Sharing is a STRICT win — this is exactly what
        // TrySharedCovers exists to produce, and what a disabled shared path would lose.
        Assert.Equal(4, IndependentDistinctCubeCount(table));
        Assert.True(sharedDistinct < IndependentDistinctCubeCount(table));
    }

    /// <summary>Distinct product-term strings of a sum-of-products expression.</summary>
    private static IEnumerable<string> DistinctCubes(AstNode expression)
    {
        return expression is OrNode or
            ? or.Operands.Select(operand => operand.ToString()!)
            : new[] { expression.ToString()! };
    }

    /// <summary>
    ///     Number of distinct cubes across every output's INDEPENDENT minimal cover,
    ///     computed straight from the product minimizer — the baseline the shared
    ///     re-cover must beat.
    /// </summary>
    private static int IndependentDistinctCubeCount(MultiOutputTable table)
    {
        var cubes = new HashSet<(int Mask, int Value)>();
        foreach (var output in table.Outputs)
        {
            var on = new HashSet<int>(output.OnSet);
            var dc = new HashSet<int>(output.DontCareSet);
            dc.ExceptWith(on);
            var (cover, _) = TruthTableMinimizer.MinimalCoverCubes(table.Variables.Count, on, dc,
                null, default, ResourceBudget.DefaultCoverStepLimit);
            foreach (var cube in cover) cubes.Add(cube);
        }

        return cubes.Count;
    }
}
