using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     In-place adjacent-level-swap variable sifting. The primary gate is the
///     function-preserving invariant: an arbitrary sequence of swaps (and a full sifting
///     run) must leave <see cref="BinaryDecisionDiagram.Evaluate(int, IReadOnlyDictionary{string, bool})" />
///     and the model count untouched, and the canonical complement-edge representation intact.
/// </summary>
public class BddSiftingTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static Dictionary<string, bool> Assignment(IReadOnlyList<string> variables, int mask)
    {
        var assignment = new Dictionary<string, bool>();
        for (var j = 0; j < variables.Count; j++)
            assignment[variables[j]] = (mask & (1 << j)) != 0;
        return assignment;
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
    public void SwapAdjacentLevels_ArbitrarySequence_PreservesFunctionAndModelCount()
    {
        var random = new Random(9182736);
        var variables = new[] { "a", "b", "c", "d", "e" };
        var checkedAssignments = 0;

        for (var trial = 0; trial < 80; trial++)
        {
            var ast = Parse(RandomExpression(random, variables, 4));
            var bdd = BinaryDecisionDiagram.Build(ast);
            var order = bdd.Variables.ToList();
            var n = order.Count;
            if (n < 2) continue;

            // Golden reference captured before any swap: truth table + model count.
            var reference = new bool[1 << n];
            for (var m = 0; m < 1 << n; m++)
                reference[m] = bdd.Evaluate(bdd.Root, Assignment(order, m));
            var referenceCount = bdd.CountSatisfyingAssignments(bdd.Root);

            for (var step = 0; step < 25; step++)
            {
                bdd.SwapAdjacentLevels(random.Next(n - 1));

                for (var m = 0; m < 1 << n; m++)
                {
                    Assert.True(reference[m] == bdd.Evaluate(bdd.Root, Assignment(order, m)),
                        $"Trial {trial} step {step}: swap changed the function at mask {m} for '{ast}'");
                    checkedAssignments++;
                }

                Assert.True(referenceCount == bdd.CountSatisfyingAssignments(bdd.Root),
                    $"Trial {trial} step {step}: swap changed the model count for '{ast}'");
            }
        }

        Assert.True(checkedAssignments > 15_000,
            $"Expected a broad invariant sweep; only checked {checkedAssignments} assignments");
    }

    [Fact]
    public void BuildWithSiftedOrder_MatchesBruteForceTruthTableAndModelCount()
    {
        var random = new Random(424242);
        var variables = new[] { "a", "b", "c", "d", "e", "f" };

        for (var trial = 0; trial < 60; trial++)
        {
            var ast = Parse(RandomExpression(random, variables, 4));
            var ordered = ast.GetVariables().OrderBy(v => v).ToList();

            var expectedCount = 0;
            for (var m = 0; m < 1 << ordered.Count; m++)
                if (TruthTable.Evaluate(ast, Assignment(ordered, m)))
                    expectedCount++;

            var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast);

            for (var m = 0; m < 1 << ordered.Count; m++)
                Assert.True(TruthTable.Evaluate(ast, Assignment(ordered, m)) ==
                            sifted.Evaluate(sifted.Root, Assignment(ordered, m)),
                    $"Trial {trial}: sifting changed semantics for '{ast}' at mask {m}");

            Assert.Equal(new BigInteger(expectedCount), sifted.CountSatisfyingAssignments(sifted.Root));
        }
    }

    [Fact]
    public void Swaps_PreserveComplementEdgeCanonicity_ForFreshlyBuiltFunctions()
    {
        // After reordering, building a function and its negation from scratch must still
        // land on the same regular node differing only by the complement bit, and two
        // equivalent forms must build to the byte-identical edge.
        var manager = new BinaryDecisionDiagram(new[] { "a", "b", "c", "d" });

        // Reorder the manager with a handful of adjacent swaps first.
        manager.SwapAdjacentLevels(0);
        manager.SwapAdjacentLevels(2);
        manager.SwapAdjacentLevels(1);

        var f = manager.FromAst(Parse("a & b | !a & c & d"));
        var notF = manager.FromAst(Parse("!(a & b | !a & c & d)"));
        Assert.Equal(BinaryDecisionDiagram.Complement(f), notF);
        Assert.Equal(BinaryDecisionDiagram.Regular(f), BinaryDecisionDiagram.Regular(notF));

        // An equivalent rewrite must reduce to the byte-identical edge (same node AND sign).
        var equivalent = manager.FromAst(Parse("a & b | !a & (c & d)"));
        Assert.Equal(f, equivalent);
    }

    [Fact]
    public void BuildWithSiftedOrder_NeverWorseThanStaticBestOnCorpus()
    {
        var random = new Random(31337);
        var variables = new[] { "a", "b", "c", "d", "e", "f", "g" };

        for (var trial = 0; trial < 40; trial++)
        {
            var ast = Parse(RandomExpression(random, variables, 5));
            var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast);
            var staticBest = BinaryDecisionDiagram.BuildWithBestOrder(ast);

            Assert.True(sifted.NodeCount <= staticBest.NodeCount,
                $"Trial {trial}: sifting ({sifted.NodeCount}) lost to static best ({staticBest.NodeCount}) for '{ast}'");
        }
    }

    [Fact]
    public void BuildWithSiftedOrder_ReducesSizeOnHiddenPairing()
    {
        // Blocked pairing a1..a6,b1..b6 that all three static heuristics keep un-interleaved;
        // only per-variable sifting can migrate each b_i next to its a_i.
        var preamble = new AndNode(Enumerable.Range(1, 6)
            .Select(i => (AstNode)new VariableNode($"a{i}")).ToList());
        var pairTerms = Enumerable.Range(1, 6)
            .Select(i => (AstNode)new AndNode(new VariableNode($"a{i}"), new VariableNode($"b{i}")));
        var ast = new OrNode(new AstNode[] { preamble }.Concat(pairTerms).ToList());

        var staticBest = BinaryDecisionDiagram.BuildWithBestOrder(ast);
        var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast);

        Assert.True(sifted.NodeCount < staticBest.NodeCount,
            $"Sifting found no improvement: {sifted.NodeCount} vs static {staticBest.NodeCount}");

        // And it is still the same function.
        var ordered = ast.GetVariables().OrderBy(v => v).ToList();
        for (var m = 0; m < 1 << ordered.Count; m++)
            Assert.True(TruthTable.Evaluate(ast, Assignment(ordered, m)) ==
                        sifted.Evaluate(sifted.Root, Assignment(ordered, m)),
                $"Sifted function diverged at mask {m}");
    }

    [Fact]
    public void BuildWithSiftedOrder_PreCancelledToken_Throws()
    {
        var expression = string.Join(" | ", Enumerable.Range(1, 6).Select(i => $"a{i} & b{i}"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            BinaryDecisionDiagram.BuildWithSiftedOrder(Parse(expression), cancellationToken: cts.Token));
    }

    [Fact]
    public void BuildWithSiftedOrder_TinyBudget_Throws()
    {
        var expression = string.Join(" | ", Enumerable.Range(1, 8).Select(i => $"a{i} & b{i}"));
        Assert.Throws<InvalidOperationException>(() =>
            BinaryDecisionDiagram.BuildWithSiftedOrder(Parse(expression), nodeBudget: 8));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void BuildWithSiftedOrder_InPlace_ReordersLargeFunctionQuickly()
    {
        // A larger blocked pairing: rebuild-based sifting would reconstruct the whole diagram
        // for every trial position. The in-place swap only touches two levels per step, so a
        // 20-variable instance completes well under a second.
        var preamble = new AndNode(Enumerable.Range(1, 10)
            .Select(i => (AstNode)new VariableNode($"a{i}")).ToList());
        var pairTerms = Enumerable.Range(1, 10)
            .Select(i => (AstNode)new AndNode(new VariableNode($"a{i}"), new VariableNode($"b{i}")));
        var ast = new OrNode(new AstNode[] { preamble }.Concat(pairTerms).ToList());

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast, maxRebuilds: 2000);
        watch.Stop();

        var staticBest = BinaryDecisionDiagram.BuildWithBestOrder(ast);
        Assert.True(sifted.NodeCount <= staticBest.NodeCount);
        Assert.True(watch.Elapsed.TotalSeconds < 5,
            $"In-place sifting took {watch.Elapsed.TotalSeconds:F2}s — expected in-place speed");
    }
}
