using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Combinatorial testing of <see cref="OptimizationOptions" />: a greedy all-pairs
///     covering array over the 7 binary configuration axes (every pair of settings
///     appears in at least one run), plus the full 128-row grid on a small expression.
///     Interaction bugs between option flags live exactly in those pairs.
/// </summary>
public class PairwiseOptionsTests
{
    private const int AxisCount = 7;

    private static OptimizationOptions FromRow(bool[] row)
    {
        return new OptimizationOptions
        {
            ComputeCnf = row[0],
            ComputeDnf = row[1],
            CnfMode = row[2] ? CnfMode.Tseitin : CnfMode.Equivalent,
            ComputeAdvancedForms = row[3],
            IncludeMetrics = row[4],
            IncludeTruthTables = row[5],
            IncludeDebugInfo = row[6]
        };
    }

    /// <summary>
    ///     Greedy pairwise covering-array construction: keep adding the candidate row
    ///     that covers the most yet-uncovered (axis, axis, value, value) pairs.
    /// </summary>
    internal static List<bool[]> BuildPairwiseRows(int axes)
    {
        var uncovered = new HashSet<(int A, int B, bool Va, bool Vb)>();
        for (var a = 0; a < axes; a++)
            for (var b = a + 1; b < axes; b++)
                foreach (var va in new[] { false, true })
                    foreach (var vb in new[] { false, true })
                        uncovered.Add((a, b, va, vb));

        var rows = new List<bool[]>();
        var rng = new Random(12345);
        while (uncovered.Count > 0)
        {
            bool[]? best = null;
            var bestGain = -1;
            for (var candidate = 0; candidate < 64; candidate++)
            {
                var row = new bool[axes];
                for (var i = 0; i < axes; i++) row[i] = rng.Next(2) == 0;
                var gain = uncovered.Count(p => row[p.A] == p.Va && row[p.B] == p.Vb);
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = row;
                }
            }

            rows.Add(best!);
            uncovered.RemoveWhere(p => best![p.A] == p.Va && best[p.B] == p.Vb);
        }

        return rows;
    }

    [Fact]
    public void PairwiseGenerator_CoversEveryPairOfSettings()
    {
        // Self-check of the covering array: all C(7,2)*4 = 84 pairs present
        var rows = BuildPairwiseRows(AxisCount);
        for (var a = 0; a < AxisCount; a++)
            for (var b = a + 1; b < AxisCount; b++)
                foreach (var va in new[] { false, true })
                    foreach (var vb in new[] { false, true })
                        Assert.True(rows.Any(r => r[a] == va && r[b] == vb),
                            $"Pair (axis{a}={va}, axis{b}={vb}) is not covered");

        // Pairwise must be dramatically smaller than the 128-row full grid
        Assert.True(rows.Count <= 16, $"Covering array degenerated to {rows.Count} rows");
    }

    [Fact]
    public void PairwiseRuns_EveryConfigurationHonorsItsContract()
    {
        var expressions = new[]
        {
            "a & b | a & c",
            "!(a & b) | c & (d | !a)",
            "a & !a"
        };
        var optimizer = new BooleanExpressionOptimizer();

        foreach (var row in BuildPairwiseRows(AxisCount))
            foreach (var expression in expressions)
            {
                var options = FromRow(row);
                var result = optimizer.OptimizeExpression(expression, options);
                var label = $"[{string.Join(",", row)}] on '{expression}'";

                // Core result must always be present and correct, regardless of options
                Assert.True(TruthTable.AreEquivalent(expression, result.Optimized),
                    $"{label}: semantics broken");

                // Every artifact's presence must match exactly what was requested
                Assert.Equal(options.ComputeCnf
                    ? ComputationStatus.Computed
                    : ComputationStatus.NotRequested, result.CnfStatus);
                Assert.Equal(options.ComputeDnf
                    ? ComputationStatus.Computed
                    : ComputationStatus.NotRequested, result.DnfStatus);
                Assert.True(options.ComputeCnf == (result.CNF.Length > 0), $"{label}: CNF presence");
                Assert.True(options.ComputeDnf == (result.DNF.Length > 0), $"{label}: DNF presence");
                Assert.True(!options.IncludeMetrics || result.Metrics != null, $"{label}: metrics missing");
                Assert.True(options.IncludeMetrics || result.Metrics == null, $"{label}: unrequested metrics");
                Assert.True(options.IncludeTruthTables == (result.OriginalTruthTable != null),
                    $"{label}: original truth table presence");
                Assert.True(options.IncludeTruthTables == (result.OptimizedTruthTable != null),
                    $"{label}: optimized truth table presence");
                // Interaction contract: the debug dump is built FROM the metrics object,
                // so it appears only when both flags are on
                var expectDebug = options.IncludeDebugInfo && options.IncludeMetrics;
                Assert.True(expectDebug == (result.DebugInfo.Length > 0), $"{label}: debug info presence");

                if (options.ComputeCnf && options.CnfMode == CnfMode.Equivalent)
                    Assert.True(TruthTable.AreEquivalent(expression, result.CNF), $"{label}: CNF not equivalent");
                if (options.ComputeDnf)
                    Assert.True(TruthTable.AreEquivalent(expression, result.DNF), $"{label}: DNF not equivalent");
            }
    }

    [Fact]
    public void FullGrid_OptimizedResultIsInvariantAcrossAllOptionCombinations()
    {
        // The full 2^7 grid on one expression: options select ARTIFACTS, they must
        // never change the optimized expression itself
        const string expression = "a & b | a & c | b & c | a & !a";
        var optimizer = new BooleanExpressionOptimizer();

        string? reference = null;
        for (var mask = 0; mask < 1 << AxisCount; mask++)
        {
            var row = new bool[AxisCount];
            for (var i = 0; i < AxisCount; i++) row[i] = (mask & (1 << i)) != 0;

            var result = optimizer.OptimizeExpression(expression, FromRow(row));
            reference ??= result.Optimized;
            Assert.True(reference == result.Optimized,
                $"Options mask {mask}: optimized result changed from '{reference}' to '{result.Optimized}'");
            Assert.Equal(MinimizationStatus.MinimalProven, result.MinimizationStatus);
        }
    }
}
