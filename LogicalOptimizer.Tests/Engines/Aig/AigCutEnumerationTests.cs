using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     k-feasible cut enumeration and local cut truth-table extraction. Cuts are the
///     frontiers a DAG-aware rewriter reasons over: the invariants below (feasibility, the
///     trivial cut, minimality, leaves that actually feed the node) and the exactness of the
///     extracted 2^m truth table against the brute-force <see cref="AndInverterGraph.Evaluate" />
///     are the correctness gates for the whole rewrite pipeline.
/// </summary>
public class AigCutEnumerationTests
{
    private static int[][] Normalize(IEnumerable<Cut> cuts)
    {
        return cuts.Select(c => c.Leaves).OrderBy(a => a.Length)
            .ThenBy(a => string.Join(",", a)).ToArray();
    }

    [Fact]
    public void HandCheckedChain_HasExactCutSet()
    {
        // Nodes: 0 const, 1=a, 2=b, 3=c, 4=(a&b), 5=(a&b)&c.
        var graph = new AndInverterGraph();
        var a = graph.CreateInput("a");
        var b = graph.CreateInput("b");
        var c = graph.CreateInput("c");
        var ab = graph.And(a, b);
        var abc = graph.And(ab, c);

        var cuts4 = Normalize(new AigCutEnumerator(graph).Enumerate(ab >> 1));
        Assert.Equal(new[] { new[] { 4 }, new[] { 1, 2 } }.OrderBy(x => x.Length)
            .ThenBy(x => string.Join(",", x)).ToArray(), cuts4);

        var cuts5 = Normalize(new AigCutEnumerator(graph).Enumerate(abc >> 1));
        // {5} trivial, {3,4} = (a&b) with c, {1,2,3} = a,b,c.
        Assert.Equal(new[] { new[] { 5 }, new[] { 3, 4 }, new[] { 1, 2, 3 } }
            .OrderBy(x => x.Length).ThenBy(x => string.Join(",", x)).ToArray(), cuts5);
    }

    [Fact]
    public void EveryCut_IsFeasible_ContainsTrivial_AndIsMinimal()
    {
        var rng = new Random(4241);
        for (var trial = 0; trial < 60; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 5, rng.Next(2, 5), allowConstants: true));
            var graph = AndInverterGraph.FromAst(ast);
            var nodes = graph.SnapshotNodes();
            var allCuts = new AigCutEnumerator(graph).EnumerateAll(4);

            foreach (var (node, cuts) in allCuts)
            {
                // Trivial cut present.
                Assert.Contains(cuts, c => c.Leaves.Length == 1 && c.Leaves[0] == node);

                foreach (var cut in cuts)
                {
                    Assert.True(cut.Leaves.Length <= 4, "cut exceeds k");
                    // Sorted & distinct.
                    for (var i = 1; i < cut.Leaves.Length; i++)
                        Assert.True(cut.Leaves[i - 1] < cut.Leaves[i]);
                    // Leaves are real nodes below the root (or the node itself for trivial).
                    foreach (var leaf in cut.Leaves)
                        Assert.True(leaf >= 0 && leaf < nodes.Length && leaf <= node);
                }

                // Minimality: no kept cut is a strict subset of another kept cut.
                for (var i = 0; i < cuts.Count; i++)
                    for (var j = 0; j < cuts.Count; j++)
                        if (i != j)
                            Assert.False(cuts[i].IsSubsetOf(cuts[j]),
                                $"cut {i} dominates cut {j} on node {node}");
            }
        }
    }

    [Fact]
    public void CutLeaves_ActuallyFeedTheNode()
    {
        // Every cut must be a genuine cutset: expanding the cone from the node while stopping
        // at leaves must reach exactly the leaves (no primary input escapes below a leaf).
        var rng = new Random(9001);
        for (var trial = 0; trial < 40; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 5, rng.Next(2, 5)));
            var graph = AndInverterGraph.FromAst(ast);
            var nodes = graph.SnapshotNodes();
            var allCuts = new AigCutEnumerator(graph).EnumerateAll(4);

            foreach (var (node, cuts) in allCuts)
                foreach (var cut in cuts)
                {
                    var leafSet = new HashSet<int>(cut.Leaves);
                    var reachedLeaves = new HashSet<int>();
                    Walk(node, nodes, leafSet, reachedLeaves, new HashSet<int>());
                    // Every leaf is reached, and the cone bottoms out only at leaves.
                    Assert.Equal(leafSet.OrderBy(x => x), reachedLeaves.OrderBy(x => x));
                }
        }
    }

    private static void Walk(int node, (int Left, int Right)[] nodes, HashSet<int> leaves,
        HashSet<int> reached, HashSet<int> visited)
    {
        if (leaves.Contains(node))
        {
            reached.Add(node);
            return;
        }

        Assert.True(node != 0 && nodes[node].Left >= 0,
            "cone reached a non-leaf input/constant — not a valid cutset");
        if (!visited.Add(node)) return;
        Walk(nodes[node].Left >> 1, nodes, leaves, reached, visited);
        Walk(nodes[node].Right >> 1, nodes, leaves, reached, visited);
    }

    [Fact]
    public void CutTruthTable_MatchesEvaluate_OnRandomGraphs()
    {
        var rng = new Random(1717);
        var checkedCount = 0;
        for (var trial = 0; trial < 120; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 5, rng.Next(2, 5), allowConstants: true));
            var graph = AndInverterGraph.FromAst(ast);
            var variables = graph.Inputs.ToList();
            var n = variables.Count;
            if (n == 0 || n > 8) continue;

            var enumerator = new AigCutEnumerator(graph);
            var allCuts = enumerator.EnumerateAll(4);

            foreach (var (node, cuts) in allCuts)
            {
                if (graph.SnapshotNodes()[node].Left < 0) continue; // skip inputs
                foreach (var cut in cuts)
                {
                    var tt = enumerator.CutTruthTable(node, cut);
                    var m = cut.Leaves.Length;
                    var assignment = new Dictionary<string, bool>();
                    for (var mask = 0; mask < 1 << n; mask++)
                    {
                        for (var j = 0; j < n; j++)
                            assignment[variables[j]] = (mask & (1 << j)) != 0;

                        // Pattern index into the cut truth table from the leaves' values.
                        var pattern = 0;
                        for (var i = 0; i < m; i++)
                            if (graph.Evaluate(cut.Leaves[i] << 1, assignment))
                                pattern |= 1 << i;

                        var expected = graph.Evaluate(node << 1, assignment);
                        Assert.True(((tt >> pattern) & 1) == (expected ? 1u : 0u),
                            $"trial {trial}: cut tt mismatch, node {node}, mask {mask}");
                        checkedCount++;
                    }
                }
            }
        }

        Assert.True(checkedCount > 0, "no cut truth tables were checked");
    }
}
