using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Structural bookkeeping for DAG-aware rewriting: per-node reference counting, the
///     deref/ref primitives, and maximum fanout-free cone (MFFC) computation. The
///     reference-count convention is: each node's count is the number of fanin edges that
///     point at it — one per AND node that uses it as a child, plus one for the Root
///     primary output — with complement bits ignored (counts are per node, not per literal).
/// </summary>
public class AigReferenceCountingTests
{
    /// <summary>
    ///     Independently recompute every node's reference count straight from the fanin
    ///     structure (AND-node children plus the Root PO), without consulting the graph's
    ///     own counters, and compare.
    /// </summary>
    private static int[] RecountFromStructure(AndInverterGraph graph)
    {
        var nodes = graph.SnapshotNodes();
        var expected = new int[nodes.Length];
        for (var i = 1; i < nodes.Length; i++)
        {
            var (left, right) = nodes[i];
            if (left < 0) continue; // input or constant: no fanins
            expected[left >> 1]++;
            expected[right >> 1]++;
        }

        expected[graph.Root >> 1]++; // Root primary-output reference
        return expected;
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("(a & b) | (a & b & c)")] // shares the (a & b) subterm
    [InlineData("(a & b) | (a & c) | (b & c)")]
    [InlineData("(a & b & c) | (a & b & d)")]
    [InlineData("(a | b) & (a | c) & (b | c)")]
    [InlineData("!(a & b) | (c & (a & b))")]
    public void RefCounts_MatchIndependentStructuralRecount(string expression)
    {
        var graph = AndInverterGraph.FromAst(RandomExpressions.Parse(expression));
        Assert.Equal(RecountFromStructure(graph), graph.SnapshotRefCounts());
    }

    [Fact]
    public void RefCounts_MatchRecount_OnRandomCorpus()
    {
        var rng = new Random(1471);
        for (var trial = 0; trial < 80; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 5, rng.Next(1, 5), allowConstants: true));
            var graph = AndInverterGraph.FromAst(ast);
            Assert.Equal(RecountFromStructure(graph), graph.SnapshotRefCounts());
        }
    }

    [Fact]
    public void SharedSubexpression_HasReferenceCountGreaterThanOne()
    {
        // Structural hashing makes (a & b) a single node used by both the left term and the
        // (a & b & c) term, so its reference count is 2.
        var graph = new AndInverterGraph();
        var a = graph.CreateInput("a");
        var b = graph.CreateInput("b");
        var c = graph.CreateInput("c");
        var ab = graph.And(a, b);
        var abc = graph.And(ab, c);
        graph.Or(ab, abc); // second consumer of ab (via the OR's internal AND)

        Assert.Equal(2, graph.RefCount(ab));
        Assert.Equal(1, graph.RefCount(abc));
    }

    [Fact]
    public void DereferenceThenReference_RestoresAllCountsExactly()
    {
        var rng = new Random(2027);
        for (var trial = 0; trial < 40; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 5, rng.Next(2, 5), allowConstants: true));
            var graph = AndInverterGraph.FromAst(ast);

            var before = graph.SnapshotRefCounts();
            var freedByDeref = graph.Dereference(graph.Root);
            var freedByRef = graph.Reference(graph.Root);
            var after = graph.SnapshotRefCounts();

            Assert.Equal(before, after);
            Assert.Equal(freedByDeref, freedByRef);
        }
    }

    [Fact]
    public void MffcSize_DoesNotDisturbReferenceCounts()
    {
        var graph = AndInverterGraph.FromAst(
            RandomExpressions.Parse("(a & b) | (a & b & c) | (c & d)"));
        var before = graph.SnapshotRefCounts();

        // Query the MFFC of every AND node; none of these queries may leak count changes.
        var nodes = graph.SnapshotNodes();
        for (var i = 1; i < nodes.Length; i++)
            if (nodes[i].Left >= 0)
            {
                graph.MffcSize(i << 1);
                graph.ComputeMffc(i << 1);
            }

        Assert.Equal(before, graph.SnapshotRefCounts());
    }

    [Fact]
    public void Mffc_ExcludesSharedFanin_ButIncludesPrivateFanin()
    {
        // Graph 1: (a & b) shared. Build ab, abc = ab & c, and an OR that also consumes ab.
        // The MFFC of abc must NOT contain ab, because ab still fans out to the OR node.
        var shared = new AndInverterGraph();
        var a = shared.CreateInput("a");
        var b = shared.CreateInput("b");
        var c = shared.CreateInput("c");
        var ab = shared.And(a, b);
        var abc = shared.And(ab, c);
        shared.Or(ab, abc); // ab now has fanout 2

        Assert.Equal(2, shared.RefCount(ab));
        var sharedCone = shared.ComputeMffc(abc);
        Assert.Equal(new[] { abc >> 1 }, sharedCone); // only abc itself
        Assert.Equal(1, shared.MffcSize(abc));
        Assert.DoesNotContain(ab >> 1, sharedCone);

        // Graph 2: private cone. abc = (a & b) & c with no other consumer of (a & b).
        // Removing abc frees ab too, so the MFFC is {abc, ab}, size 2.
        var priv = new AndInverterGraph();
        var pa = priv.CreateInput("a");
        var pb = priv.CreateInput("b");
        var pc = priv.CreateInput("c");
        var pab = priv.And(pa, pb);
        var pabc = priv.And(pab, pc);

        Assert.Equal(1, priv.RefCount(pab));
        Assert.Equal(2, priv.MffcSize(pabc));
        var privateCone = new HashSet<int>(priv.ComputeMffc(pabc));
        Assert.Contains(pabc >> 1, privateCone);
        Assert.Contains(pab >> 1, privateCone); // private fanin IS in the cone
        Assert.Equal(2, privateCone.Count);
    }

    [Fact]
    public void MffcSize_OfWholeFunctionRoot_EqualsAllAndNodes()
    {
        // For a graph whose Root has fanout only to the PO, removing the Root frees every
        // reachable AND node, so the MFFC size equals AndNodeCount.
        var graph = AndInverterGraph.FromAst(
            RandomExpressions.Parse("(a & b & c) | (a & !b)"));
        Assert.Equal(graph.AndNodeCount, graph.MffcSize(graph.Root));
    }

    [Fact]
    public void MffcSize_OfInputOrConstant_IsZero()
    {
        var graph = new AndInverterGraph();
        var a = graph.CreateInput("a");
        Assert.Equal(0, graph.MffcSize(a));
        Assert.Empty(graph.ComputeMffc(a));
        Assert.Equal(0, graph.MffcSize(AndInverterGraph.FalseLiteral));
        Assert.Equal(0, graph.MffcSize(AndInverterGraph.TrueLiteral));
    }
}
