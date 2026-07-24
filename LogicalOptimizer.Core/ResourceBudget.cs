namespace LogicalOptimizer;

/// <summary>
///     Unified work budgets for the potentially expensive engines. Every limit has a safe
///     default; callers with harder latency requirements can tighten them per call via
///     <c>OptimizationOptions.Budget</c>. Exhausting a budget never produces a wrong
///     result — each engine falls back (heuristic simplification, Unknown verdict) or throws
///     a documented exception.
///     <para>
///         Scale contract of the fixed limits: expressions accept up to 100 variables
///         overall; truth-table-based operations (TruthTable, exact minimization input)
///         cap at 20; exact minimization is guaranteed ≤10, budgeted ≤12; the SAT-based
///         prime cover extends to 24; SAT/BDD/Tseitin engines have no variable cap, only
///         these work budgets.
///     </para>
/// </summary>
public sealed class ResourceBudget
{
    /// <summary>Default branch-and-bound step limit for the minimum-cover search.</summary>
    public const int DefaultCoverStepLimit = 200_000;

    /// <summary>Cube-pair comparisons for Quine–McCluskey prime generation (11-12 variable attempts).</summary>
    public long QmPairComparisonLimit { get; init; } = PerformanceValidator.QM_PAIR_COMPARISON_LIMIT;

    /// <summary>Branch-and-bound steps for the minimum-cover search outside the guarantee zone.</summary>
    public int CoverStepLimit { get; init; } = DefaultCoverStepLimit;

    /// <summary>
    ///     SAT conflicts for one explicit equivalence query (mirrors
    ///     <c>EquivalenceChecker.DefaultMaxConflicts</c>, which asserts the match in tests).
    /// </summary>
    public int SatConflictLimit { get; init; } = 200_000;

    /// <summary>SAT conflicts for the per-call soundness guard beyond the truth-table range.</summary>
    public int SoundnessGuardConflictLimit { get; init; } = PerformanceValidator.SOUNDNESS_GUARD_MAX_CONFLICTS;

    /// <summary>
    ///     Node budget for BDD construction (mirrors
    ///     <c>BinaryDecisionDiagram.DefaultNodeBudget</c>, asserted in tests).
    /// </summary>
    public int BddNodeLimit { get; init; } = 1_000_000;

    public static ResourceBudget Default { get; } = new();
}
