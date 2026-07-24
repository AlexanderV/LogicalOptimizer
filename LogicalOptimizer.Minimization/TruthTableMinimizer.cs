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
    ///     Optional work budget for prime-implicant generation; when exceeded an
    ///     <see cref="InvalidOperationException" /> is thrown so callers can fall back
    ///     to heuristic simplification (dense functions near 12 variables can otherwise
    ///     cost seconds).
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation, observed per QM level.</param>
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
        cancellationToken.ThrowIfCancellationRequested();
        var totalMinterms = 1 << variables.Count;
        var on = new HashSet<int>(onSet);
        var dc = dontCareSet == null ? new HashSet<int>() : new HashSet<int>(dontCareSet);
        dc.ExceptWith(on);

        var offSet = new List<int>();
        for (var m = 0; m < totalMinterms; m++)
            if (!on.Contains(m) && !dc.Contains(m))
                offSet.Add(m);

        if (offSet.Count == 0) return ConstantNode.True;
        if (on.Count == 0) return ConstantNode.False;

        var primes = GeneratePrimeImplicants(variables.Count, new HashSet<int>(offSet.Concat(dc)),
            pairComparisonLimit, cancellationToken);
        var (cover, _) = SelectMinimumCover(primes, offSet, ResourceBudget.DefaultCoverStepLimit);

        // Each complement product (x & !y) becomes a clause (!x | y)
        var clauses = cover.Select(implicant => BuildClause(variables, implicant)).ToList();
        return clauses.Aggregate((a, b) => new AndNode(a, b));
    }

    private static List<Implicant> GeneratePrimeImplicants(int variableCount, HashSet<int> careSet,
        long? pairComparisonLimit = null, CancellationToken cancellationToken = default)
    {
        var fullMask = (1 << variableCount) - 1;
        var current = new HashSet<Implicant>(careSet.Select(m => new Implicant(fullMask, m)));
        var primes = new List<Implicant>();
        var comparisons = 0L;

        while (current.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var next = new HashSet<Implicant>();
            var combined = new HashSet<Implicant>();

            // Group by mask; combine cubes whose values differ in exactly one masked bit
            foreach (var group in current.GroupBy(c => c.Mask))
            {
                var cubes = group.ToList();
                comparisons += (long)cubes.Count * (cubes.Count - 1) / 2;
                if (comparisons > pairComparisonLimit)
                    throw new InvalidOperationException(WorkLimitMessage);

                for (var i = 0; i < cubes.Count; i++)
                {
                    // Dense levels pair millions of cubes; a per-level check alone
                    // leaves cancellation unobserved for tens of seconds
                    if ((i & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                    for (var j = i + 1; j < cubes.Count; j++)
                    {
                        var difference = cubes[i].Value ^ cubes[j].Value;
                        if (System.Numerics.BitOperations.PopCount((uint)difference) != 1) continue;

                        next.Add(new Implicant(cubes[i].Mask & ~difference, cubes[i].Value & ~difference));
                        combined.Add(cubes[i]);
                        combined.Add(cubes[j]);
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
        var search = new CoverSearch(candidates, stepLimit);
        var best = search.Search(uncovered, new List<Implicant>(), null);
        var proven = best != null && !search.LimitHit;

        if (best == null)
        {
            // Work limit exceeded before any full cover — greedy completion (sound, unproven)
            best = new List<Implicant>();
            var remaining = new HashSet<int>(uncovered);
            while (remaining.Count > 0)
            {
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

            // Essential primes: a minterm covered by exactly one candidate forces it
            foreach (var minterm in uncovered)
            {
                Implicant? sole = null;
                var coverCount = 0;
                foreach (var candidate in candidates)
                {
                    if (!candidate.Covers(minterm)) continue;
                    coverCount++;
                    sole = candidate;
                    if (coverCount > 1) break;
                }

                if (coverCount == 1 && sole.HasValue)
                {
                    cover.Add(sole.Value);
                    uncovered.RemoveWhere(m => sole.Value.Covers(m));
                    candidates.Remove(sole.Value);
                    progress = true;
                    break;
                }
            }

            if (progress || uncovered.Count == 0) continue;

            candidates.RemoveAll(c => !uncovered.Any(c.Covers));

            // Row dominance: if every candidate covering m1 also covers m2, drop m2
            var rows = uncovered.ToList();
            var rowCandidates = rows.ToDictionary(
                m => m,
                m => candidates.Select((c, i) => (c, i)).Where(x => x.c.Covers(m)).Select(x => x.i).ToHashSet());
            foreach (var m1 in rows)
            {
                if (!uncovered.Contains(m1)) continue;
                foreach (var m2 in rows)
                {
                    if (m1 == m2 || !uncovered.Contains(m2) || !uncovered.Contains(m1)) continue;
                    if (rowCandidates[m1].Count > rowCandidates[m2].Count) continue;
                    if (rowCandidates[m1].SetEquals(rowCandidates[m2]) && m1 > m2) continue;
                    if (rowCandidates[m1].IsSubsetOf(rowCandidates[m2]))
                    {
                        uncovered.Remove(m2);
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
            // cost, c2 has a no-worse substitute and can be dropped
            var columnRows = candidates.ToDictionary(
                c => c,
                c => uncovered.Where(c.Covers).ToHashSet());
            for (var i = 0; i < candidates.Count && !progress; i++)
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    var c1 = candidates[i];
                    var c2 = candidates[j];
                    if (c1.LiteralCount > c2.LiteralCount) continue;
                    if (columnRows[c1].Count == columnRows[c2].Count &&
                        columnRows[c1].SetEquals(columnRows[c2]) && c1.LiteralCount == c2.LiteralCount && i > j)
                        continue;
                    if (columnRows[c2].IsSubsetOf(columnRows[c1]))
                    {
                        candidates.RemoveAt(j);
                        progress = true;
                        break;
                    }
                }
        } while (progress && uncovered.Count > 0);
    }

    private sealed class CoverSearch
    {
        private readonly List<Implicant> _candidates;
        private readonly int _stepLimit;
        private int _steps;

        public CoverSearch(List<Implicant> candidates, int stepLimit)
        {
            _candidates = candidates;
            _stepLimit = stepLimit;
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
        return terms.Aggregate((a, b) => new OrNode(a, b));
    }

    private static AstNode BuildTerm(IReadOnlyList<string> variables, Implicant implicant)
    {
        var literals = BuildLiterals(variables, implicant, negate: false);
        return literals.Aggregate((a, b) => new AndNode(a, b));
    }

    private static AstNode BuildClause(IReadOnlyList<string> variables, Implicant implicant)
    {
        if (implicant.Mask == 0) return ConstantNode.False;

        var literals = BuildLiterals(variables, implicant, negate: true);
        return literals.Aggregate((a, b) => new OrNode(a, b));
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
