namespace LogicalOptimizer;

/// <summary>
///     Accumulates a CNF (clauses over 1-based DIMACS literals) with auxiliary-variable
///     allocation, for feeding encoders and the <see cref="SatSolver" />.
/// </summary>
public sealed class CnfBuilder
{
    private readonly List<int[]> _clauses = new();

    public CnfBuilder(int variableCount)
    {
        if (variableCount < 0) throw new ArgumentOutOfRangeException(nameof(variableCount));
        VariableCount = variableCount;
    }

    /// <summary>Total variables including allocated auxiliaries.</summary>
    public int VariableCount { get; private set; }

    public IReadOnlyList<int[]> Clauses => _clauses;

    public int NewVariable()
    {
        return ++VariableCount;
    }

    public void AddClause(params int[] literals)
    {
        _clauses.Add(literals);
    }

    public SatSolver ToSolver()
    {
        var solver = new SatSolver(VariableCount);
        foreach (var clause in _clauses) solver.AddClause(clause);
        return solver;
    }
}

/// <summary>
///     Which CNF encoding a cardinality constraint (<see cref="CardinalityEncoder" />) is
///     expanded with. <see cref="Auto" /> measures the applicable encodings and picks the
///     smallest (by clauses + auxiliary variables); it may change its choice between minor
///     releases, but only with a CHANGELOG note and never worse than the stable default beyond
///     a documented threshold on the fixed calibration corpus. All values are semantically
///     equivalent — they differ only in size and propagation strength.
/// </summary>
public enum CardinalityEncoding
{
    /// <summary>Measure the applicable encodings and pick the smallest (guaranteed ≤ the default).</summary>
    Auto,

    /// <summary>Binomial encoding: one clause per (k+1)-subset, no auxiliaries. Best for tiny n / k.</summary>
    Pairwise,

    /// <summary>Sequential counter (Sinz 2005): O(n·k) clauses and auxiliaries. The stable default.</summary>
    SequentialCounter,

    /// <summary>2D product encoding (Chen 2010) for at-most-one; falls back to the totalizer for k≠1.</summary>
    Product,

    /// <summary>Totalizer (Bailleux &amp; Boufkhad 2003): a capped unary-counter tree, strong propagation.</summary>
    Totalizer
}

/// <summary>
///     Which CNF encoding a pseudo-Boolean constraint (<see cref="PseudoBooleanEncoder" />) is
///     expanded with. <see cref="Auto" /> measures the applicable encodings and picks the
///     smallest; see the note on <see cref="CardinalityEncoding.Auto" /> for the between-release
///     policy. All values are semantically equivalent.
/// </summary>
public enum PseudoBooleanEncoding
{
    /// <summary>Measure the applicable encodings and pick the smallest (guaranteed ≤ the default).</summary>
    Auto,

    /// <summary>The decision-diagram / dynamic-programming expansion. The stable default.</summary>
    DynamicProgramming,

    /// <summary>Binary adder network (weights summed as binary numbers) compared against the bound.</summary>
    BinaryMerge,

    /// <summary>Generalized totalizer (Joshi et al. 2015): a weighted unary-counter tree, capped at bound+1.</summary>
    GeneralizedTotalizer
}

/// <summary>
///     The size an encoding call added to a <see cref="CnfBuilder" />: the number of clauses and
///     auxiliary variables introduced. Available from the encoding-selecting overloads for
///     diagnostics and for comparing encodings; the same numbers are also readable directly off
///     <see cref="CnfBuilder.Clauses" />.Count and <see cref="CnfBuilder.VariableCount" /> deltas.
/// </summary>
public readonly struct EncodingStats
{
    public EncodingStats(int clauses, int auxiliaryVariables)
    {
        Clauses = clauses;
        AuxiliaryVariables = auxiliaryVariables;
    }

    /// <summary>Clauses added by the encoding call.</summary>
    public int Clauses { get; }

    /// <summary>Auxiliary variables allocated by the encoding call.</summary>
    public int AuxiliaryVariables { get; }

    /// <summary>The size metric used to compare encodings: <see cref="Clauses" /> + <see cref="AuxiliaryVariables" />.</summary>
    public int Cost => Clauses + AuxiliaryVariables;

    public override string ToString()
    {
        return $"{Clauses} clauses, {AuxiliaryVariables} aux vars";
    }
}

/// <summary>
///     Cardinality constraints over literals. The parameterless overloads encode with the
///     sequential counter (Sinz 2005): O(n·k) clauses and auxiliaries, unit-propagation
///     preserves generalized arc consistency. Overloads taking a
///     <see cref="CardinalityEncoding" /> select from a portfolio of semantically equivalent
///     encodings and report the size they introduced.
/// </summary>
public static class CardinalityEncoder
{
    /// <summary>The applicable-encoding limits <see cref="CardinalityEncoding.Auto" /> considers.</summary>
    private const int PairwiseAutoClauseCap = 1024;

    /// <summary>At most k of the literals are true (sequential counter — the stable default output).</summary>
    public static void AtMostK(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        EncodeAtMostSequential(builder, literals, k);
    }

    /// <summary>At least k of the literals are true (the stable default output).</summary>
    public static void AtLeastK(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        EncodeAtLeastDefault(builder, literals, k);
    }

    /// <summary>Exactly k of the literals are true (the stable default output).</summary>
    public static void ExactlyK(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        AtMostK(builder, literals, k);
        AtLeastK(builder, literals, k);
    }

    /// <summary>
    ///     At most k of the literals are true, encoded with the chosen strategy. Returns the
    ///     clauses / auxiliary variables the call introduced. Passing
    ///     <see cref="CardinalityEncoding.SequentialCounter" /> reproduces the default output.
    /// </summary>
    public static EncodingStats AtMostK(CnfBuilder builder, IReadOnlyList<int> literals, int k,
        CardinalityEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(literals);
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k));
        var chosen = encoding == CardinalityEncoding.Auto
            ? SelectAtMost(builder.VariableCount, literals, k)
            : encoding;
        return Measure(builder, b => EncodeAtMostConcrete(b, literals, k, chosen));
    }

    /// <summary>At least k of the literals are true, encoded with the chosen strategy.</summary>
    public static EncodingStats AtLeastK(CnfBuilder builder, IReadOnlyList<int> literals, int k,
        CardinalityEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(literals);
        return Measure(builder, b => EncodeAtLeastConcrete(b, literals, k, encoding));
    }

    /// <summary>Exactly k of the literals are true, encoded with the chosen strategy.</summary>
    public static EncodingStats ExactlyK(CnfBuilder builder, IReadOnlyList<int> literals, int k,
        CardinalityEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(literals);
        return Measure(builder, b =>
        {
            EncodeAtMostConcrete(b, literals, k,
                encoding == CardinalityEncoding.Auto ? SelectAtMost(b.VariableCount, literals, k) : encoding);
            EncodeAtLeastConcrete(b, literals, k, encoding);
        });
    }

    // --- measurement -------------------------------------------------------

    private static EncodingStats Measure(CnfBuilder builder, Action<CnfBuilder> encode)
    {
        var clauses0 = builder.Clauses.Count;
        var variables0 = builder.VariableCount;
        encode(builder);
        return new EncodingStats(builder.Clauses.Count - clauses0, builder.VariableCount - variables0);
    }

    // --- dispatch ----------------------------------------------------------

    private static void EncodeAtMostConcrete(CnfBuilder builder, IReadOnlyList<int> literals, int k,
        CardinalityEncoding encoding)
    {
        var n = literals.Count;
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k));
        if (k >= n) return; // trivially satisfied

        switch (encoding)
        {
            case CardinalityEncoding.Pairwise:
                EncodeAtMostPairwise(builder, literals, k);
                break;
            case CardinalityEncoding.Product:
                if (k == 1) EncodeAtMostOneProduct(builder, literals);
                else EncodeAtMostTotalizer(builder, literals, k);
                break;
            case CardinalityEncoding.Totalizer:
                EncodeAtMostTotalizer(builder, literals, k);
                break;
            default: // SequentialCounter
                EncodeAtMostSequential(builder, literals, k);
                break;
        }
    }

    private static void EncodeAtLeastConcrete(CnfBuilder builder, IReadOnlyList<int> literals, int k,
        CardinalityEncoding encoding)
    {
        if (k <= 0) return;
        var n = literals.Count;
        if (k > n)
        {
            builder.AddClause(); // unsatisfiable
            return;
        }

        // At least k true ⟺ at most n-k of the negations true — every strategy handles both.
        var negations = new int[n];
        for (var i = 0; i < n; i++) negations[i] = -literals[i];
        var chosen = encoding == CardinalityEncoding.Auto
            ? SelectAtMost(builder.VariableCount, negations, n - k)
            : encoding;
        EncodeAtMostConcrete(builder, negations, n - k, chosen);
    }

    // --- Auto selection ----------------------------------------------------

    /// <summary>
    ///     Deterministic Auto policy: enumerate the applicable encodings for this shape, encode
    ///     each into a throwaway builder to measure its exact size, and keep the smallest by
    ///     <see cref="EncodingStats.Cost" /> (ties broken by <see cref="CardinalityEncoding" />
    ///     order). Because the sequential counter is always a candidate, Auto is never larger
    ///     than the default. Pairwise is only considered when its (k+1)-subset count is small,
    ///     and Product only for at-most-one; that keeps the trial itself cheap.
    /// </summary>
    private static CardinalityEncoding SelectAtMost(int variableCount, IReadOnlyList<int> literals, int k)
    {
        var n = literals.Count;
        if (k >= n || k < 0) return CardinalityEncoding.SequentialCounter; // trivial: any candidate is empty

        var candidates = new List<CardinalityEncoding>
        {
            CardinalityEncoding.SequentialCounter,
            CardinalityEncoding.Totalizer
        };
        if (k == 1) candidates.Add(CardinalityEncoding.Product);
        if (BinomialAtMost(n, k + 1, PairwiseAutoClauseCap) <= PairwiseAutoClauseCap)
            candidates.Add(CardinalityEncoding.Pairwise);

        var best = CardinalityEncoding.SequentialCounter;
        var bestCost = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var scratch = new CnfBuilder(variableCount);
            EncodeAtMostConcrete(scratch, literals, k, candidate);
            var cost = scratch.Clauses.Count + (scratch.VariableCount - variableCount);
            // Strictly-less keeps the earliest (smallest enum) candidate on ties → deterministic.
            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>C(n, r), saturating at <paramref name="cap" /> + 1 to avoid overflow on large shapes.</summary>
    private static long BinomialAtMost(int n, int r, int cap)
    {
        if (r < 0 || r > n) return 0;
        r = Math.Min(r, n - r);
        long result = 1;
        for (var i = 0; i < r; i++)
        {
            result = result * (n - i) / (i + 1);
            if (result > cap) return cap + 1;
        }

        return result;
    }

    // --- sequential counter (Sinz 2005) — the stable default ---------------

    private static void EncodeAtMostSequential(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k));
        var n = literals.Count;
        if (k >= n) return; // trivially satisfied

        if (k == 0)
        {
            foreach (var literal in literals) builder.AddClause(-literal);
            return;
        }

        // registers[i][j]: among the first i+1 literals at least j+1 are true
        var registers = new int[n - 1][];
        for (var i = 0; i < n - 1; i++)
        {
            registers[i] = new int[k];
            for (var j = 0; j < k; j++) registers[i][j] = builder.NewVariable();
        }

        builder.AddClause(-literals[0], registers[0][0]);
        for (var j = 1; j < k; j++) builder.AddClause(-registers[0][j]);

        for (var i = 1; i < n - 1; i++)
        {
            builder.AddClause(-literals[i], registers[i][0]);
            builder.AddClause(-registers[i - 1][0], registers[i][0]);
            for (var j = 1; j < k; j++)
            {
                builder.AddClause(-literals[i], -registers[i - 1][j - 1], registers[i][j]);
                builder.AddClause(-registers[i - 1][j], registers[i][j]);
            }

            builder.AddClause(-literals[i], -registers[i - 1][k - 1]);
        }

        builder.AddClause(-literals[n - 1], -registers[n - 2][k - 1]);
    }

    private static void EncodeAtLeastDefault(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        if (k <= 0) return;
        if (k > literals.Count)
        {
            builder.AddClause(); // unsatisfiable
            return;
        }

        if (k == 1)
        {
            builder.AddClause(literals.ToArray());
            return;
        }

        // At least k true ⟺ at most n-k of the negations true
        EncodeAtMostSequential(builder, literals.Select(l => -l).ToList(), literals.Count - k);
    }

    // --- pairwise / binomial ----------------------------------------------

    /// <summary>For every (k+1)-subset of the literals, forbid them all being true.</summary>
    private static void EncodeAtMostPairwise(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        var n = literals.Count;
        var size = k + 1; // 1 ≤ size ≤ n because 0 ≤ k < n
        var combo = new int[size];
        for (var i = 0; i < size; i++) combo[i] = i;

        while (true)
        {
            var clause = new int[size];
            for (var i = 0; i < size; i++) clause[i] = -literals[combo[i]];
            builder.AddClause(clause);

            // Advance to the next combination in colex order.
            var pos = size - 1;
            while (pos >= 0 && combo[pos] == n - size + pos) pos--;
            if (pos < 0) break;
            combo[pos]++;
            for (var i = pos + 1; i < size; i++) combo[i] = combo[i - 1] + 1;
        }
    }

    // --- product encoding (Chen 2010), at-most-one ------------------------

    private static void EncodeAtMostOneProduct(CnfBuilder builder, IReadOnlyList<int> literals)
    {
        var n = literals.Count;
        if (n <= 1) return;
        if (n <= 4)
        {
            EncodeAtMostPairwise(builder, literals, 1); // base case: pairwise at-most-one
            return;
        }

        var p = (int)Math.Ceiling(Math.Sqrt(n));
        var q = (n + p - 1) / p;
        var rows = new int[p];
        var cols = new int[q];
        for (var i = 0; i < p; i++) rows[i] = builder.NewVariable();
        for (var i = 0; i < q; i++) cols[i] = builder.NewVariable();

        for (var i = 0; i < n; i++)
        {
            var r = i / q;
            var c = i % q;
            builder.AddClause(-literals[i], rows[r]);
            builder.AddClause(-literals[i], cols[c]);
        }

        // At most one distinct (row, column) cell may be selected.
        EncodeAtMostOneProduct(builder, rows);
        EncodeAtMostOneProduct(builder, cols);
    }

    // --- totalizer (Bailleux & Boufkhad 2003), capped at k+1 --------------

    private static void EncodeAtMostTotalizer(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        var outputs = BuildTotalizer(builder, literals, 0, literals.Count, k + 1);
        // outputs[j] means "at least j+1 of the subtree are true"; forbid "at least k+1".
        if (outputs.Count > k) builder.AddClause(-outputs[k]);
    }

    /// <summary>
    ///     Build a capped unary counter for literals[lo..hi): outputs[j] is forced true when at
    ///     least j+1 of them are true. Only the "count-up" direction is emitted, which is exactly
    ///     what an at-most bound (a single forbidding unit on outputs[k]) needs.
    /// </summary>
    private static List<int> BuildTotalizer(CnfBuilder builder, IReadOnlyList<int> literals, int lo, int hi,
        int limit)
    {
        var count = hi - lo;
        if (count == 1) return new List<int> { literals[lo] };

        var mid = lo + count / 2;
        var left = BuildTotalizer(builder, literals, lo, mid, limit);
        var right = BuildTotalizer(builder, literals, mid, hi, limit);

        var m = Math.Min(count, limit);
        var outputs = new List<int>(m);
        for (var i = 0; i < m; i++) outputs.Add(builder.NewVariable());

        for (var alpha = 0; alpha <= left.Count; alpha++)
        for (var beta = 0; beta <= right.Count; beta++)
        {
            var sigma = alpha + beta;
            if (sigma < 1 || sigma > m) continue;

            // left ≥ alpha AND right ≥ beta ⟹ parent ≥ alpha+beta
            var clause = new List<int>(3);
            if (alpha > 0) clause.Add(-left[alpha - 1]);
            if (beta > 0) clause.Add(-right[beta - 1]);
            clause.Add(outputs[sigma - 1]);
            builder.AddClause(clause.ToArray());
        }

        return outputs;
    }
}

/// <summary>
///     Linear pseudo-Boolean constraints (sum of positive-weighted literals compared to a
///     bound). The parameterless overloads use a decision-diagram expansion with memoization —
///     the classic BDD encoding, polynomial for practical weight ranges. Overloads taking a
///     <see cref="PseudoBooleanEncoding" /> select from a portfolio of semantically equivalent
///     encodings and report the size they introduced.
/// </summary>
public static class PseudoBooleanEncoder
{
    /// <summary>The applicable-encoding limits <see cref="PseudoBooleanEncoding.Auto" /> considers.</summary>
    private const long GeneralizedTotalizerAutoBoundCap = 4096;

    private const int GeneralizedTotalizerAutoLiteralCap = 32;

    /// <summary>Σ weight_i · literal_i ≤ bound. Weights must be positive (stable default output).</summary>
    public static void AtMost(CnfBuilder builder, IReadOnlyList<int> literals, IReadOnlyList<long> weights,
        long bound)
    {
        EncodeAtMostDynamicProgramming(builder, literals, weights, bound);
    }

    /// <summary>Σ weight_i · literal_i ≥ bound. Weights must be positive (stable default output).</summary>
    public static void AtLeast(CnfBuilder builder, IReadOnlyList<int> literals, IReadOnlyList<long> weights,
        long bound)
    {
        // Σ w·x ≥ b ⟺ Σ w·(¬x) ≤ W - b
        var total = weights.Sum();
        AtMost(builder, literals.Select(l => -l).ToList(), weights, total - bound);
    }

    /// <summary>
    ///     Σ weight_i · literal_i ≤ bound, encoded with the chosen strategy. Returns the clauses /
    ///     auxiliary variables the call introduced. Passing
    ///     <see cref="PseudoBooleanEncoding.DynamicProgramming" /> reproduces the default output.
    /// </summary>
    public static EncodingStats AtMost(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound, PseudoBooleanEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWeighted(literals, weights);
        var chosen = encoding == PseudoBooleanEncoding.Auto
            ? SelectAtMost(builder.VariableCount, literals, weights, bound)
            : encoding;
        return Measure(builder, b => EncodeAtMostConcrete(b, literals, weights, bound, chosen));
    }

    /// <summary>Σ weight_i · literal_i ≥ bound, encoded with the chosen strategy.</summary>
    public static EncodingStats AtLeast(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound, PseudoBooleanEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWeighted(literals, weights);
        var total = weights.Sum();
        var negations = literals.Select(l => -l).ToList();
        var chosen = encoding == PseudoBooleanEncoding.Auto
            ? SelectAtMost(builder.VariableCount, negations, weights, total - bound)
            : encoding;
        return Measure(builder, b => EncodeAtMostConcrete(b, negations, weights, total - bound, chosen));
    }

    private static void ValidateWeighted(IReadOnlyList<int> literals, IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(literals);
        ArgumentNullException.ThrowIfNull(weights);
        if (literals.Count != weights.Count)
            throw new ArgumentException("Literals and weights must have equal length");
        if (weights.Any(w => w <= 0))
            throw new ArgumentException("Weights must be positive", nameof(weights));
    }

    private static EncodingStats Measure(CnfBuilder builder, Action<CnfBuilder> encode)
    {
        var clauses0 = builder.Clauses.Count;
        var variables0 = builder.VariableCount;
        encode(builder);
        return new EncodingStats(builder.Clauses.Count - clauses0, builder.VariableCount - variables0);
    }

    private static void EncodeAtMostConcrete(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound, PseudoBooleanEncoding encoding)
    {
        switch (encoding)
        {
            case PseudoBooleanEncoding.BinaryMerge:
                EncodeAtMostBinaryMerge(builder, literals, weights, bound);
                break;
            case PseudoBooleanEncoding.GeneralizedTotalizer:
                EncodeAtMostGeneralizedTotalizer(builder, literals, weights, bound);
                break;
            default: // DynamicProgramming
                EncodeAtMostDynamicProgramming(builder, literals, weights, bound);
                break;
        }
    }

    /// <summary>
    ///     Deterministic Auto policy mirroring <see cref="CardinalityEncoder" />: measure each
    ///     applicable encoding on a throwaway builder and keep the smallest by
    ///     <see cref="EncodingStats.Cost" /> (ties broken by <see cref="PseudoBooleanEncoding" />
    ///     order). Dynamic programming is always a candidate, so Auto is never worse than the
    ///     default. The generalized totalizer is only tried for small bounds and literal counts,
    ///     where its weighted counter tree stays cheap.
    /// </summary>
    private static PseudoBooleanEncoding SelectAtMost(int variableCount, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound)
    {
        var total = weights.Sum();
        if (bound < 0 || bound >= total) return PseudoBooleanEncoding.DynamicProgramming; // trivial shapes

        var candidates = new List<PseudoBooleanEncoding>
        {
            PseudoBooleanEncoding.DynamicProgramming,
            PseudoBooleanEncoding.BinaryMerge
        };
        if (bound + 1 <= GeneralizedTotalizerAutoBoundCap && literals.Count <= GeneralizedTotalizerAutoLiteralCap)
            candidates.Add(PseudoBooleanEncoding.GeneralizedTotalizer);

        var best = PseudoBooleanEncoding.DynamicProgramming;
        var bestCost = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var scratch = new CnfBuilder(variableCount);
            EncodeAtMostConcrete(scratch, literals, weights, bound, candidate);
            var cost = scratch.Clauses.Count + (scratch.VariableCount - variableCount);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        return best;
    }

    // --- dynamic programming / BDD (the stable default) -------------------

    private static void EncodeAtMostDynamicProgramming(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound)
    {
        if (literals.Count != weights.Count)
            throw new ArgumentException("Literals and weights must have equal length");
        if (weights.Any(w => w <= 0))
            throw new ArgumentException("Weights must be positive", nameof(weights));
        if (bound < 0)
        {
            builder.AddClause(); // nothing can be ≤ a negative bound with non-negative sums
            return;
        }

        // Suffix sums allow early "always fits" termination
        var suffix = new long[literals.Count + 1];
        for (var i = literals.Count - 1; i >= 0; i--) suffix[i] = suffix[i + 1] + weights[i];

        var memo = new Dictionary<(int Index, long Remaining), int>();
        var root = Encode(builder, literals, weights, suffix, 0, bound, memo);
        if (root == TrueNode) return;
        if (root == FalseNode)
        {
            builder.AddClause();
            return;
        }

        builder.AddClause(root);
    }

    private const int TrueNode = int.MaxValue;
    private const int FalseNode = int.MinValue;

    /// <summary>
    ///     Node asserting "the suffix from index onward spends at most remaining".
    ///     Returns TrueNode/FalseNode terminals or an auxiliary variable with
    ///     implication-direction clauses (sufficient to enforce the constraint).
    /// </summary>
    private static int Encode(CnfBuilder builder, IReadOnlyList<int> literals, IReadOnlyList<long> weights,
        long[] suffix, int index, long remaining, Dictionary<(int, long), int> memo)
    {
        if (remaining < 0) return FalseNode;
        if (suffix[index] <= remaining) return TrueNode; // everything fits regardless

        // remaining < suffix[index] and index == count would mean suffix 0 > remaining ≥ 0
        var key = (index, remaining);
        if (memo.TryGetValue(key, out var cached)) return cached;

        var high = Encode(builder, literals, weights, suffix, index + 1, remaining - weights[index], memo);
        var low = Encode(builder, literals, weights, suffix, index + 1, remaining, memo);

        var node = builder.NewVariable();
        var literal = literals[index];

        // node → (literal → high) and node → (¬literal → low)
        if (high == FalseNode) builder.AddClause(-node, -literal);
        else if (high != TrueNode) builder.AddClause(-node, -literal, high);
        if (low == FalseNode) builder.AddClause(-node, literal);
        else if (low != TrueNode) builder.AddClause(-node, literal, low);

        memo[key] = node;
        return node;
    }

    // --- generalized totalizer (Joshi, Martins & Manquinho 2015) ----------

    /// <summary>
    ///     Weighted unary counter tree capped at bound+1: each node keeps a variable per distinct
    ///     reachable partial sum value v (meaning "subtree weighted sum ≥ v"), any value beyond
    ///     the bound collapsing to the single overflow value bound+1, which the root then forbids.
    /// </summary>
    private static void EncodeAtMostGeneralizedTotalizer(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound)
    {
        if (bound < 0)
        {
            builder.AddClause();
            return;
        }

        var total = weights.Sum();
        if (bound >= total) return; // always satisfied

        var mu = bound + 1; // cap: everything ≥ mu is "over budget"
        var root = BuildGeneralizedTotalizer(builder, literals, weights, 0, literals.Count, mu);
        if (root.TryGetValue(mu, out var overflow)) builder.AddClause(-overflow);
    }

    /// <summary>
    ///     Build a weighted counter for literals[lo..hi): a map from reachable sum value v (1..mu)
    ///     to a variable forced true when the subtree weighted sum is ≥ v. Values are capped at mu.
    /// </summary>
    private static SortedDictionary<long, int> BuildGeneralizedTotalizer(CnfBuilder builder,
        IReadOnlyList<int> literals, IReadOnlyList<long> weights, int lo, int hi, long mu)
    {
        var count = hi - lo;
        if (count == 1)
        {
            var value = Math.Min(weights[lo], mu);
            return new SortedDictionary<long, int> { [value] = literals[lo] };
        }

        var mid = lo + count / 2;
        var left = BuildGeneralizedTotalizer(builder, literals, weights, lo, mid, mu);
        var right = BuildGeneralizedTotalizer(builder, literals, weights, mid, hi, mu);

        var parent = new SortedDictionary<long, int>();

        // Include a virtual "0 → true" entry on each side (that side contributes nothing).
        var leftEntries = new List<(long Value, int Var)> { (0, 0) };
        leftEntries.AddRange(left.Select(kv => (kv.Key, kv.Value)));
        var rightEntries = new List<(long Value, int Var)> { (0, 0) };
        rightEntries.AddRange(right.Select(kv => (kv.Key, kv.Value)));

        foreach (var (lv, la) in leftEntries)
        foreach (var (rv, rb) in rightEntries)
        {
            if (lv == 0 && rv == 0) continue;
            var sum = Math.Min(lv + rv, mu);
            if (!parent.TryGetValue(sum, out var ov))
            {
                ov = builder.NewVariable();
                parent[sum] = ov;
            }

            // left ≥ lv AND right ≥ rv ⟹ parent ≥ sum
            var clause = new List<int>(3);
            if (la != 0) clause.Add(-la);
            if (rb != 0) clause.Add(-rb);
            clause.Add(ov);
            builder.AddClause(clause.ToArray());
        }

        return parent;
    }

    // --- binary adder network (weights summed in binary, compared to bound) ---

    /// <summary>
    ///     Sum the weighted literals as a binary number with a carry-save adder network, then
    ///     assert the result ≤ bound with a constant comparator (one clause per zero bit of the
    ///     bound). Column bit 0 is the constant-false sentinel used for empty columns.
    /// </summary>
    private static void EncodeAtMostBinaryMerge(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, long bound)
    {
        if (bound < 0)
        {
            builder.AddClause();
            return;
        }

        var total = weights.Sum();
        if (bound >= total) return; // always satisfied

        // Bucket each literal into the bit columns set in its weight.
        var columns = new Dictionary<int, List<int>>();

        void Bucket(int column, int literal)
        {
            if (!columns.TryGetValue(column, out var list))
            {
                list = new List<int>();
                columns[column] = list;
            }

            list.Add(literal);
        }

        for (var i = 0; i < literals.Count; i++)
        {
            var weight = weights[i];
            for (var bit = 0; weight != 0; bit++, weight >>= 1)
                if ((weight & 1) != 0)
                    Bucket(bit, literals[i]);
        }

        // Reduce every column to a single output bit, carrying into the next column.
        var result = new List<int>(); // result[j] is the literal for output bit j, or 0 = constant false
        var maxColumn = columns.Count == 0 ? -1 : columns.Keys.Max();
        for (var j = 0; j <= maxColumn; j++)
        {
            var list = columns.TryGetValue(j, out var bucket) ? bucket : new List<int>();

            while (list.Count >= 3)
            {
                var a = list[^1];
                var b = list[^2];
                var c = list[^3];
                list.RemoveRange(list.Count - 3, 3);
                var (sum, carry) = FullAdder(builder, a, b, c);
                list.Add(sum);
                Bucket(j + 1, carry);
                maxColumn = Math.Max(maxColumn, j + 1);
            }

            if (list.Count == 2)
            {
                var (sum, carry) = HalfAdder(builder, list[0], list[1]);
                list = new List<int> { sum };
                Bucket(j + 1, carry);
                maxColumn = Math.Max(maxColumn, j + 1);
            }

            result.Add(list.Count == 1 ? list[0] : 0);
        }

        AssertLessOrEqualConstant(builder, result, bound);
    }

    /// <summary>sum = a⊕b⊕c, carry = majority(a,b,c), both with full Tseitin equivalences.</summary>
    private static (int Sum, int Carry) FullAdder(CnfBuilder builder, int a, int b, int c)
    {
        var sum = builder.NewVariable();
        var carry = builder.NewVariable();
        for (var mask = 0; mask < 8; mask++)
        {
            var av = (mask & 1) != 0;
            var bv = (mask & 2) != 0;
            var cv = (mask & 4) != 0;
            var count = (av ? 1 : 0) + (bv ? 1 : 0) + (cv ? 1 : 0);
            var sumBit = count % 2 == 1;
            var carryBit = count >= 2;
            var antecedent = new[] { av ? -a : a, bv ? -b : b, cv ? -c : c };
            builder.AddClause(antecedent.Append(sumBit ? sum : -sum).ToArray());
            builder.AddClause(antecedent.Append(carryBit ? carry : -carry).ToArray());
        }

        return (sum, carry);
    }

    /// <summary>sum = a⊕b, carry = a∧b, both with full Tseitin equivalences.</summary>
    private static (int Sum, int Carry) HalfAdder(CnfBuilder builder, int a, int b)
    {
        var sum = builder.NewVariable();
        var carry = builder.NewVariable();
        for (var mask = 0; mask < 4; mask++)
        {
            var av = (mask & 1) != 0;
            var bv = (mask & 2) != 0;
            var sumBit = av ^ bv;
            var carryBit = av && bv;
            var antecedent = new[] { av ? -a : a, bv ? -b : b };
            builder.AddClause(antecedent.Append(sumBit ? sum : -sum).ToArray());
            builder.AddClause(antecedent.Append(carryBit ? carry : -carry).ToArray());
        }

        return (sum, carry);
    }

    /// <summary>
    ///     Assert the binary number in <paramref name="bits" /> (LSB first, 0 = constant false) is
    ///     ≤ <paramref name="bound" />. For every zero bit i of the bound, forbid "bit i is 1 and
    ///     all higher bits equal the bound" — i.e. that this is the most significant differing bit.
    /// </summary>
    private static void AssertLessOrEqualConstant(CnfBuilder builder, List<int> bits, long bound)
    {
        var width = bits.Count;
        for (var i = 0; i < width; i++)
        {
            if (((bound >> i) & 1) != 0) continue; // bound bit is 1 → no constraint at this position
            var ri = bits[i];
            if (ri == 0) continue; // result bit is constant 0 → can never be the differing 1

            var clause = new List<int> { -ri };
            var trivial = false;
            for (var j = i + 1; j < width; j++)
            {
                var rj = bits[j];
                if (((bound >> j) & 1) != 0)
                {
                    // bound bit 1: differs when result bit is 0
                    if (rj == 0)
                    {
                        trivial = true; // result bit is always 0 → differs → clause satisfied
                        break;
                    }

                    clause.Add(-rj);
                }
                else
                {
                    // bound bit 0: differs when result bit is 1
                    if (rj != 0) clause.Add(rj);
                }
            }

            if (!trivial) builder.AddClause(clause.ToArray());
        }
    }
}
