using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The AIG rewrite library is now backed by a baked table of <b>provably minimum-AND</b>
///     templates, one per NPN class over 1..4 inputs. These tests are the correctness and
///     minimality gates: every baked recipe must simulate exactly to its canonical class, the
///     table must cover every class exactly once, and (Exhaustive) every class's AND-count must
///     equal the exhaustive-search minimum and never exceed the previous constructive library.
/// </summary>
public class AigMinLibraryTests
{
    private static AigTemplate FromRecipe(int m, int[] recipe)
    {
        var gateCount = recipe[2];
        var gates = new (int A, int B)[gateCount];
        for (var g = 0; g < gateCount; g++) gates[g] = (recipe[3 + 2 * g], recipe[4 + 2 * g]);
        return new AigTemplate(m, gates, recipe[1]);
    }

    private static uint Parity(int m)
    {
        uint tt = 0;
        for (var p = 0; p < 1 << m; p++)
            if (System.Numerics.BitOperations.PopCount((uint)p) % 2 == 1)
                tt |= 1u << p;
        return tt;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryBakedRecipe_SimulatesToItsCanonicalClass(int m)
    {
        foreach (var row in AigMinLibraryData.For(m))
        {
            var template = FromRecipe(m, row);
            Assert.Equal((uint)row[0], template.Simulate());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BakedTable_CoversEveryNpnClassExactlyOnce(int m)
    {
        var keys = AigMinLibraryData.For(m).Select(r => (uint)r[0]).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count()); // no duplicate class

        var classes = new HashSet<uint>();
        for (uint tt = 0; tt <= TruthTableOps.Mask(m); tt++)
            classes.Add(NpnCanonicalizer.Canonicalize(tt, m).Canonical);
        Assert.True(classes.SetEquals(keys), $"baked table must key exactly the {classes.Count} NPN classes");
    }

    [Fact]
    public void WellKnownGates_HaveTheirMinimumAndCounts()
    {
        Assert.Equal(1, AigRewriteLibrary.Synthesize(0b1000, 2).AndCount); // AND2
        Assert.Equal(1, AigRewriteLibrary.Synthesize(0b1110, 2).AndCount); // OR2
        Assert.Equal(3, AigRewriteLibrary.Synthesize(0b0110, 2).AndCount); // XOR2 (minimum is 3)

        // maj3 = 4, mux3 = 3, xor3 = 6 — classic minimum AIG sizes.
        uint maj = 0, mux = 0, xor3 = 0;
        for (var p = 0; p < 8; p++)
        {
            int x0 = p & 1, x1 = (p >> 1) & 1, x2 = (p >> 2) & 1;
            if (x0 + x1 + x2 >= 2) maj |= 1u << p;
            if ((x2 == 1 ? x1 : x0) == 1) mux |= 1u << p;
            if ((x0 ^ x1 ^ x2) == 1) xor3 |= 1u << p;
        }

        Assert.Equal(4, AigRewriteLibrary.Synthesize(maj, 3).AndCount);
        Assert.Equal(3, AigRewriteLibrary.Synthesize(mux, 3).AndCount);
        Assert.Equal(6, AigRewriteLibrary.Synthesize(xor3, 3).AndCount);
    }

    [Fact]
    public void FourInputParity_NeedsNineAndNodes_AndTheHardestClassesNeedTen()
    {
        // 4-input parity is a hard function (its minimum AIG has 9 AND nodes), but not the
        // costliest: the hardest four-input NPN classes need 10 AND nodes, which is the maximum.
        Assert.Equal(9, AigRewriteLibrary.Synthesize(Parity(4), 4).AndCount);

        var max = 0;
        foreach (var row in AigMinLibraryData.For(4)) max = Math.Max(max, row[2]);
        Assert.Equal(10, max);
    }

    [Fact]
    public void SampledFourInputFunctions_SynthesizeExactlyAndMatchTheirClassCount()
    {
        var rng = new Random(4242);
        for (var i = 0; i < 3000; i++)
        {
            var tt = (uint)rng.Next() & 0xFFFF;
            var template = AigRewriteLibrary.Synthesize(tt, 4);
            Assert.Equal(tt, template.Simulate());

            // A member's template has the same AND-count as its class representative.
            var canonical = NpnCanonicalizer.Canonicalize(tt, 4).Canonical;
            var classTemplate = AigRewriteLibrary.Synthesize(canonical, 4);
            Assert.Equal(classTemplate.AndCount, template.AndCount);
        }
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void EveryClass_IsProvablyMinimum_AndNeverWorseThanTheConstructiveLibrary()
    {
        for (var m = 1; m <= 4; m++)
        {
            var (classMin, _, unfoundMin) = AigMinLibraryGenerator.CertifiedMinimums(m);
            var histogram = new SortedDictionary<int, int>();

            foreach (var row in AigMinLibraryData.For(m))
            {
                var canonical = (uint)row[0];
                var template = FromRecipe(m, row);
                var count = template.AndCount;

                // (a) realizes the class exactly.
                Assert.Equal(canonical, template.Simulate());

                // (b) minimal. The complete BFS certifies the exact minimum for every class up to
                // its built depth plus the two record passes (min ≤ 9 over four inputs); those must
                // match exactly. The remaining classes are certified to need at least `unfoundMin`
                // AND nodes, and the recipe realizes the class in exactly that many (checked via the
                // histogram), so the minimum is pinned: unfound ⇒ min = unfoundMin.
                var certified = classMin.TryGetValue(canonical, out var k) ? k : unfoundMin;
                Assert.Equal(certified, count);

                // (c) never worse than the previous constructive Shannon/ITE library.
                Assert.True(count <= ConstructiveReferenceCount(canonical, m),
                    $"m={m} class 0x{canonical:X}: min {count} exceeds constructive reference");

                histogram[count] = histogram.GetValueOrDefault(count) + 1;
            }

            // The exhaustive minimum-AND histogram over all NPN classes. Pins the whole
            // distribution, so any non-minimal recipe shifts a bucket and fails here.
            if (m == 4)
            {
                var expected = new SortedDictionary<int, int>
                {
                    [0] = 2,
                    [1] = 1,
                    [2] = 2,
                    [3] = 7,
                    [4] = 9,
                    [5] = 24,
                    [6] = 30,
                    [7] = 61,
                    [8] = 45,
                    [9] = 37,
                    [10] = 4
                };
                Assert.Equal(expected, histogram);
            }
        }
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void FreshSatGeneration_ForSmallInputs_MatchesTheBakedTable()
    {
        // Exercises the SAT-based exact-synthesis generator end to end (fast for m ≤ 3, whose
        // hardest class needs only 6 AND nodes) and confirms the baked recipes agree with a
        // freshly synthesized minimum. The full 4-input regeneration is env-var gated below
        // because a handful of 4-input witness instances are slow to solve.
        for (var m = 1; m <= 3; m++)
        {
            var fresh = AigMinLibraryGenerator.Generate(m);
            var baked = AigMinLibraryData.For(m).ToDictionary(r => (uint)r[0], r => FromRecipe(m, r));

            Assert.Equal(fresh.Count, baked.Count);
            foreach (var (tt, template) in fresh)
            {
                Assert.True(baked.TryGetValue(tt, out var bakedTemplate), $"missing baked class 0x{tt:X}");
                // Same (minimum) AND-count and same realized function; the exact gate wiring may
                // differ between equally minimal solutions, so compare count + simulation.
                Assert.Equal(template.AndCount, bakedTemplate.AndCount);
                Assert.Equal(tt, bakedTemplate.Simulate());
            }
        }
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void RegenerateBakedData_WhenRequested()
    {
        // Opt-in: rewrite LogicalOptimizer.Core/AigMinLibraryData.cs from scratch. Slow (SAT-based
        // exact synthesis for all 222 four-input classes). Run with
        // LOGICALOPTIMIZER_REGENERATE_AIGMIN=1 to refresh the baked table deterministically.
        if (Environment.GetEnvironmentVariable("LOGICALOPTIMIZER_REGENERATE_AIGMIN") != "1") return;

        var source = AigMinLibraryGenerator.EmitDataSource();
        var path = Path.Combine(RepositoryRoot(), "LogicalOptimizer.Core", "AigMinLibraryData.cs");
        File.WriteAllText(path, source);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LogicalOptimizer.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("solution root not found");
    }

    // ------------------------------------------------------------------------------------------
    // Reference: the previous constructive Shannon/ITE synthesis, kept here only so the tests can
    // assert the baked minimum table is never worse than what shipped before.
    // ------------------------------------------------------------------------------------------
    private static int ConstructiveReferenceCount(uint canonicalTt, int m)
    {
        var graph = new AndInverterGraph();
        var leaves = new int[m];
        for (var i = 0; i < m; i++) leaves[i] = graph.CreateInput("x" + i);
        var baseAnd = graph.AndNodeCount;
        var memo = new Dictionary<uint, int>();
        Shannon(graph, canonicalTt, m, leaves, memo);
        return graph.AndNodeCount - baseAnd;
    }

    private static int Shannon(AndInverterGraph graph, uint tt, int m, int[] leaves, Dictionary<uint, int> memo)
    {
        var mask = TruthTableOps.Mask(m);
        tt &= mask;
        if (tt == 0) return AndInverterGraph.FalseLiteral;
        if (tt == mask) return AndInverterGraph.TrueLiteral;
        if (memo.TryGetValue(tt, out var cached)) return cached;

        var split = LowestSupport(tt, m);
        var (c0, c1) = Cofactors(tt, split, m);
        var low = Shannon(graph, c0, m, leaves, memo);
        var high = Shannon(graph, c1, m, leaves, memo);
        var result = SmartIte(graph, leaves[split], high, low);
        memo[tt] = result;
        return result;
    }

    private static int SmartIte(AndInverterGraph graph, int condition, int thenLit, int elseLit)
    {
        if (thenLit == AndInverterGraph.TrueLiteral && elseLit == AndInverterGraph.FalseLiteral) return condition;
        if (thenLit == AndInverterGraph.FalseLiteral && elseLit == AndInverterGraph.TrueLiteral)
            return AndInverterGraph.Not(condition);
        if (thenLit == AndInverterGraph.TrueLiteral) return graph.Or(condition, elseLit);
        if (elseLit == AndInverterGraph.FalseLiteral) return graph.And(condition, thenLit);
        if (thenLit == AndInverterGraph.FalseLiteral) return graph.And(AndInverterGraph.Not(condition), elseLit);
        if (elseLit == AndInverterGraph.TrueLiteral) return graph.Or(AndInverterGraph.Not(condition), thenLit);
        return graph.Ite(condition, thenLit, elseLit);
    }

    private static int LowestSupport(uint tt, int m)
    {
        for (var i = 0; i < m; i++)
        {
            var (c0, c1) = Cofactors(tt, i, m);
            if (c0 != c1) return i;
        }

        return 0;
    }

    private static (uint C0, uint C1) Cofactors(uint tt, int var, int m)
    {
        var size = 1 << m;
        uint c0 = 0, c1 = 0;
        for (var p = 0; p < size; p++)
        {
            var withoutBit = p & ~(1 << var);
            if (((tt >> withoutBit) & 1) != 0) c0 |= 1u << p;
            var withBit = p | (1 << var);
            if (((tt >> withBit) & 1) != 0) c1 |= 1u << p;
        }

        return (c0, c1);
    }
}
