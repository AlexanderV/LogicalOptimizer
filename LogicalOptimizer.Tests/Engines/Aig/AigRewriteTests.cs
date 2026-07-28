using System.Text;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The DAG-aware cut rewriter's two sacred invariants: it must never change the function,
///     and it must never grow the AND-node count. Both are checked here — function preservation
///     by exhaustive (small n) / sampled (larger n) evaluation, and the size invariant directly
///     off <see cref="AndInverterGraph.AndNodeCount" />. Crafted cases prove the library actually
///     fires and reduces nodes.
/// </summary>
public class AigRewriteTests
{
    private static bool AreEquivalent(AndInverterGraph a, AndInverterGraph b, IReadOnlyList<string> variables)
    {
        var assignment = new Dictionary<string, bool>();
        for (var mask = 0; mask < 1 << variables.Count; mask++)
        {
            for (var j = 0; j < variables.Count; j++) assignment[variables[j]] = (mask & (1 << j)) != 0;
            if (a.Evaluate(a.Root, assignment) != b.Evaluate(b.Root, assignment)) return false;
        }

        return true;
    }

    private static bool AreEquivalentSampled(AndInverterGraph a, AndInverterGraph b,
        IReadOnlyList<string> variables, Random rng, int samples)
    {
        var assignment = new Dictionary<string, bool>();
        for (var s = 0; s < samples; s++)
        {
            foreach (var v in variables) assignment[v] = rng.Next(2) == 1;
            if (a.Evaluate(a.Root, assignment) != b.Evaluate(b.Root, assignment)) return false;
        }

        return true;
    }

    private static string RandomExpr(Random rng, string[] variables, int depth)
    {
        if (depth <= 0 || rng.Next(3) == 0) return variables[rng.Next(variables.Length)];

        var op = rng.Next(3);
        if (op == 0) return "!" + RandomExpr(rng, variables, depth - 1);

        var separator = op == 1 ? "&" : "|";
        var count = 2 + rng.Next(2);
        var sb = new StringBuilder("(");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(RandomExpr(rng, variables, depth - 1));
        }

        sb.Append(')');
        return sb.ToString();
    }

    [Fact]
    public void Rewrite_PreservesFunctionAndNeverGrows_OverRandomFourVariableFormulas()
    {
        var rng = new Random(20260728);
        var variables = new[] { "a", "b", "c", "d" };
        var rewritten = 0;

        for (var trial = 0; trial < 1500; trial++)
        {
            var ast = new FormulaFactory().Parse(RandomExpr(rng, variables, 4));
            var graph = AndInverterGraph.FromAst(ast);
            var before = graph.AndNodeCount;

            var result = graph.RewriteToFixpoint();

            Assert.True(result.AndNodeCount <= before,
                $"AND count grew: {before} -> {result.AndNodeCount}");
            Assert.True(AreEquivalent(graph, result, ast.GetVariables().OrderBy(v => v).ToList()),
                "rewrite changed the function");
            if (result.AndNodeCount < before) rewritten++;
        }

        // The corpus is dense enough that the library fires on a healthy fraction.
        Assert.True(rewritten > 100, $"expected many reductions, saw {rewritten}");
    }

    [Fact]
    public void Rewrite_PreservesFunction_OnXorAndEquivalenceHeavyFormulas()
    {
        // XOR/EQV are not in the surface grammar; build them straight through the factory so
        // the rewriter meets 4-input sub-functions where the NPN library genuinely applies.
        var factory = new FormulaFactory();
        AstNode a = factory.Variable("a"), b = factory.Variable("b"),
            c = factory.Variable("c"), d = factory.Variable("d");

        var formulas = new[]
        {
            factory.Xor(factory.Xor(a, b), factory.Xor(c, d)),
            factory.Equivalence(factory.Xor(a, b), factory.Xor(c, d)),
            factory.And(factory.Xor(a, b), factory.Xor(c, d)),
            factory.Or(factory.Equivalence(a, b), factory.Equivalence(c, d)),
            factory.Xor(factory.And(a, b), factory.Or(c, d))
        };

        foreach (var formula in formulas)
        {
            var graph = AndInverterGraph.FromAst(formula);
            var before = graph.AndNodeCount;
            var result = graph.RewriteToFixpoint();

            Assert.True(result.AndNodeCount <= before);
            Assert.True(AreEquivalent(graph, result, formula.GetVariables().OrderBy(v => v).ToList()));
        }
    }

    [Theory]
    // Crafted formulas whose naive AIG translation carries structural redundancy the cut
    // rewriter removes. before/after AND-node counts are asserted to strictly shrink.
    [InlineData("(d&b&!(!a&b))", 3, 2)]
    [InlineData("!(!(d&a&c)|!(b&b&c))", 4, 3)]
    [InlineData("((!(c&d)|!(a&d))&!(c&!a))", 5, 2)]
    [InlineData("(a|(((c&b&a)|!d|(d&c))&((b|a)&(a|c|b)&c))|c)", 12, 1)]
    public void Rewrite_StrictlyReducesAndCount_OnCraftedCases(string expression, int before, int after)
    {
        var ast = new FormulaFactory().Parse(expression);
        var graph = AndInverterGraph.FromAst(ast);
        Assert.Equal(before, graph.AndNodeCount);

        var result = graph.RewriteToFixpoint();

        Assert.Equal(after, result.AndNodeCount);
        Assert.True(after < before, "case must strictly reduce");
        Assert.True(AreEquivalent(graph, result, ast.GetVariables().OrderBy(v => v).ToList()));
    }

    [Fact]
    public void Rewrite_ReachesAFixpoint_SecondRunFindsNothing()
    {
        var ast = new FormulaFactory().Parse("(a|(((c&b&a)|!d|(d&c))&((b|a)&(a|c|b)&c))|c)");
        var graph = AndInverterGraph.FromAst(ast);

        var once = graph.RewriteToFixpoint();
        var twice = once.RewriteToFixpoint();

        Assert.Equal(once.AndNodeCount, twice.AndNodeCount);
    }

    [Fact]
    public void Rewrite_WithZeroRounds_LeavesTheGraphUnchanged()
    {
        var ast = new FormulaFactory().Parse("(a|(((c&b&a)|!d|(d&c))&((b|a)&(a|c|b)&c))|c)");
        var graph = AndInverterGraph.FromAst(ast);

        var result = graph.RewriteToFixpoint(0);

        Assert.Same(graph, result);
        Assert.Equal(graph.AndNodeCount, result.AndNodeCount);
    }

    [Fact]
    public void Rewrite_HonorsCancellation()
    {
        var ast = new FormulaFactory().Parse("(a|(((c&b&a)|!d|(d&c))&((b|a)&(a|c|b)&c))|c)");
        var graph = AndInverterGraph.FromAst(ast);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => graph.RewriteToFixpoint(32, cts.Token));
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void Rewrite_PreservesFunction_OverLargerSampledFormulas()
    {
        var rng = new Random(99);
        var variables = new[] { "a", "b", "c", "d", "e", "f" };

        for (var trial = 0; trial < 3000; trial++)
        {
            var ast = new FormulaFactory().Parse(RandomExpr(rng, variables, 5));
            var graph = AndInverterGraph.FromAst(ast);
            var before = graph.AndNodeCount;
            var result = graph.RewriteToFixpoint();

            Assert.True(result.AndNodeCount <= before);
            Assert.True(AreEquivalentSampled(graph, result,
                ast.GetVariables().OrderBy(v => v).ToList(), rng, 200));
        }
    }
}
