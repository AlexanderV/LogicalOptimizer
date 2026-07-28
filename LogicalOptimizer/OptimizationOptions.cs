namespace LogicalOptimizer;

/// <summary>
///     Which artifacts <see cref="BooleanExpressionOptimizer.OptimizeExpression(string, OptimizationOptions)" />
///     should produce. The optimized expression itself is always computed; everything
///     else is opt-in so callers do not pay for normal forms they never read.
/// </summary>
public sealed class OptimizationOptions
{
    public bool ComputeCnf { get; init; } = true;
    public bool ComputeDnf { get; init; } = true;
    public CnfMode CnfMode { get; init; } = CnfMode.Equivalent;
    public bool ComputeAdvancedForms { get; init; } = true;
    public bool IncludeMetrics { get; init; }
    public bool IncludeTruthTables { get; init; }
    public bool IncludeDebugInfo { get; init; }

    /// <summary>
    ///     Multi-level, DAG-aware structural rewriting of the optimized expression via the
    ///     internal And-Inverter Graph (cut-based ABC-style rewrite). The optimizer produces
    ///     one extra candidate — the AIG-rewritten form — and adopts it only if it is verified
    ///     equivalent to the input and strictly cheaper than the current best; it can never make
    ///     the result worse. On by default since v3.0, so the default optimizer output may be a
    ///     smaller multi-level form than the two-level/multi-level result produced before v3.0.
    ///     Set it to <c>false</c> to restore the exact pre-3.0 behavior; results stay
    ///     equivalence-verified either way.
    /// </summary>
    public bool EnableAigRewriting { get; init; } = true;

    /// <summary>Work budgets for the expensive engines (exact minimizer, SAT guard).</summary>
    public ResourceBudget Budget { get; init; } = ResourceBudget.Default;

    /// <summary>
    ///     Cooperative cancellation for the whole call; observed between optimization
    ///     iterations and inside the exact/SAT engines. Throws OperationCanceledException.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    public static OptimizationOptions Default { get; } = new();

    public static OptimizationOptions Everything { get; } = new()
    {
        IncludeMetrics = true,
        IncludeTruthTables = true,
        IncludeDebugInfo = true
    };
}

/// <summary>How the CNF artifact is produced.</summary>
public enum CnfMode
{
    /// <summary>
    ///     Equivalent CNF over the original variables. In the exact-minimization range it is a
    ///     minimum-cover POS whose proof status is reported by
    ///     <see cref="OptimizationResult.CnfMinimizationStatus" /> (proven in the ≤10 guarantee
    ///     zone, possibly budget-limited at 11–12); beyond the threshold it is budgeted
    ///     distribution and may end as <see cref="ComputationStatus.TooLarge" />.
    /// </summary>
    Equivalent,

    /// <summary>
    ///     Equisatisfiable CNF via Tseitin transformation: always linear in expression size,
    ///     introduces auxiliary _tN variables. Never <see cref="ComputationStatus.TooLarge" />.
    /// </summary>
    Tseitin
}

/// <summary>
///     Provenance of the minimality claim for an optimization result. Refers to the
///     two-level cover cost model: total literals first, then term count. The returned
///     optimized (multi-level) expression never has more literals than that cover.
/// </summary>
public enum MinimizationStatus
{
    /// <summary>The minimum cover search completed: the result is provably minimal.</summary>
    MinimalProven,

    /// <summary>
    ///     Exact search ran but exceeded its work budget: the result is sound and usually
    ///     minimal, but optimality was not proven.
    /// </summary>
    BudgetExceeded,

    /// <summary>Outside the exact range (or the exact attempt fell back): rule-based simplification only.</summary>
    Heuristic
}

/// <summary>Outcome of a potentially expensive computation on the result object.</summary>
public enum ComputationStatus
{
    Computed,
    TooLarge,
    NotRequested
}
