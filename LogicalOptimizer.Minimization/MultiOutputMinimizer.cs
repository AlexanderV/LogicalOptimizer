namespace LogicalOptimizer;

/// <summary>
///     Multi-output minimization with cube sharing (PLA-style): every output first gets
///     its independent minimal cover, then a shared greedy re-cover tries to reuse product
///     terms across outputs — a term implemented once can feed several outputs. The shared
///     solution is adopted only when it lowers total cost (distinct-cube literals, then
///     distinct cube count); each output stays provably equivalent to its table.
/// </summary>
internal static class MultiOutputMinimizer
{
    /// <remarks>
    ///     <c>canonicalize</c> is an optional display canonicalization (the CLI passes the
    ///     commutativity rule); null keeps raw cube order.
    /// </remarks>
    public static List<(string Name, AstNode Expression)> Minimize(MultiOutputTable table,
        long? pairComparisonLimit, int coverStepLimit, CancellationToken cancellationToken = default,
        Func<AstNode, AstNode>? canonicalize = null)
    {
        var n = table.Variables.Count;
        var totalMinterms = 1 << n;

        // Independent minimal covers (throws InvalidOperationException past the QM budget)
        var covers = new List<List<(int Mask, int Value)>>();
        var onSets = new List<HashSet<int>>();
        var offSets = new List<HashSet<int>>();
        foreach (var output in table.Outputs)
        {
            var on = new HashSet<int>(output.OnSet);
            var dc = new HashSet<int>(output.DontCareSet);
            dc.ExceptWith(on);

            var off = new HashSet<int>();
            for (var m = 0; m < totalMinterms; m++)
                if (!on.Contains(m) && !dc.Contains(m))
                    off.Add(m);

            onSets.Add(on);
            offSets.Add(off);
            covers.Add(on.Count == 0
                ? new List<(int, int)>()
                : TruthTableMinimizer.MinimalCoverCubes(n, on, dc, pairComparisonLimit,
                    cancellationToken, coverStepLimit).Cover);
        }

        var shared = TrySharedCovers(covers, onSets, offSets, n);
        var chosen = shared ?? covers;

        var results = new List<(string, AstNode)>();
        for (var i = 0; i < table.Outputs.Count; i++)
        {
            var sop = TruthTableMinimizer.BuildSopFromCubes(table.Variables, chosen[i]);
            results.Add((table.Outputs[i].Name, canonicalize?.Invoke(sop) ?? sop));
        }

        return results;
    }

    /// <summary>
    ///     Greedy re-cover of every output from the pooled cubes, preferring cubes already
    ///     used elsewhere. Returns null when sharing does not beat the independent covers.
    /// </summary>
    private static List<List<(int Mask, int Value)>>? TrySharedCovers(
        List<List<(int Mask, int Value)>> independentCovers,
        List<HashSet<int>> onSets, List<HashSet<int>> offSets, int variableCount)
    {
        var outputCount = independentCovers.Count;
        if (outputCount < 2) return null;

        var pool = independentCovers.SelectMany(c => c).Distinct().ToList();
        if (pool.Count == 0) return null;

        // usable[c][j]: cube touches no OFF-minterm of output j (safe to feed j)
        var usable = pool.ToDictionary(c => c, c => new bool[outputCount]);
        foreach (var cube in pool)
        {
            var minterms = CubeMinterms(cube, variableCount);
            for (var j = 0; j < outputCount; j++)
                usable[cube][j] = !minterms.Any(offSets[j].Contains);
        }

        var sharedCovers = new List<List<(int Mask, int Value)>>();
        var usedCubes = new HashSet<(int Mask, int Value)>();
        for (var j = 0; j < outputCount; j++)
        {
            var cover = new List<(int Mask, int Value)>();
            var uncovered = new HashSet<int>(onSets[j]);
            var candidates = pool.Where(c => usable[c][j]).ToList();

            while (uncovered.Count > 0)
            {
                var scored = candidates
                    .Select(c => (Cube: c, Gain: CountCovered(c, uncovered)))
                    .Where(x => x.Gain > 0)
                    .OrderByDescending(x => usedCubes.Contains(x.Cube))
                    .ThenByDescending(x => x.Gain)
                    .ThenBy(x => System.Numerics.BitOperations.PopCount((uint)x.Cube.Mask))
                    .ToList();
                if (scored.Count == 0) return null; // cannot cover from the pool: bail out

                var best = scored[0].Cube;
                cover.Add(best);
                usedCubes.Add(best);
                uncovered.RemoveWhere(m => Covers(best, m));
            }

            sharedCovers.Add(cover);
        }

        return Cost(sharedCovers).CompareTo(Cost(independentCovers)) < 0 ? sharedCovers : null;
    }

    /// <summary>(total literals over distinct cubes, distinct cube count) — shared terms count once.</summary>
    private static (int Literals, int Cubes) Cost(List<List<(int Mask, int Value)>> covers)
    {
        var distinct = covers.SelectMany(c => c).Distinct().ToList();
        return (distinct.Sum(c => System.Numerics.BitOperations.PopCount((uint)c.Mask)), distinct.Count);
    }

    private static bool Covers((int Mask, int Value) cube, int minterm)
    {
        return (minterm & cube.Mask) == cube.Value;
    }

    private static int CountCovered((int Mask, int Value) cube, HashSet<int> minterms)
    {
        return minterms.Count(m => Covers(cube, m));
    }

    private static List<int> CubeMinterms((int Mask, int Value) cube, int variableCount)
    {
        var freeBits = new List<int>();
        for (var j = 0; j < variableCount; j++)
            if ((cube.Mask & (1 << j)) == 0)
                freeBits.Add(j);

        var minterms = new List<int>(1 << freeBits.Count);
        for (var combination = 0; combination < 1 << freeBits.Count; combination++)
        {
            var minterm = cube.Value;
            for (var k = 0; k < freeBits.Count; k++)
                if ((combination & (1 << k)) != 0)
                    minterm |= 1 << freeBits[k];
            minterms.Add(minterm);
        }

        return minterms;
    }
}
