using Xunit;

namespace LogicalOptimizer.Tests;


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
