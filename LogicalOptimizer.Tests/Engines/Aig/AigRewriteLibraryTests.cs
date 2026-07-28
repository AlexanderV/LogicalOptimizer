using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The rewrite library must implement EXACTLY the requested local function: for every
///     truth table the synthesized <see cref="AigTemplate" />, simulated over all 2^m leaf
///     patterns, equals the target — and replaying it into a real <see cref="AndInverterGraph" />
///     reproduces the same function. Correctness is the hard gate; AND-counts are reported to
///     show the structures stay compact.
/// </summary>
public class AigRewriteLibraryTests
{
    private static void AssertImplements(uint tt, int m)
    {
        var template = AigRewriteLibrary.Synthesize(tt, m);
        Assert.Equal(tt, template.Simulate());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryFunctionUpToThreeInputs_IsImplementedExactly(int m)
    {
        var mask = TruthTableOps.Mask(m);
        for (uint tt = 0; tt <= mask; tt++)
            AssertImplements(tt, m);
    }

    [Fact]
    public void RandomFourInputFunctions_AreImplementedExactly()
    {
        var rng = new Random(1234);
        for (var i = 0; i < 4000; i++)
        {
            var tt = (uint)rng.Next() & 0xFFFF;
            AssertImplements(tt, 4);
        }
    }

    [Fact]
    public void OneCanonicalTemplatePerNpnClass_ImplementsEveryMemberViaTransform()
    {
        // Walk one representative of many NPN classes and also many arbitrary members: every
        // synthesized structure (canonical template + forward-transform relabel) is exact.
        var rng = new Random(77);
        var seen = new HashSet<uint>();
        for (var i = 0; i < 6000; i++)
        {
            var tt = (uint)rng.Next() & 0xFFFF;
            seen.Add(NpnCanonicalizer.Canonicalize(tt, 4).Canonical);
            AssertImplements(tt, 4);
        }

        Assert.True(seen.Count > 100, "expected to exercise many NPN classes");
    }

    [Fact]
    public void Template_ReplayedIntoGraph_ReproducesTheFunction()
    {
        var rng = new Random(31337);
        for (var trial = 0; trial < 400; trial++)
        {
            var m = rng.Next(1, 5);
            var mask = TruthTableOps.Mask(m);
            var tt = (uint)rng.Next() & mask;

            var template = AigRewriteLibrary.Synthesize(tt, m);

            var graph = new AndInverterGraph();
            var leaves = new int[m];
            for (var i = 0; i < m; i++) leaves[i] = graph.CreateInput("y" + i);
            var output = template.BuildInto(graph, leaves);

            // Compare the replayed graph's function to the target truth table.
            var assignment = new Dictionary<string, bool>();
            for (var p = 0; p < 1 << m; p++)
            {
                for (var i = 0; i < m; i++) assignment["y" + i] = (p & (1 << i)) != 0;
                var expected = ((tt >> p) & 1) != 0;
                Assert.Equal(expected, graph.Evaluate(output, assignment));
            }
        }
    }

    [Fact]
    public void CommonGates_AreCompact()
    {
        // Sanity on AND-counts for canonical simple gates.
        Assert.Equal(1, AigRewriteLibrary.Synthesize(0b1000, 2).AndCount); // AND2
        Assert.Equal(1, AigRewriteLibrary.Synthesize(0b1110, 2).AndCount); // OR2
        Assert.True(AigRewriteLibrary.Synthesize(0b0110, 2).AndCount <= 3); // XOR2
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void AllFourInputFunctions_AreImplementedExactly()
    {
        var maxAnd = 0;
        long totalAnd = 0;
        for (uint tt = 0; tt <= 0xFFFF; tt++)
        {
            var template = AigRewriteLibrary.Synthesize(tt, 4);
            Assert.Equal(tt, template.Simulate());
            maxAnd = Math.Max(maxAnd, template.AndCount);
            totalAnd += template.AndCount;
        }

        // Not an assertion on optimality — just guard against pathological blow-up.
        Assert.True(maxAnd <= 20, $"max AND-count {maxAnd} unexpectedly large");
        Assert.True(totalAnd > 0);
    }
}
