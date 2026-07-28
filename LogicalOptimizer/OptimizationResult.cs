namespace LogicalOptimizer;

public class OptimizationResult
{
    public string Original { get; set; } = "";
    public string Optimized { get; set; } = "";
    public string CNF { get; set; } = "";
    public string DNF { get; set; } = "";
    public ComputationStatus CnfStatus { get; set; } = ComputationStatus.Computed;
    public ComputationStatus DnfStatus { get; set; } = ComputationStatus.Computed;

    /// <summary>
    ///     Whether the result's minimality is proven (exact minimum-cover search completed),
    ///     unproven due to a budget, or heuristic-only. Never silently downgraded: any
    ///     budget exhaustion in the exact path is visible here. Scope: the SOP / two-level
    ///     minimum cover behind <see cref="Optimized" /> and <see cref="DNF" />; the
    ///     equivalent-CNF (POS) artifact has its own <see cref="CnfMinimizationStatus" />.
    /// </summary>
    public MinimizationStatus MinimizationStatus { get; set; } = MinimizationStatus.Heuristic;

    /// <summary>
    ///     Minimality provenance of the equivalent-CNF (<see cref="CnfMode.Equivalent" />)
    ///     artifact specifically, reported separately from the SOP-scoped
    ///     <see cref="MinimizationStatus" /> because the POS cover search can hit its budget
    ///     independently of the SOP one. <c>MinimalProven</c> only when the POS minimum-cover
    ///     search completed; <c>BudgetExceeded</c> when it hit the step limit; <c>Heuristic</c>
    ///     when no exact equivalent CNF was produced (heuristic zone, Tseitin mode, not
    ///     requested, or TooLarge). It never claims a minimality the CNF does not have.
    /// </summary>
    public MinimizationStatus CnfMinimizationStatus { get; set; } = MinimizationStatus.Heuristic;
    public string Advanced { get; set; } = "";

    /// <summary>Human-readable debug dump (AST trees + metrics); empty unless requested.</summary>
    public string DebugInfo { get; set; } = "";

    public List<string> Variables { get; set; } = new();
    public OptimizationMetrics? Metrics { get; set; }
    public TruthTable? OriginalTruthTable { get; set; }
    public TruthTable? OptimizedTruthTable { get; set; }

    /// <summary>
    ///     Checks equivalence of the original and optimized expressions. When both truth
    ///     tables are already materialized (small expressions) they are compared directly;
    ///     otherwise the scalable <see cref="EquivalenceChecker" /> is used — truth table up
    ///     to 12 variables, SAT-miter beyond it — so this self-check works across the whole
    ///     facade range (up to 100 variables) instead of throwing past the 20-variable
    ///     truth-table limit. Returns true only when equivalence is positively proven; a SAT
    ///     Unknown (budget exhausted) yields false rather than a false positive.
    /// </summary>
    public bool IsEquivalent()
    {
        if (OriginalTruthTable != null && OptimizedTruthTable != null)
            return TruthTable.AreEquivalent(OriginalTruthTable, OptimizedTruthTable);

        return EquivalenceChecker.Check(Original, Optimized).AreEquivalent == true;
    }

    /// <summary>
    ///     Three-valued equivalence self-check for callers that must tell a refutation apart
    ///     from a budget-exhausted verdict: <c>AreEquivalent</c> is <c>true</c> (proven
    ///     equivalent), <c>false</c> (a concrete counterexample exists) or <c>null</c>
    ///     (<c>Unknown</c> — SAT budget ran out). <see cref="IsEquivalent" /> collapses the
    ///     latter two into <c>false</c>; this method preserves the distinction.
    /// </summary>
    public EquivalenceCheckResult CheckEquivalence()
    {
        return EquivalenceChecker.Check(Original, Optimized);
    }

    public override string ToString()
    {
        var result =
            $"Original: {Original}\nOptimized: {Optimized}\nCNF: {CNF}\nDNF: {DNF}\nVariables: [{string.Join(", ", Variables)}]";

        if (OriginalTruthTable != null && OptimizedTruthTable != null)
        {
            result += $"\nEquivalent: {IsEquivalent()}";
            result += $"\n\nOriginal Truth Table:\n{OriginalTruthTable}";
            result += $"\nOptimized Truth Table:\n{OptimizedTruthTable}";
        }

        return result;
    }
}
