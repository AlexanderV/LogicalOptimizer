using System.Text;

namespace LogicalOptimizer;

public class OptimizationMetrics
{
    public int OriginalNodes { get; set; }
    public int OptimizedNodes { get; set; }
    public int Iterations { get; set; }
    public int AppliedRules { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public Dictionary<string, int> RuleApplicationCount { get; set; } = new();
    public List<string> OptimizationSteps { get; set; } = new();

    public double CompressionRatio => OriginalNodes > 0 ? (double) OptimizedNodes / OriginalNodes : 1.0;
    public bool IsImproved => OptimizedNodes < OriginalNodes;

    public void AddStep(string step)
    {
        OptimizationSteps.Add(step);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Optimization Metrics ===");
        sb.AppendLine($"Original nodes: {OriginalNodes}");
        sb.AppendLine($"Optimized nodes: {OptimizedNodes}");
        sb.AppendLine($"Compression ratio: {CompressionRatio:P1}");
        sb.AppendLine($"Iterations: {Iterations}");
        sb.AppendLine($"Applied rules: {AppliedRules}");
        sb.AppendLine($"Elapsed time: {ElapsedTime.TotalMilliseconds:F2}ms");

        if (RuleApplicationCount.Any())
        {
            sb.AppendLine("Rule applications:");
            foreach (var kvp in RuleApplicationCount.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }

        return sb.ToString();
    }
}

public static class AstMetrics
{
    public static int CountNodes(AstNode node)
    {
        return node switch
        {
            BinaryNode binary => 1 + CountNodes(binary.Left) + CountNodes(binary.Right),
            NotNode not => 1 + CountNodes(not.Operand),
            _ => 1
        };
    }

    public static int GetDepth(AstNode node)
    {
        return node switch
        {
            BinaryNode binary => 1 + Math.Max(GetDepth(binary.Left), GetDepth(binary.Right)),
            NotNode not => 1 + GetDepth(not.Operand),
            _ => 1
        };
    }

    public static int CountOperators(AstNode node)
    {
        return node switch
        {
            BinaryNode binary => 1 + CountOperators(binary.Left) + CountOperators(binary.Right),
            NotNode not => 1 + CountOperators(not.Operand),
            _ => 0
        };
    }
}