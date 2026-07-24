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
    ///     budget exhaustion in the exact path is visible here.
    /// </summary>
    public MinimizationStatus MinimizationStatus { get; set; } = MinimizationStatus.Heuristic;
    public string Advanced { get; set; } = "";

    /// <summary>Human-readable debug dump (AST trees + metrics); empty unless requested.</summary>
    public string DebugInfo { get; set; } = "";

    public List<string> Variables { get; set; } = new();
    public OptimizationMetrics? Metrics { get; set; }
    public TruthTable? OriginalTruthTable { get; set; }
    public TruthTable? OptimizedTruthTable { get; set; }

    /// <summary>
    ///     Checks equivalence of original and optimized expressions through truth tables
    /// </summary>
    public bool IsEquivalent()
    {
        if (OriginalTruthTable == null || OptimizedTruthTable == null)
            return TruthTable.AreEquivalent(Original, Optimized);

        return TruthTable.AreEquivalent(OriginalTruthTable, OptimizedTruthTable);
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
