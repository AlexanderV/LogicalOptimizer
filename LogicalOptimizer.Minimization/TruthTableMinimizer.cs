namespace LogicalOptimizer;

/// <summary>
///     Exact two-level minimization: Quine–McCluskey prime implicant generation followed
///     by an exact minimum cover search (essential primes + branch-and-bound, greedy
///     fallback past a work limit). Cost order: total literals, then term count.
///     Minterm bit convention: bit j of a minterm index is the value of variables[j]
///     (variables are expected in sorted order).
/// </summary>
public static class TruthTableMinimizer
{
    private const int MaxColumnDominanceCandidates = 400;

    /// <summary>Thrown message marker when a work limit interrupts exact minimization.</summary>
    internal const string WorkLimitMessage = "Exact minimization work limit exceeded";

    /// <summary>An implicant: variables with a set Mask bit are fixed to the corresponding Value bit.</summary>
    private readonly record struct Implicant(int Mask, int Value)
    {
        public bool Covers(int minterm) => (minterm & Mask) == Value;
        public int LiteralCount => System.Numerics.BitOperations.PopCount((uint)Mask);
    }

    /// <summary>
    ///     Minimal sum-of-products for the function given by its ON-set (and optional
    ///     don't-care set) over 2^variables.Count minterms.
    /// </summary>
    /// <param name="variables">Sorted variable names; bit j of a minterm is variables[j].</param>
    /// <param name="onSet">Minterms where the function is 1.</param>
    /// <param name="dontCareSet">Minterms whose value is unspecified.</param>
    /// <param name="pairComparisonLimit">
    ///     Optional work budget for prime-implicant generation; when exceeded a
    ///     <see cref="ComputationBudgetExceededException" /> is thrown so callers can fall
    ///     back to heuristic simplification (dense functions near 12 variables can otherwise
    ///     cost seconds).
    /// </param>
    /// <param name="cancellationToken">
    ///     Cooperative cancellation, observed throughout: prime generation (per level and
    ///     within dense pairing loops), covering-table reduction (per row/column pass) and
    ///     the branch-and-bound cover search (periodically per node).
    /// </param>
    public static AstNode MinimalSop(IReadOnlyList<string> variables,
        IReadOnlyCollection<int> onSet, IReadOnlyCollection<int>? dontCareSet = null,
        long? pairComparisonLimit = null, CancellationToken cancellationToken = default)
    {
        return MinimalSopWithStatus(variables, onSet, dontCareSet, pairComparisonLimit, cancellationToken)
            .Expression;
    }

    /// <summary>
    ///     Like <see cref="MinimalSop" /> but also reports whether the minimum cover search
    ///     completed: when ProvenMinimal is true the returned SOP is provably minimal in
    ///     (total literals, then term count); when false the cover is sound but the exact
    ///     search hit <paramref name="coverStepLimit" /> before proving optimality.
    /// </summary>
    public static (AstNode Expression, bool ProvenMinimal) MinimalSopWithStatus(IReadOnlyList<string> variables,
        IReadOnlyCollection<int> onSet, IReadOnlyCollection<int>? dontCareSet = null,
        long? pairComparisonLimit = null, CancellationToken cancellationToken = default,
        int coverStepLimit = ResourceBudget.DefaultCoverStepLimit)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (variables.Count > TruthTable.MaxVariables)
            throw new ArgumentException($"Exact minimization supports at most {TruthTable.MaxVariables} variables");

        var on = new HashSet<int>(onSet);
        var dc = dontCareSet == null ? new HashSet<int>() : new HashSet<int>(dontCareSet);
        dc.ExceptWith(on);

        if (on.Count == 0) return (ConstantNode.False, true);

        var (cubes, proven) = MinimalCoverCubes(variables.Count, on, dc, pairComparisonLimit,
            cancellationToken, coverStepLimit);
        return (BuildSopFromCubes(variables, cubes), proven);
    }

    /// <summary>
    ///     Minimum cover as raw cubes (Mask/Value pairs) — the building block for
    ///     multi-output minimization, where cubes are shared across outputs.
    /// </summary>
    internal static (List<(int Mask, int Value)> Cover, bool Proven) MinimalCoverCubes(int variableCount,
        HashSet<int> onSet, HashSet<int> dontCareSet, long? pairComparisonLimit,
        CancellationToken cancellationToken, int coverStepLimit)
    {
        var care = new HashSet<int>(onSet);
        care.UnionWith(dontCareSet);

        var primes = GeneratePrimeImplicants(variableCount, care, pairComparisonLimit, cancellationToken);
        var (cover, proven) = SelectMinimumCover(primes, onSet.ToList(), coverStepLimit, cancellationToken);
        return (cover.Select(c => (c.Mask, c.Value)).ToList(), proven);
    }

    /// <summary>SOP from raw cubes; a cube with no fixed variables makes the constant 1.</summary>
    internal static AstNode BuildSopFromCubes(IReadOnlyList<string> variables,
        List<(int Mask, int Value)> cubes)
    {
        if (cubes.Count == 0) return ConstantNode.False;
        return BuildSop(variables, cubes.Select(c => new Implicant(c.Mask, c.Value)).ToList());
    }

    /// <summary>
    ///     Minimal product-of-sums: the complement's minimal SOP negated via De Morgan.
    /// </summary>
    public static AstNode MinimalPos(IReadOnlyList<string> variables,
        IReadOnlyCollection<int> onSet, IReadOnlyCollection<int>? dontCareSet = null,
        long? pairComparisonLimit = null, CancellationToken cancellationToken = default)
    {
        return MinimalPosWithStatus(variables, onSet, dontCareSet, pairComparisonLimit, cancellationToken)
            .Expression;
    }

    /// <summary>
    ///     Like <see cref="MinimalPos" /> but also reports whether the minimum cover search of
    ///     the complement completed: when ProvenMinimal is true the returned POS is provably
    ///     minimal in (total literals, then clause count); when false the clause set is sound
    ///     but the exact search hit <paramref name="coverStepLimit" /> before proving
    ///     optimality (mirrors <see cref="MinimalSopWithStatus" /> so the POS path is no
    ///     longer silently unproven).
    /// </summary>
    public static (AstNode Expression, bool ProvenMinimal) MinimalPosWithStatus(IReadOnlyList<string> variables,
        IReadOnlyCollection<int> onSet, IReadOnlyCollection<int>? dontCareSet = null,
        long? pairComparisonLimit = null, CancellationToken cancellationToken = default,
        int coverStepLimit = ResourceBudget.DefaultCoverStepLimit)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalMinterms = 1 << variables.Count;
        var on = new HashSet<int>(onSet);
        var dc = dontCareSet == null ? new HashSet<int>() : new HashSet<int>(dontCareSet);
        dc.ExceptWith(on);

        var offSet = new List<int>();
        for (var m = 0; m < totalMinterms; m++)
            if (!on.Contains(m) && !dc.Contains(m))
                offSet.Add(m);

        if (offSet.Count == 0) return (ConstantNode.True, true);
        if (on.Count == 0) return (ConstantNode.False, true);

        var primes = GeneratePrimeImplicants(variables.Count, new HashSet<int>(offSet.Concat(dc)),
            pairComparisonLimit, cancellationToken);
        var (cover, proven) = SelectMinimumCover(primes, offSet, coverStepLimit, cancellationToken);

        // Each complement product (x & !y) becomes a clause (!x | y)
        var clauses = cover.Select(implicant => BuildClause(variables, implicant)).ToList();
        return (clauses.Count == 1 ? clauses[0] : new AndNode(clauses), proven);
    }

    private static List<Implicant> GeneratePrimeImplicants(int variableCount, HashSet<int> careSet,
        long? pairComparisonLimit = null, CancellationToken cancellationToken = default)
    {
        var fullMask = (1 << variableCount) - 1;
        var current = new HashSet<Implicant>();
        foreach (var minterm in careSet) current.Add(new Implicant(fullMask, minterm));
        var primes = new List<Implicant>();
        var comparisons = 0L;

        // Reusable popcount buckets (index = number of set value-bits, 0..variableCount);
        // sized once, cleared per mask group. Buckets hold POSITIONS within the materialized
        // level rather than cubes, which is what lets a merge be recorded as an array write.
        var buckets = new List<int>[variableCount + 1];
        for (var b = 0; b < buckets.Length; b++) buckets[b] = new List<int>();

        // Double-buffered levels. Each level used to allocate two fresh HashSet<Implicant> that
        // grew to thousands of entries; Clear() keeps the capacity, so the sets are paid for once
        // instead of once per level. Iteration order is unaffected: HashSet<T> enumerates its
        // entry array in insertion order, Clear() resets that array, and nothing here removes -
        // so a cleared-and-refilled set enumerates exactly like a fresh one. That matters,
        // because the order of `primes` decides candidate order in the cover search.
        var spare = new HashSet<Implicant>();

        // The current level materialized in enumeration order, plus a merged flag per position.
        // `merged` replaces a HashSet of combined cubes: each merging pair marked BOTH its cubes,
        // and a level produces several times more merges than it holds cubes, so that set was the
        // bulk of the remaining hashing. Positions turn it into two array writes.
        var levelCubes = new List<Implicant>();
        var merged = Array.Empty<bool>();

        // Mask grouping without LINQ. GroupBy allocated a Lookup plus a grouping object per
        // distinct mask on every level; this keeps first-appearance group order (what GroupBy
        // guarantees and the prime order depends on) using buffers reused across levels.
        var groupOfMask = new Dictionary<int, int>();
        var groups = new List<List<int>>();

        // Value -> position within the higher popcount bucket, rebuilt per adjacent bucket pair.
        // This is what replaces the quadratic scan below; see the merge loop for why a lookup
        // is enough. Positions are what keep the emission order identical to the scan's.
        var hiIndex = new Dictionary<int, int>();
        var partnerPos = new int[variableCount];
        var partnerBit = new int[variableCount];

        while (current.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var next = spare;
            next.Clear();

            levelCubes.Clear();
            foreach (var cube in current) levelCubes.Add(cube);
            if (merged.Length < levelCubes.Count) merged = new bool[levelCubes.Count];
            else Array.Clear(merged, 0, levelCubes.Count);

            groupOfMask.Clear();
            foreach (var g in groups) g.Clear();
            var groupCount = 0;
            for (var c = 0; c < levelCubes.Count; c++)
            {
                var mask = levelCubes[c].Mask;
                if (!groupOfMask.TryGetValue(mask, out var index))
                {
                    index = groupCount++;
                    groupOfMask[mask] = index;
                    if (groups.Count < groupCount) groups.Add(new List<int>());
                }

                groups[index].Add(c);
            }

            // Group by mask; within a mask two cubes can combine only if their values
            // differ in exactly one bit — i.e. their set-bit counts differ by one. Bucket
            // by value popcount and compare only adjacent buckets (classic QM speedup):
            // this yields the identical prime set while skipping every pair that provably
            // cannot merge.
            for (var g = 0; g < groupCount; g++)
            {
                foreach (var b in buckets) b.Clear();
                var group = groups[g];
                for (var c = 0; c < group.Count; c++)
                    buckets[System.Numerics.BitOperations.PopCount((uint)levelCubes[group[c]].Value)]
                        .Add(group[c]);

                for (var k = 0; k < buckets.Length - 1; k++)
                {
                    var lo = buckets[k];
                    var hi = buckets[k + 1];
                    if (lo.Count == 0 || hi.Count == 0) continue;

                    // Work accounting stays the notional pair count, so a caller's budget means
                    // the same thing it always did regardless of how the pairs are found.
                    comparisons += (long)lo.Count * hi.Count;
                    if (comparisons > pairComparisonLimit)
                        throw new ComputationBudgetExceededException(WorkLimitMessage);

                    // Partners are looked up, not scanned for. Every cube in a group shares one
                    // Mask, and Value is always a subset of Mask, so a cube in `hi` merges with
                    // `a` exactly when its Value is a.Value with ONE more bit set - and that bit
                    // must come from the free set (a.Mask & ~a.Value). That is at most
                    // variableCount possibilities to probe instead of the whole bucket, turning
                    // the level from O(|lo| x |hi|) into O(|lo| x variableCount). On a 10-variable
                    // dense care set the largest adjacent pair alone was ~53k comparisons; the
                    // whole generation measures ~1.9x faster there.
                    //
                    // Emission order is preserved exactly: partners are collected with their
                    // positions in `hi` and replayed in ascending position, which is the order the
                    // scan visited them. That matters because `next` is a HashSet enumerated in
                    // insertion order, so it decides the order of `primes`, hence the candidate
                    // order of the cover search and which of several equally costed covers wins.
                    // Not left to argument: the emitted prime sequence was dumped for both
                    // implementations, over a dense and a sparse 10-variable function, and the
                    // dumps are byte-identical (as are the resulting covers).
                    hiIndex.Clear();
                    for (var j = 0; j < hi.Count; j++) hiIndex[levelCubes[hi[j]].Value] = j;

                    for (var i = 0; i < lo.Count; i++)
                    {
                        // Dense levels pair millions of cubes; a per-level check alone
                        // leaves cancellation unobserved for tens of seconds
                        if ((i & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                        var aIndex = lo[i];
                        var a = levelCubes[aIndex];

                        var found = 0;
                        var free = a.Mask & ~a.Value;
                        while (free != 0)
                        {
                            var bit = free & -free;
                            free &= free - 1;
                            if (!hiIndex.TryGetValue(a.Value | bit, out var position)) continue;

                            // Insertion sort by position: at most variableCount entries.
                            var k2 = found++;
                            while (k2 > 0 && partnerPos[k2 - 1] > position)
                            {
                                partnerPos[k2] = partnerPos[k2 - 1];
                                partnerBit[k2] = partnerBit[k2 - 1];
                                k2--;
                            }

                            partnerPos[k2] = position;
                            partnerBit[k2] = bit;
                        }

                        for (var p = 0; p < found; p++)
                        {
                            var difference = partnerBit[p];
                            next.Add(new Implicant(a.Mask & ~difference, a.Value & ~difference));
                            merged[aIndex] = true;
                            merged[hi[partnerPos[p]]] = true;
                        }
                    }
                }
            }

            for (var c = 0; c < levelCubes.Count; c++)
                if (!merged[c])
                    primes.Add(levelCubes[c]);

            // Swap: the level just consumed becomes the buffer the next level fills.
            spare = current;
            current = next;
        }

        return primes;
    }

    /// <summary>
    ///     Minimum cover: covering-table reductions (essential primes, row dominance,
    ///     column dominance) to a fixpoint, then branch-and-bound with an independent-set
    ///     lower bound on the cyclic core. Returns whether minimality was proven — the
    ///     search never silently degrades: hitting the step limit is reported.
    /// </summary>
    private static (List<Implicant> Cover, bool Proven) SelectMinimumCover(List<Implicant> primes,
        List<int> onMinterms, int stepLimit, CancellationToken cancellationToken = default)
    {
        var cover = new List<Implicant>();
        var uncovered = new HashSet<int>(onMinterms);
        var candidates = new List<Implicant>(primes);

        ReduceCoverTable(candidates, uncovered, cover, cancellationToken);

        if (uncovered.Count == 0) return (cover, true);

        // Cyclic core: exact branch-and-bound on what the reductions could not resolve
        var search = new CoverSearch(candidates, stepLimit, cancellationToken);
        var best = search.Search(uncovered, new List<Implicant>(), null);
        var proven = best != null && !search.LimitHit;

        if (best == null)
        {
            // Work limit exceeded before any full cover — greedy completion (sound, unproven)
            best = new List<Implicant>();
            var remaining = new HashSet<int>(uncovered);
            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pick = candidates
                    .OrderByDescending(c => remaining.Count(c.Covers))
                    .ThenBy(c => c.LiteralCount)
                    .First();
                best.Add(pick);
                remaining.RemoveWhere(pick.Covers);
            }
        }

        cover.AddRange(best);
        return (cover, proven);
    }

    /// <summary>
    ///     Classic covering-table reductions to a fixpoint. Sound for the (literals, terms)
    ///     cost order: essentials are forced; a dominated row is covered by any cover of its
    ///     dominating row; a dominated column has a no-worse substitute.
    /// </summary>
    private static void ReduceCoverTable(List<Implicant> candidates, HashSet<int> uncovered, List<Implicant> cover,
        CancellationToken cancellationToken = default)
    {
        // Every buffer below used to be rebuilt from scratch on each fixpoint round, including a
        // JAGGED ulong[][] with one small array per row and per column. They are hoisted here and
        // grown on demand; the bitsets are flat, indexed as [item * words + word], which removes
        // both the per-round allocation and one pointer hop per access.
        var essentials = new List<Implicant>();
        var essentialSet = new HashSet<Implicant>();
        var rows = new List<int>();
        var uncoveredList = new List<int>();
        var rowBits = Array.Empty<ulong>();
        var rowPop = Array.Empty<int>();
        var colBits = Array.Empty<ulong>();
        var colPop = Array.Empty<int>();
        var dominated = Array.Empty<bool>();

        bool progress;
        do
        {
            progress = false;
            // Dense tables re-scan candidates per essential; without this check a
            // 13-variable table ignores cancellation for tens of seconds
            cancellationToken.ThrowIfCancellationRequested();

            // Essential primes: a minterm covered by exactly one candidate forces it.
            // Collect every essential in a single pass and apply them together — essentials
            // are forced into every minimal cover, so this yields the same forced set as
            // extracting them one at a time, but without restarting the whole fixpoint (and
            // its quadratic dominance passes) after each one.
            essentials.Clear();
            essentialSet.Clear();
            foreach (var minterm in uncovered)
            {
                Implicant sole = default;
                var coverCount = 0;
                foreach (var candidate in candidates)
                {
                    if (!candidate.Covers(minterm)) continue;
                    if (++coverCount > 1) break;
                    sole = candidate;
                }

                if (coverCount == 1 && essentialSet.Add(sole))
                    essentials.Add(sole);
            }

            if (essentials.Count > 0)
            {
                foreach (var essential in essentials)
                {
                    cover.Add(essential);
                    candidates.Remove(essential);
                    uncovered.RemoveWhere(essential.Covers);
                }

                progress = true;
            }

            if (progress || uncovered.Count == 0) continue;

            candidates.RemoveAll(c =>
            {
                foreach (var minterm in uncovered)
                    if (c.Covers(minterm))
                        return false;
                return true;
            });

            // Row dominance: if every candidate covering m1 also covers m2, drop m2.
            // Coverage is held as a bitmask over candidate indices, so the O(rows^2)
            // subset/equality tests are a handful of word ops instead of HashSet<int>
            // operations with per-row allocation.
            rows.Clear();
            foreach (var minterm in uncovered) rows.Add(minterm);
            var candWords = (candidates.Count + 63) / 64 + 1;
            if (rowBits.Length < rows.Count * candWords) rowBits = new ulong[rows.Count * candWords];
            else Array.Clear(rowBits, 0, rows.Count * candWords);
            if (rowPop.Length < rows.Count) rowPop = new int[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                var at = r * candWords;
                for (var i = 0; i < candidates.Count; i++)
                    if (candidates[i].Covers(rows[r]))
                        rowBits[at + (i >> 6)] |= 1UL << (i & 63);
                rowPop[r] = PopCount(rowBits, at, candWords);
            }

            for (var a = 0; a < rows.Count; a++)
            {
                // A single row-dominance pass over a dense table is O(rows^2); the
                // per-round check above is too coarse, so observe cancellation per row.
                cancellationToken.ThrowIfCancellationRequested();
                if (!uncovered.Contains(rows[a])) continue;
                for (var b = 0; b < rows.Count; b++)
                {
                    if (a == b || !uncovered.Contains(rows[b]) || !uncovered.Contains(rows[a])) continue;
                    if (rowPop[a] > rowPop[b]) continue;
                    if (rowPop[a] == rowPop[b] &&
                        MaskEquals(rowBits, a * candWords, rowBits, b * candWords, candWords) &&
                        rows[a] > rows[b]) continue;
                    if (IsSubset(rowBits, a * candWords, rowBits, b * candWords, candWords))
                    {
                        uncovered.Remove(rows[b]);
                        progress = true;
                    }
                }
            }

            if (progress) continue;

            // Column dominance is quadratic in candidates; on large tables the essential
            // and row-dominance passes above carry the load and the cyclic core that
            // reaches here is small
            if (candidates.Count > MaxColumnDominanceCandidates) continue;

            // Column dominance: if c1 covers a superset of c2's rows at no higher literal
            // cost, c2 has a no-worse substitute and can be dropped. Coverage is a bitmask
            // over the (positional) uncovered-minterm indices.
            uncoveredList.Clear();
            foreach (var minterm in uncovered) uncoveredList.Add(minterm);
            var uncoveredWords = (uncoveredList.Count + 63) / 64 + 1;
            var colCells = candidates.Count * uncoveredWords;
            if (colBits.Length < colCells) colBits = new ulong[colCells];
            else Array.Clear(colBits, 0, colCells);
            if (colPop.Length < candidates.Count) colPop = new int[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var at = i * uncoveredWords;
                for (var u = 0; u < uncoveredList.Count; u++)
                    if (candidates[i].Covers(uncoveredList[u]))
                        colBits[at + (u >> 6)] |= 1UL << (u & 63);
                colPop[i] = PopCount(colBits, at, uncoveredWords);
            }

            // Collect every dominated column in ONE pass, then drop them together.
            //
            // This used to remove a single candidate and `break` all the way out, which restarted
            // the whole fixpoint: essentials rescanned, both bitmask tables rebuilt from scratch
            // (each is rows x candidates coverage tests), row dominance redone - to delete one
            // more column. With a few hundred candidates that is a few hundred full rounds, and it
            // was 83% of exact minimization's total runtime. The essentials pass above was already
            // batched for exactly this reason; this is the same fix applied to columns.
            //
            // Batching is sound because dominance is transitive here: if k dominates i and i
            // dominates j, then k covers a superset of j's rows at no higher literal cost, so it
            // dominates j too. Removing i in the same pass therefore cannot rescue j. The `i > j`
            // tie-break is kept so that among columns with identical coverage and cost exactly one
            // - the lowest index - survives, and indices stay stable because nothing is removed
            // until the pass is over.
            if (dominated.Length < candidates.Count) dominated = new bool[candidates.Count];
            else Array.Clear(dominated, 0, candidates.Count);
            var anyDominated = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (dominated[i]) continue;
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (i == j || dominated[j]) continue;
                    var c1 = candidates[i];
                    var c2 = candidates[j];
                    if (c1.LiteralCount > c2.LiteralCount) continue;
                    if (colPop[i] == colPop[j] &&
                        MaskEquals(colBits, i * uncoveredWords, colBits, j * uncoveredWords, uncoveredWords) &&
                        c1.LiteralCount == c2.LiteralCount && i > j)
                        continue;
                    if (IsSubset(colBits, j * uncoveredWords, colBits, i * uncoveredWords, uncoveredWords))
                    {
                        dominated[j] = true;
                        anyDominated = true;
                    }
                }
            }

            if (anyDominated)
            {
                var write = 0;
                for (var read = 0; read < candidates.Count; read++)
                    if (!dominated[read])
                        candidates[write++] = candidates[read];
                candidates.RemoveRange(write, candidates.Count - write);
                progress = true;
            }
        } while (progress && uncovered.Count > 0);
    }

    /// <summary>
    ///     Bitset helpers over a FLAT backing array: each logical bitset occupies
    ///     <c>words</c> consecutive slots starting at the given offset. The covering table used a
    ///     jagged <c>ulong[][]</c> rebuilt each fixpoint round - one array object per row and per
    ///     column - which is what these offsets replace.
    /// </summary>
    private static int PopCount(ulong[] bits, int offset, int words)
    {
        var count = 0;
        for (var w = 0; w < words; w++) count += System.Numerics.BitOperations.PopCount(bits[offset + w]);
        return count;
    }

    /// <summary>Whether two equal-length bitsets hold the same elements.</summary>
    private static bool MaskEquals(ulong[] a, int offsetA, ulong[] b, int offsetB, int words)
    {
        for (var w = 0; w < words; w++)
            if (a[offsetA + w] != b[offsetB + w]) return false;
        return true;
    }

    /// <summary>Whether every bit of the first bitset is also set in the second (a ⊆ b).</summary>
    private static bool IsSubset(ulong[] a, int offsetA, ulong[] b, int offsetB, int words)
    {
        for (var w = 0; w < words; w++)
            if ((a[offsetA + w] & ~b[offsetB + w]) != 0) return false;
        return true;
    }

    private sealed class CoverSearch
    {
        private readonly List<Implicant> _candidates;
        private readonly int _stepLimit;
        private readonly CancellationToken _cancellationToken;
        private int _steps;

        // Per-depth scratch. Every node used to allocate a filtered+sorted option list and, per
        // option, a list of the minterms that option newly covers. The recursion is depth-first,
        // so one pair of lists per depth is enough and the search stops allocating per node.
        private readonly List<List<Implicant>> _optionsByDepth = new();
        private readonly List<List<int>> _coveredByDepth = new();

        // LowerBound ran once per node and allocated a HashSet plus a list per minterm.
        private readonly HashSet<int> _blocked = new();
        private readonly List<Implicant> _covering = new();

        public CoverSearch(List<Implicant> candidates, int stepLimit, CancellationToken cancellationToken)
        {
            _candidates = candidates;
            _stepLimit = stepLimit;
            _cancellationToken = cancellationToken;
        }

        public bool LimitHit { get; private set; }

        public List<Implicant>? Search(HashSet<int> uncovered, List<Implicant> partial, List<Implicant>? best)
        {
            return Search(uncovered, partial, best, 0);
        }

        private List<Implicant>? Search(HashSet<int> uncovered, List<Implicant> partial, List<Implicant>? best,
            int depth)
        {
            if (uncovered.Count == 0)
                return best == null || Cost(partial).CompareTo(Cost(best)) < 0
                    ? new List<Implicant>(partial)
                    : best;

            if (++_steps > _stepLimit)
            {
                LimitHit = true;
                return best;
            }

            // Branch-and-bound on a large cyclic core can run for many steps under a
            // high budget; observe cancellation here (the search checked only its own
            // step limit before, so a dense table ignored the token for tens of seconds)
            // without paying a check at every node.
            if ((_steps & 0x3FF) == 0) _cancellationToken.ThrowIfCancellationRequested();

            if (best != null)
            {
                // Independent-set lower bound: pairwise candidate-disjoint minterms each
                // need a distinct term; prune when even the bound cannot beat best
                var (lbLiterals, lbTerms) = LowerBound(uncovered);
                var partialCost = Cost(partial);
                var bound = (partialCost.Literals + lbLiterals, partialCost.Terms + lbTerms);
                if (bound.CompareTo(Cost(best)) >= 0) return best;
            }

            // Branch on the hardest minterm (fewest covering candidates). MinBy returns the FIRST
            // minimum in enumeration order, so the strict `<` below keeps the same tie-break.
            var target = 0;
            var bestCount = int.MaxValue;
            var seen = false;
            foreach (var minterm in uncovered)
            {
                var count = 0;
                foreach (var candidate in _candidates)
                    if (candidate.Covers(minterm))
                        count++;
                if (!seen || count < bestCount)
                {
                    seen = true;
                    bestCount = count;
                    target = minterm;
                }
            }

            var options = RentOptions(depth);
            foreach (var candidate in _candidates)
                if (candidate.Covers(target))
                    options.Add(candidate);
            if (options.Count == 0) return best; // uncoverable under current candidate set

            // OrderBy is a STABLE sort and the option order decides which of several equally
            // costed covers is returned, so this is insertion sort (also stable), not List.Sort.
            for (var i = 1; i < options.Count; i++)
            {
                var value = options[i];
                var j = i - 1;
                while (j >= 0 && options[j].LiteralCount > value.LiteralCount)
                {
                    options[j + 1] = options[j];
                    j--;
                }

                options[j + 1] = value;
            }

            var newlyCovered = RentCovered(depth);
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];

                newlyCovered.Clear();
                foreach (var minterm in uncovered)
                    if (option.Covers(minterm))
                        newlyCovered.Add(minterm);

                foreach (var minterm in newlyCovered) uncovered.Remove(minterm);
                partial.Add(option);

                best = Search(uncovered, partial, best, depth + 1);

                partial.RemoveAt(partial.Count - 1);
                foreach (var minterm in newlyCovered) uncovered.Add(minterm);
            }

            return best;
        }

        /// <summary>A cleared per-depth option buffer; the recursion below rents depth + 1.</summary>
        private List<Implicant> RentOptions(int depth)
        {
            while (_optionsByDepth.Count <= depth) _optionsByDepth.Add(new List<Implicant>());
            var list = _optionsByDepth[depth];
            list.Clear();
            return list;
        }

        private List<int> RentCovered(int depth)
        {
            while (_coveredByDepth.Count <= depth) _coveredByDepth.Add(new List<int>());
            var list = _coveredByDepth[depth];
            list.Clear();
            return list;
        }

        private (int Literals, int Terms) LowerBound(HashSet<int> uncovered)
        {
            var lbLiterals = 0;
            var lbTerms = 0;
            _blocked.Clear();

            foreach (var minterm in uncovered)
            {
                if (_blocked.Contains(minterm)) continue;

                _covering.Clear();
                foreach (var candidate in _candidates)
                    if (candidate.Covers(minterm))
                        _covering.Add(candidate);

                if (_covering.Count == 0) continue;

                lbTerms++;
                var cheapest = int.MaxValue;
                foreach (var candidate in _covering)
                    if (candidate.LiteralCount < cheapest)
                        cheapest = candidate.LiteralCount;
                lbLiterals += cheapest;

                foreach (var other in uncovered)
                {
                    if (_blocked.Contains(other)) continue;
                    foreach (var candidate in _covering)
                        if (candidate.Covers(other))
                        {
                            _blocked.Add(other);
                            break;
                        }
                }
            }

            return (lbLiterals, lbTerms);
        }
    }

    private static (int Literals, int Terms) Cost(List<Implicant> cover)
    {
        // Called up to three times per branch-and-bound node; LINQ's Sum allocated a delegate
        // and an enumerator on each one.
        var literals = 0;
        foreach (var implicant in cover) literals += implicant.LiteralCount;
        return (literals, cover.Count);
    }

    private static AstNode BuildSop(IReadOnlyList<string> variables, List<Implicant> cover)
    {
        // A term with no fixed variables covers everything: the function is constant 1
        if (cover.Any(c => c.Mask == 0)) return ConstantNode.True;

        var terms = cover
            .Select(implicant => BuildTerm(variables, implicant))
            .ToList();
        return terms.Count == 1 ? terms[0] : new OrNode(terms);
    }

    private static AstNode BuildTerm(IReadOnlyList<string> variables, Implicant implicant)
    {
        var literals = BuildLiterals(variables, implicant, negate: false);
        return literals.Count == 1 ? literals[0] : new AndNode(literals);
    }

    private static AstNode BuildClause(IReadOnlyList<string> variables, Implicant implicant)
    {
        if (implicant.Mask == 0) return ConstantNode.False;

        var literals = BuildLiterals(variables, implicant, negate: true);
        return literals.Count == 1 ? literals[0] : new OrNode(literals);
    }

    private static List<AstNode> BuildLiterals(IReadOnlyList<string> variables, Implicant implicant, bool negate)
    {
        var literals = new List<AstNode>();
        for (var j = 0; j < variables.Count; j++)
        {
            if ((implicant.Mask & (1 << j)) == 0) continue;

            var isSet = (implicant.Value & (1 << j)) != 0;
            var positive = negate ? !isSet : isSet;
            literals.Add(positive
                ? new VariableNode(variables[j])
                : new NotNode(new VariableNode(variables[j])));
        }

        return literals;
    }
}
