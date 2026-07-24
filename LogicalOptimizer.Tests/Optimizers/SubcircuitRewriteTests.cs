using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for the precomputed-optimal-subcircuit rewriting pass: library completeness,
///     soundness against the truth-table oracle, actual improvements, idempotence.
/// </summary>
public class SubcircuitRewriteTests
{
    [Fact]
    public void Library_All256Functions_HaveEquivalentEntries()
    {
        var variables = new List<string> { "x0", "x1", "x2" };
        for (var bits = 0; bits < 256; bits++)
        {
            var terms = new List<string>();
            for (var m = 0; m < 8; m++)
                if ((bits & (1 << m)) != 0)
                    terms.Add(string.Join(" & ", variables.Select((v, j) =>
                        (m & (1 << j)) != 0 ? v : "!" + v)));
            var reference = terms.Count == 0 ? "0" : string.Join(" | ", terms);

            Assert.True(SubcircuitLibrary.TryRewrite(RandomExpressions.Parse(reference), out var minimal));
            Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse(reference), minimal),
                $"Function {bits}: library entry not equivalent");
        }
    }

    [Fact]
    public void RewriteSubcircuits_RandomExpressions_SoundAndNeverWorse()
    {
        var rng = new Random(949);
        for (var trial = 0; trial < 80; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 6, rng.Next(1, 5), allowConstants: true));
            var rewritten = SubcircuitLibrary.RewriteSubcircuits(ast);

            Assert.True(TruthTable.AreEquivalent(ast, rewritten),
                $"Trial {trial}: rewrite changed semantics of '{ast}'");
            Assert.True(AstMetrics.CountLiterals(rewritten) <= AstMetrics.CountLiterals(ast),
                $"Trial {trial}: rewrite worsened '{ast}' to '{rewritten}'");
        }
    }

    [Fact]
    public void RewriteSubcircuits_CollapsesConsensusSubtree()
    {
        // The consensus-heavy 3-var core inside a larger expression: the local pass must
        // collapse it to the known 4-literal minimum without touching the rest
        var ast = RandomExpressions.Parse("(x & y | !x & z | y & z) | p & q");
        var rewritten = SubcircuitLibrary.RewriteSubcircuits(ast);

        Assert.True(TruthTable.AreEquivalent(ast, rewritten));
        // 4 literals for the collapsed core + 2 for p & q
        Assert.Equal(6, AstMetrics.CountLiterals(rewritten));
    }

    [Fact]
    public void RewriteSubcircuits_IsIdempotent()
    {
        var rng = new Random(959);
        for (var trial = 0; trial < 30; trial++)
        {
            var ast = RandomExpressions.Parse(RandomExpressions.Generate(rng, 5, rng.Next(1, 4)));
            var once = SubcircuitLibrary.RewriteSubcircuits(ast);
            var twice = SubcircuitLibrary.RewriteSubcircuits(once);
            Assert.True(once.Equals(twice),
                $"Trial {trial}: second pass changed '{once}' to '{twice}'");
        }
    }

    [Fact]
    public void RewriteSubcircuits_LeavesExtendedOperatorsIntact()
    {
        var ast = new OrNode(
            new XorNode(new VariableNode("a"), new VariableNode("b")),
            RandomExpressions.Parse("c & d | c & !d"));
        var rewritten = SubcircuitLibrary.RewriteSubcircuits(ast);

        // XOR survives; the c-subtree collapses to just c
        Assert.Contains("XOR", rewritten.ToString());
        Assert.True(TruthTable.AreEquivalent(ast, rewritten));
        Assert.Equal(3, AstMetrics.CountLiterals(rewritten));
    }
}
