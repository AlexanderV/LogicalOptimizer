using System.Globalization;
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

    /// <summary>
    ///     Convergence trace: one entry per rewrite fixpoint iteration recording the node
    ///     count at that step, so callers can see how the expression converged (populated
    ///     only when metrics are requested).
    /// </summary>
    public List<string> OptimizationSteps { get; set; } = new();

    /// <summary>
    ///     Bytes allocated on the calling thread across the whole optimization run
    ///     (measured via <see cref="GC.GetAllocatedBytesForCurrentThread" />). A memory-cost
    ///     signal for the call; 0 when metrics were not requested.
    /// </summary>
    public long AllocatedBytes { get; set; }

    public double CompressionRatio => OriginalNodes > 0 ? (double)OptimizedNodes / OriginalNodes : 1.0;
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
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Compression ratio: {CompressionRatio * 100:F1}%"));
        sb.AppendLine($"Iterations: {Iterations}");
        sb.AppendLine($"Applied rules: {AppliedRules}");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Elapsed time: {ElapsedTime.TotalMilliseconds:F2}ms"));

        if (RuleApplicationCount.Any())
        {
            sb.AppendLine("Rule applications:");
            foreach (var kvp in RuleApplicationCount.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }

        return sb.ToString();
    }
}

/// <summary>
///     Size and shape metrics over expression trees. Cost model: an n-ary And/Or node
///     counts as ONE node (and one operator) regardless of its operand count — the flat
///     list is a single connective, not a chain of binary ones.
/// </summary>
public static class AstMetrics
{
    /// <summary>Total node count; an n-ary node counts as 1 plus its operands.</summary>
    public static int CountNodes(AstNode node)
    {
        return node switch
        {
            NaryNode nary => 1 + nary.Operands.Sum(CountNodes),
            BinaryNode binary => 1 + CountNodes(binary.Left) + CountNodes(binary.Right),
            NotNode not => 1 + CountNodes(not.Operand),
            _ => 1
        };
    }

    /// <summary>Number of variable occurrences (constants excluded)</summary>
    public static int CountLiterals(AstNode node)
    {
        return node switch
        {
            VariableNode => 1,
            NotNode not => CountLiterals(not.Operand),
            NaryNode nary => nary.Operands.Sum(CountLiterals),
            BinaryNode binary => CountLiterals(binary.Left) + CountLiterals(binary.Right),
            _ => 0
        };
    }

    /// <summary>Tree depth; an n-ary node contributes one level over its deepest operand.</summary>
    public static int GetDepth(AstNode node)
    {
        return node switch
        {
            NaryNode nary => 1 + nary.Operands.Max(GetDepth),
            BinaryNode binary => 1 + Math.Max(GetDepth(binary.Left), GetDepth(binary.Right)),
            NotNode not => 1 + GetDepth(not.Operand),
            _ => 1
        };
    }

    /// <summary>Operator count; an n-ary connective counts as one operator.</summary>
    public static int CountOperators(AstNode node)
    {
        return node switch
        {
            NaryNode nary => 1 + nary.Operands.Sum(CountOperators),
            BinaryNode binary => 1 + CountOperators(binary.Left) + CountOperators(binary.Right),
            NotNode not => 1 + CountOperators(not.Operand),
            _ => 0
        };
    }
}
