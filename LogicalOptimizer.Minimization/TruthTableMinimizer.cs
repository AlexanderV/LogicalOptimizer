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
        var current = new HashSet<Implicant>(careSet.Select(m => new Implicant(fullMask, m)));
        var primes = new List<Implicant>();
        var comparisons = 0L;

        // Reusable popcount buckets (index = number of set value-bits, 0..variableCount);
        // sized once, cleared per mask group.
        var buckets = new List<Implicant>[variableCount + 1];
        for (var b = 0; b < buckets.Length; b++) buckets[b] = new List<Implicant>();

        while (current.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var next = new HashSet<Implicant>();
            var combined = new HashSet<Implicant>();

            // Group by mask; within a mask two cubes can combine only if their values
            // differ in exactly one bit — i.e. their set-bit counts differ by one. Bucket
            // by value popcount and compare only adjacent buckets (classic QM speedup):
            // this yields the identical prime set while skipping every pair that provably
            // cannot merge.
            foreach (var group in current.GroupBy(c => c.Mask))
            {
                foreach (var b in buckets) b.Clear();
                foreach (var cube in group)
                    buckets[System.Numerics.BitOperations.PopCount((uint)cube.Value)].Add(cube);

                for (var k = 0; k < buckets.Length - 1; k++)
                {
                    var lo = buckets[k];
                    var hi = buckets[k + 1];
                    if (lo.Count == 0 || hi.Count == 0) continue;

                    comparisons += (long)lo.Count * hi.Count;
                    if (comparisons > pairComparisonLimit)
                        throw new ComputationBudgetExceededException(WorkLimitMessage);

                    for (var i = 0; i < lo.Count; i++)
                    {
                        // Dense levels pair millions of cubes; a per-level check alone
                        // leaves cancellation unobserved for tens of seconds
                        if ((i & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                        var a = lo[i];
                        for (var j = 0; j < hi.Count; j++)
                        {
                            var b = hi[j];
                            var difference = a.Value ^ b.Value;
                            if (System.Numerics.BitOperations.PopCount((uint)difference) != 1) continue;

                            next.Add(new Implicant(a.Mask & ~difference, a.Value & ~difference));
                            combined.Add(a);
                            combined.Add(b);
                        }
                    }
                }
            }

            primes.AddRange(current.Where(c => !combined.Contains(c)));
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
            var essentials = new List<Implicant>();
            var essentialSet = new HashSet<Implicant>();
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

            candidates.RemoveAll(c => !uncovered.Any(c.Covers));

            // Row dominance: if every candidate covering m1 also covers m2, drop m2.
            // Coverage is held as a bitmask over candidate indices, so the O(rows^2)
            // subset/equality tests are a handful of word ops instead of HashSet<int>
            // operations with per-row allocation.
            var rows = uncovered.ToList();
            var candWords = (candidates.Count + 63) / 64 + 1;
            var rowMask = new ulong[rows.Count][];
            var rowPop = new int[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                var mask = new ulong[candWords];
                for (var i = 0; i < candidates.Count; i++)
                    if (candidates[i].Covers(rows[r]))
                        mask[i >> 6] |= 1UL << (i & 63);
                rowMask[r] = mask;
                rowPop[r] = PopCount(mask);
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
                    if (rowPop[a] == rowPop[b] && MaskEquals(rowMask[a], rowMask[b]) && rows[a] > rows[b]) continue;
                    if (IsSubset(rowMask[a], rowMask[b]))
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
            var uncoveredList = uncovered.ToList();
            var uncoveredWords = (uncoveredList.Count + 63) / 64 + 1;
            var colMask = new ulong[candidates.Count][];
            var colPop = new int[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var mask = new ulong[uncoveredWords];
                for (var u = 0; u < uncoveredList.Count; u++)
                    if (candidates[i].Covers(uncoveredList[u]))
                        mask[u >> 6] |= 1UL << (u & 63);
                colMask[i] = mask;
                colPop[i] = PopCount(mask);
            }

            for (var i = 0; i < candidates.Count && !progress; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    var c1 = candidates[i];
                    var c2 = candidates[j];
                    if (c1.LiteralCount > c2.LiteralCount) continue;
                    if (colPop[i] == colPop[j] && MaskEquals(colMask[i], colMask[j]) &&
                        c1.LiteralCount == c2.LiteralCount && i > j)
                        continue;
                    if (IsSubset(colMask[j], colMask[i]))
                    {
                        candidates.RemoveAt(j);
                        progress = true;
                        break;
                    }
                }
            }
        } while (progress && uncovered.Count > 0);
    }

    /// <summary>Total set bits across a bitset stored as 64-bit words.</summary>
    private static int PopCount(ulong[] mask)
    {
        var count = 0;
        foreach (var w in mask) count += System.Numerics.BitOperations.PopCount(w);
        return count;
    }

    /// <summary>Whether two equal-length bitsets hold the same elements.</summary>
    private static bool MaskEquals(ulong[] a, ulong[] b)
    {
        for (var w = 0; w < a.Length; w++)
            if (a[w] != b[w]) return false;
        return true;
    }

    /// <summary>Whether every bit of <paramref name="a" /> is also set in <paramref name="b" /> (a ⊆ b).</summary>
    private static bool IsSubset(ulong[] a, ulong[] b)
    {
        for (var w = 0; w < a.Length; w++)
            if ((a[w] & ~b[w]) != 0) return false;
        return true;
    }

    private sealed class CoverSearch
    {
        private readonly List<Implicant> _candidates;
        private readonly int _stepLimit;
        private readonly CancellationToken _cancellationToken;
        private int _steps;

        public CoverSearch(List<Implicant> candidates, int stepLimit, CancellationToken cancellationToken)
        {
            _candidates = candidates;
            _stepLimit = stepLimit;
            _cancellationToken = cancellationToken;
        }

        public bool LimitHit { get; private set; }

        public List<Implicant>? Search(HashSet<int> uncovered, List<Implicant> partial, List<Implicant>? best)
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

            // Branch on the hardest minterm (fewest covering candidates)
            var target = uncovered.MinBy(m => _candidates.Count(c => c.Covers(m)));
            var options = _candidates.Where(c => c.Covers(target)).OrderBy(c => c.LiteralCount).ToList();
            if (options.Count == 0) return best; // uncoverable under current candidate set

            foreach (var option in options)
            {
                var newlyCovered = uncovered.Where(option.Covers).ToList();
                uncovered.ExceptWith(newlyCovered);
                partial.Add(option);

                best = Search(uncovered, partial, best);

                partial.RemoveAt(partial.Count - 1);
                uncovered.UnionWith(newlyCovered);
            }

            return best;
        }

        private (int Literals, int Terms) LowerBound(HashSet<int> uncovered)
        {
            var lbLiterals = 0;
            var lbTerms = 0;
            var blocked = new HashSet<int>();

            foreach (var minterm in uncovered)
            {
                if (blocked.Contains(minterm)) continue;

                List<Implicant>? covering = null;
                foreach (var candidate in _candidates)
                    if (candidate.Covers(minterm))
                        (covering ??= new List<Implicant>()).Add(candidate);

                if (covering == null) continue;

                lbTerms++;
                lbLiterals += covering.Min(c => c.LiteralCount);
                foreach (var other in uncovered)
                    if (!blocked.Contains(other) && covering.Any(c => c.Covers(other)))
                        blocked.Add(other);
            }

            return (lbLiterals, lbTerms);
        }
    }

    private static (int Literals, int Terms) Cost(List<Implicant> cover)
    {
        return (cover.Sum(c => c.LiteralCount), cover.Count);
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
