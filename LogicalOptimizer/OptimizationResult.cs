namespace LogicalOptimizer;

public class OptimizationResult
{
    public string Original { get; set; }
    public string Optimized { get; set; }
    public string CNF { get; set; }
    public string DNF { get; set; }
    public string Advanced { get; set; } = "";
    public List<string> Variables { get; set; }
    public OptimizationMetrics? Metrics { get; set; }
    public TruthTable? OriginalTruthTable { get; set; }
    public TruthTable? OptimizedTruthTable { get; set; }
    public CompiledTruthTable? CompiledOriginalTruthTable { get; set; }
    public CompiledTruthTable? CompiledOptimizedTruthTable { get; set; }

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