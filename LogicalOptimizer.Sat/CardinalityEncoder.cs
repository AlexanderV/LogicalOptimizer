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
///     Cardinality constraints over literals, encoded with the sequential counter
///     (Sinz 2005): O(n·k) clauses and auxiliaries, unit-propagation preserves
///     generalized arc consistency.
/// </summary>
public static class CardinalityEncoder
{
    /// <summary>At most k of the literals are true.</summary>
    public static void AtMostK(CnfBuilder builder, IReadOnlyList<int> literals, int k)
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

    /// <summary>At least k of the literals are true.</summary>
    public static void AtLeastK(CnfBuilder builder, IReadOnlyList<int> literals, int k)
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
        AtMostK(builder, literals.Select(l => -l).ToList(), literals.Count - k);
    }

    /// <summary>Exactly k of the literals are true.</summary>
    public static void ExactlyK(CnfBuilder builder, IReadOnlyList<int> literals, int k)
    {
        AtMostK(builder, literals, k);
        AtLeastK(builder, literals, k);
    }
}

/// <summary>
///     Linear pseudo-Boolean constraints (sum of positive-weighted literals compared to a
///     bound), encoded through a decision-diagram expansion with memoization — the classic
///     BDD encoding of PB constraints, polynomial for practical weight ranges.
/// </summary>
public static class PseudoBooleanEncoder
{
    /// <summary>Σ weight_i · literal_i ≤ bound. Weights must be positive.</summary>
    public static void AtMost(CnfBuilder builder, IReadOnlyList<int> literals, IReadOnlyList<long> weights,
        long bound)
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

    /// <summary>Σ weight_i · literal_i ≥ bound. Weights must be positive.</summary>
    public static void AtLeast(CnfBuilder builder, IReadOnlyList<int> literals, IReadOnlyList<long> weights,
        long bound)
    {
        // Σ w·x ≥ b ⟺ Σ w·(¬x) ≤ W - b
        var total = weights.Sum();
        AtMost(builder, literals.Select(l => -l).ToList(), weights, total - bound);
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
}
