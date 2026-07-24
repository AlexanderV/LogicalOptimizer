namespace LogicalOptimizer;

public enum MaxSatStatus
{
    /// <summary>An optimal assignment was found and proven optimal.</summary>
    Optimal,

    /// <summary>The hard clauses alone are unsatisfiable.</summary>
    HardClausesUnsatisfiable,

    /// <summary>The conflict budget ran out; <see cref="MaxSatResult.Cost" /> is the best found.</summary>
    Unknown
}

/// <summary>Outcome of a MaxSAT optimization.</summary>
public sealed class MaxSatResult
{
    internal MaxSatResult(MaxSatStatus status, long cost, bool[]? values)
    {
        Status = status;
        Cost = cost;
        Values = values;
    }

    public MaxSatStatus Status { get; }

    /// <summary>Total weight of falsified soft clauses (optimal when Status is Optimal).</summary>
    public long Cost { get; }

    /// <summary>Variable values indexed 1..variableCount; null when hard clauses are UNSAT.</summary>
    public bool[]? Values { get; }

    public bool GetValue(int variable)
    {
        if (Values == null) throw new InvalidOperationException("No model available");
        return Values[variable];
    }
}

/// <summary>
///     Weighted partial MaxSAT: hard clauses must hold, soft clauses carry positive
///     weights and the total weight of falsified softs is minimized. Implementation:
///     each soft clause gets a relaxation literal; a linear search tightens a
///     pseudo-Boolean bound on the relaxation weights until UNSAT proves optimality.
///     Built entirely on the in-house solver and encoders — no dependencies.
/// </summary>
public sealed class MaxSatSolver
{
    private readonly int _variableCount;
    private readonly List<int[]> _hardClauses = new();
    private readonly List<(int Weight, int[] Clause)> _softClauses = new();

    public MaxSatSolver(int variableCount)
    {
        if (variableCount < 0) throw new ArgumentOutOfRangeException(nameof(variableCount));
        _variableCount = variableCount;
    }

    public void AddHard(params int[] literals)
    {
        _hardClauses.Add(literals.ToArray());
    }

    public void AddSoft(int weight, params int[] literals)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight), "Soft weights must be positive");
        _softClauses.Add((weight, literals.ToArray()));
    }

    public MaxSatResult Solve(int maxConflictsPerCall = 1_000_000, CancellationToken cancellationToken = default)
    {
        // Relaxation variables come right after the original ones
        var relaxationOf = new int[_softClauses.Count];
        for (var i = 0; i < _softClauses.Count; i++)
            relaxationOf[i] = _variableCount + 1 + i;

        var bestValues = (bool[]?)null;
        var bestCost = long.MaxValue;
        var upperBound = (long?)null; // exclusive bound to try next (cost - 1)

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var builder = new CnfBuilder(_variableCount + _softClauses.Count);
            foreach (var clause in _hardClauses) builder.AddClause(clause);
            for (var i = 0; i < _softClauses.Count; i++)
                builder.AddClause(_softClauses[i].Clause.Append(relaxationOf[i]).ToArray());

            if (upperBound.HasValue)
                PseudoBooleanEncoder.AtMost(builder,
                    relaxationOf,
                    _softClauses.Select(s => (long)s.Weight).ToList(),
                    upperBound.Value);

            var solver = builder.ToSolver();
            switch (solver.Solve(maxConflictsPerCall, cancellationToken))
            {
                case SatResult.Unsatisfiable when bestValues == null:
                    return new MaxSatResult(MaxSatStatus.HardClausesUnsatisfiable, long.MaxValue, null);

                case SatResult.Unsatisfiable:
                    // No assignment beats the best found: it is optimal
                    return new MaxSatResult(MaxSatStatus.Optimal, bestCost, bestValues);

                case SatResult.Unknown:
                    return bestValues == null
                        ? new MaxSatResult(MaxSatStatus.Unknown, long.MaxValue, null)
                        : new MaxSatResult(MaxSatStatus.Unknown, bestCost, bestValues);

                default:
                    var values = new bool[_variableCount + 1];
                    for (var v = 1; v <= _variableCount; v++) values[v] = solver.GetValue(v);

                    // True cost from the ORIGINAL soft clauses (a relaxation variable may
                    // be set even when its clause happens to be satisfied)
                    var cost = 0L;
                    foreach (var (weight, clause) in _softClauses)
                        if (!clause.Any(l => values[Math.Abs(l)] == l > 0))
                            cost += weight;

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestValues = values;
                    }

                    if (bestCost == 0)
                        return new MaxSatResult(MaxSatStatus.Optimal, 0, bestValues);
                    upperBound = bestCost - 1;
                    continue;
            }
        }
    }
}
