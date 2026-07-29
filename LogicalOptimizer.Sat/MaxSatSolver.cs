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

/// <summary>
///     Which MaxSAT search the solver runs. All values return the SAME proven optimum on the
///     instances they solve to completion; they differ only in the search path (and therefore
///     in which instances stay within a given conflict budget).
/// </summary>
public enum MaxSatAlgorithm
{
    /// <summary>
    ///     Let the solver choose. Currently routes to <see cref="Linear" /> (the stable default);
    ///     the routing may change between minor releases, but only ever between algorithms that
    ///     return the same proven optimum, and never at the cost of correctness.
    /// </summary>
    Auto,

    /// <summary>
    ///     The model-improving linear search: tighten a pseudo-Boolean upper bound on the
    ///     relaxation weights until UNSAT proves optimality. This is exactly what the
    ///     parameterless <see cref="MaxSatSolver.Solve(int, System.Threading.CancellationToken)" />
    ///     runs.
    /// </summary>
    Linear,

    /// <summary>
    ///     Core-guided (unweighted MSU3 / weighted MSU3-style) lower-bound search: solve under
    ///     soft-selector assumptions, extract UNSAT cores, relax them with a cardinality
    ///     (pseudo-Boolean when weighted) bound that is raised one round at a time until the
    ///     formula becomes SAT — at which point the bound is a proven optimum.
    /// </summary>
    CoreGuided
}

/// <summary>Outcome of a MaxSAT optimization.</summary>
public sealed class MaxSatResult
{
    internal MaxSatResult(MaxSatStatus status, long cost, bool[]? values)
        : this(status, cost, values, cost, cost)
    {
    }

    internal MaxSatResult(MaxSatStatus status, long cost, bool[]? values, long lowerBound, long upperBound)
    {
        Status = status;
        Cost = cost;
        Values = values;
        LowerBound = lowerBound;
        UpperBound = upperBound;
    }

    public MaxSatStatus Status { get; }

    /// <summary>Total weight of falsified soft clauses (optimal when Status is Optimal).</summary>
    public long Cost { get; }

    /// <summary>Variable values indexed 1..variableCount; null when hard clauses are UNSAT.</summary>
    public bool[]? Values { get; }

    /// <summary>
    ///     A proven lower bound on the optimal cost: <see cref="LowerBound" /> ≤ optimum ≤
    ///     <see cref="UpperBound" />. When <see cref="Status" /> is <see cref="MaxSatStatus.Optimal" />
    ///     the two bounds coincide with <see cref="Cost" />; when the search stops early
    ///     (<see cref="MaxSatStatus.Unknown" />) they bracket the still-unknown optimum. Undefined
    ///     when no model exists (<see cref="MaxSatStatus.HardClausesUnsatisfiable" />).
    /// </summary>
    public long LowerBound { get; }

    /// <summary>
    ///     The cost of the best model found so far (the incumbent), an upper bound on the optimum.
    ///     Equals <see cref="Cost" /> whenever a model is available. Never a proven optimum unless
    ///     <see cref="Status" /> is <see cref="MaxSatStatus.Optimal" />.
    /// </summary>
    public long UpperBound { get; }

    public bool GetValue(int variable)
    {
        if (Values == null) throw new InvalidOperationException("No model available");
        return Values[variable];
    }
}

/// <summary>
///     Weighted partial MaxSAT: hard clauses must hold, soft clauses carry positive
///     weights and the total weight of falsified softs is minimized. Two algorithms are
///     available (see <see cref="MaxSatAlgorithm" />):
///     <list type="bullet">
///         <item>
///             <description>
///                 <see cref="MaxSatAlgorithm.Linear" /> — each soft clause gets a relaxation
///                 literal and a linear search tightens a pseudo-Boolean bound on the relaxation
///                 weights until UNSAT proves optimality;
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="MaxSatAlgorithm.CoreGuided" /> — an MSU3-style lower-bound search
///                 that solves under soft-selector assumptions, extracts UNSAT cores and relaxes
///                 only the cores with a cardinality / pseudo-Boolean bound raised round by round.
///             </description>
///         </item>
///     </list>
///     Built entirely on the in-house solver and encoders — no dependencies. Either algorithm,
///     run to completion, returns the same PROVEN optimum; an incumbent found under a spent
///     budget is reported as <see cref="MaxSatStatus.Unknown" />, never as Optimal.
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

    /// <summary>
    ///     Solve with the linear search (unchanged since the first release). Equivalent to
    ///     <see cref="Solve(MaxSatAlgorithm, int, System.Threading.CancellationToken)" /> with
    ///     <see cref="MaxSatAlgorithm.Linear" />.
    /// </summary>
    public MaxSatResult Solve(int maxConflictsPerCall = 1_000_000, CancellationToken cancellationToken = default)
    {
        return SolveLinear(maxConflictsPerCall, cancellationToken);
    }

    /// <summary>
    ///     Solve with the requested <paramref name="algorithm" />. The parameterless
    ///     <see cref="Solve(int, System.Threading.CancellationToken)" /> is preserved and keeps
    ///     running the linear search; the core-guided path is opt-in through this overload.
    /// </summary>
    public MaxSatResult Solve(MaxSatAlgorithm algorithm, int maxConflictsPerCall = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        return algorithm switch
        {
            MaxSatAlgorithm.CoreGuided => SolveCoreGuided(maxConflictsPerCall, cancellationToken),
            _ => SolveLinear(maxConflictsPerCall, cancellationToken) // Linear and Auto
        };
    }

    // --- linear search (the stable default; behaviour unchanged) -----------

    private MaxSatResult SolveLinear(int maxConflictsPerCall, CancellationToken cancellationToken)
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
                        ? new MaxSatResult(MaxSatStatus.Unknown, long.MaxValue, null, 0, long.MaxValue)
                        : new MaxSatResult(MaxSatStatus.Unknown, bestCost, bestValues, 0, bestCost);

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

    // --- core-guided search (MSU3-style lower-bound search) ----------------

    /// <summary>
    ///     Core-guided MaxSAT. Each soft clause C_i gets a blocking variable b_i and the hard
    ///     clause (C_i ∨ b_i); minimizing Σ w_i·b_i then equals the MaxSAT cost. The search keeps
    ///     a lower bound <c>lb</c> (proven: optimum ≥ lb) and a relaxation set R of blocking
    ///     variables that have appeared in some core. Each round it solves the working formula
    ///     under the assumptions {¬b_i : b_i ∉ R} with the constraint "Σ_{R} w·b ≤ lb":
    ///     <list type="bullet">
    ///         <item><description>SAT ⇒ the assignment falsifies at most <c>lb</c> of weight; with
    ///             optimum ≥ lb it is a proven optimum.</description></item>
    ///         <item><description>UNSAT with a non-empty core ⇒ the core's blocking variables join
    ///             R (the bound is unchanged).</description></item>
    ///         <item><description>UNSAT with an empty core ⇒ even relaxing everything cannot fit in
    ///             <c>lb</c>, so optimum &gt; lb; raise <c>lb</c> by one.</description></item>
    ///     </list>
    ///     Unweighted instances (all soft weights 1) use a cardinality bound — this is textbook
    ///     MSU3. Weighted instances use a pseudo-Boolean bound raised in unit steps (a sound
    ///     weighted MSU3 variant): every solved instance yields the proven optimum, and instances
    ///     whose optimum is large relative to the conflict budget defer to
    ///     <see cref="MaxSatStatus.Unknown" /> with a sound incumbent rather than a wrong number.
    /// </summary>
    private MaxSatResult SolveCoreGuided(int maxConflictsPerCall, CancellationToken cancellationToken)
    {
        var softCount = _softClauses.Count;
        var totalVars = _variableCount + softCount;

        // Blocking variable for soft clause i sits right after the original variables.
        var blockingOf = new int[softCount];
        var weightOfBlocking = new Dictionary<int, long>(softCount);
        for (var i = 0; i < softCount; i++)
        {
            blockingOf[i] = _variableCount + 1 + i;
            weightOfBlocking[blockingOf[i]] = _softClauses[i].Weight;
        }

        // Establish hard satisfiability first, so the core-guided loop below never conflates a
        // hard-UNSAT with a cardinality-induced UNSAT, and to seed a sound incumbent.
        var hardStatus = SolveHardOnly(totalVars, maxConflictsPerCall, cancellationToken, out var incumbent);
        if (hardStatus == SatResult.Unsatisfiable)
            return new MaxSatResult(MaxSatStatus.HardClausesUnsatisfiable, long.MaxValue, null);
        if (hardStatus == SatResult.Unknown || incumbent == null)
            // Budget spent before even a feasible model: no proof, no incumbent.
            return new MaxSatResult(MaxSatStatus.Unknown, long.MaxValue, null, 0, long.MaxValue);

        var incumbentCost = CostOf(incumbent);
        if (incumbentCost == 0)
            return new MaxSatResult(MaxSatStatus.Optimal, 0, incumbent);

        var unweighted = _softClauses.All(s => s.Weight == 1);

        var relaxSet = new List<int>(); // blocking vars that have appeared in a core
        var relaxWeights = new List<long>(); // parallel weights for the pseudo-Boolean bound
        var inRelaxSet = new bool[totalVars + 1];
        var lowerBound = 0L;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var builder = new CnfBuilder(totalVars);
            foreach (var clause in _hardClauses) builder.AddClause(clause);
            for (var i = 0; i < softCount; i++)
                builder.AddClause(_softClauses[i].Clause.Append(blockingOf[i]).ToArray());

            if (relaxSet.Count > 0)
            {
                if (unweighted) CardinalityEncoder.AtMostK(builder, relaxSet, (int)lowerBound);
                else PseudoBooleanEncoder.AtMost(builder, relaxSet, relaxWeights, lowerBound);
            }

            var solver = builder.ToSolver();
            var assumptions = new List<int>(softCount);
            for (var i = 0; i < softCount; i++)
                if (!inRelaxSet[blockingOf[i]])
                    assumptions.Add(-blockingOf[i]);

            switch (solver.Solve(assumptions, maxConflictsPerCall, cancellationToken))
            {
                case SatResult.Satisfiable:
                {
                    var model = new bool[_variableCount + 1];
                    for (var v = 1; v <= _variableCount; v++) model[v] = solver.GetValue(v);
                    // The model falsifies at most `lowerBound` of weight and optimum ≥ lowerBound,
                    // so its cost is the proven optimum.
                    return new MaxSatResult(MaxSatStatus.Optimal, CostOf(model), model);
                }

                case SatResult.Unknown:
                    // Budget spent: return the sound incumbent with its bracketing bounds.
                    return new MaxSatResult(MaxSatStatus.Unknown, incumbentCost, incumbent,
                        lowerBound, incumbentCost);

                default: // Unsatisfiable
                    var core = solver.UnsatCore ?? Array.Empty<int>();
                    var grew = false;
                    foreach (var literal in core)
                    {
                        var blocking = Math.Abs(literal);
                        if (inRelaxSet[blocking]) continue;
                        inRelaxSet[blocking] = true;
                        relaxSet.Add(blocking);
                        relaxWeights.Add(weightOfBlocking[blocking]);
                        grew = true;
                    }

                    if (grew) continue; // new blocking vars joined R; keep the same bound

                    // Empty core: the bound itself is infeasible, so optimum > lowerBound.
                    lowerBound++;
                    if (lowerBound >= incumbentCost)
                        // optimum ≥ lowerBound ≥ incumbentCost ≥ optimum ⇒ the incumbent is optimal.
                        return new MaxSatResult(MaxSatStatus.Optimal, incumbentCost, incumbent);
                    continue;
            }
        }
    }

    /// <summary>Solve the hard clauses alone; returns the verdict and, on SAT, a model over 1..n.</summary>
    private SatResult SolveHardOnly(int totalVars, int maxConflicts, CancellationToken cancellationToken,
        out bool[]? model)
    {
        var solver = new SatSolver(totalVars);
        foreach (var clause in _hardClauses) solver.AddClause(clause);
        var verdict = solver.Solve(maxConflicts, cancellationToken);
        if (verdict != SatResult.Satisfiable)
        {
            model = null;
            return verdict;
        }

        model = new bool[_variableCount + 1];
        for (var v = 1; v <= _variableCount; v++) model[v] = solver.GetValue(v);
        return verdict;
    }

    /// <summary>Total weight of soft clauses falsified by <paramref name="model" /> (indexed 1..n).</summary>
    private long CostOf(bool[] model)
    {
        var cost = 0L;
        foreach (var (weight, clause) in _softClauses)
            if (!clause.Any(l => model[Math.Abs(l)] == l > 0))
                cost += weight;
        return cost;
    }
}
