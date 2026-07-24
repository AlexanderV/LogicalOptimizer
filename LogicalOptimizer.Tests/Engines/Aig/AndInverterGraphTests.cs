using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     AIG engine tests: semantics against the brute-force oracle, structural hashing,
///     one-level simplification rules, OR-recovery on the way back to an AST.
/// </summary>
public class AndInverterGraphTests
{
    [Fact]
    public void FromAst_RandomExpressions_MatchesTruthTableOracle()
    {
        var rng = new Random(919);
        for (var trial = 0; trial < 60; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, rng.Next(1, 5), allowConstants: true);
            var ast = RandomExpressions.Parse(expression);
            var graph = AndInverterGraph.FromAst(ast);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                Assert.True(graph.Evaluate(graph.Root, assignment) == TruthTable.Evaluate(ast, assignment),
                    $"Trial {trial}: AIG differs at mask {m} for '{expression}'");
            }
        }
    }

    [Fact]
    public void ToAst_RoundTrip_IsEquivalent()
    {
        var rng = new Random(929);
        for (var trial = 0; trial < 60; trial++)
        {
            var ast = RandomExpressions.Parse(RandomExpressions.Generate(rng, 5, rng.Next(1, 5)));
            var graph = AndInverterGraph.FromAst(ast);
            Assert.True(TruthTable.AreEquivalent(ast, graph.ToAst(graph.Root)),
                $"Trial {trial}: round trip changed semantics of '{ast}'");
        }
    }

    [Fact]
    public void StructuralHashing_SharesIdenticalSubtrees()
    {
        // "a & b | a & b": one AND for the shared term, one AND for the OR = 2 nodes...
        // actually Or(x,x) = x by the idempotence rule, so only ONE node remains
        var graph = AndInverterGraph.FromAst(RandomExpressions.Parse("a & b | a & b"));
        Assert.Equal(1, graph.AndNodeCount);

        // "(a & b) | (a & b) & c": shared core (a&b) counts once: core + (core&c) + OR = 3
        var shared = AndInverterGraph.FromAst(RandomExpressions.Parse("(a & b) | (a & b) & c"));
        Assert.Equal(3, shared.AndNodeCount);
    }

    [Fact]
    public void SimplificationRules_FoldConstantsAndComplements()
    {
        var graph = new AndInverterGraph();
        var a = graph.CreateInput("a");

        Assert.Equal(AndInverterGraph.FalseLiteral, graph.And(a, AndInverterGraph.Not(a)));
        Assert.Equal(AndInverterGraph.TrueLiteral, graph.Or(a, AndInverterGraph.Not(a)));
        Assert.Equal(a, graph.And(a, a));
        Assert.Equal(a, graph.And(a, AndInverterGraph.TrueLiteral));
        Assert.Equal(AndInverterGraph.FalseLiteral, graph.And(a, AndInverterGraph.FalseLiteral));
        Assert.Equal(a, AndInverterGraph.Not(AndInverterGraph.Not(a)));
        Assert.Equal(0, graph.AndNodeCount);
    }

    [Fact]
    public void ExtendedOperators_TranslateCorrectly()
    {
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var nodes = new (AstNode Ast, Func<bool, bool, bool> Expected)[]
        {
            (new XorNode(a, b), (x, y) => x ^ y),
            (new EqvNode(a, b), (x, y) => x == y),
            (new ImpNode(a, b), (x, y) => !x || y),
            (new NandNode(a, b), (x, y) => !(x && y)),
            (new NorNode(a, b), (x, y) => !(x || y))
        };

        foreach (var (ast, expected) in nodes)
        {
            var graph = AndInverterGraph.FromAst(ast);
            foreach (var x in new[] { false, true })
                foreach (var y in new[] { false, true })
                    Assert.True(graph.Evaluate(graph.Root,
                            new Dictionary<string, bool> { ["a"] = x, ["b"] = y }) == expected(x, y),
                        $"{ast.GetType().Name} wrong at ({x},{y})");
        }
    }

    [Fact]
    public void ToAst_RecoversOrInsteadOfNandChains()
    {
        var graph = AndInverterGraph.FromAst(RandomExpressions.Parse("a | b"));
        Assert.Equal("a | b", graph.ToAst(graph.Root).ToString());
    }

    [Fact]
    public void Cleanup_NeverGrowsAndPreservesSemantics()
    {
        var rng = new Random(939);
        for (var trial = 0; trial < 40; trial++)
        {
            var ast = RandomExpressions.Parse(RandomExpressions.Generate(rng, 4, rng.Next(1, 5), allowConstants: true));
            var graph = AndInverterGraph.FromAst(ast);
            var cleaned = graph.Cleanup();

            Assert.True(cleaned.AndNodeCount <= graph.AndNodeCount,
                $"Trial {trial}: cleanup grew the graph for '{ast}'");
            Assert.True(TruthTable.AreEquivalent(ast, cleaned.ToAst(cleaned.Root)),
                $"Trial {trial}: cleanup changed semantics of '{ast}'");
        }
    }
}
