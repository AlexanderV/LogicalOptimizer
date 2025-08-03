using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for smart commutativity - rearranges terms for better factorization
/// </summary>
public class CommutativityOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplySmartCommutivity(node);
    }

    private AstNode ApplySmartCommutivity(AstNode node)
    {
        // Smart term rearrangement for better factorization
        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);

            // Sort terms for better factorization
            var sortedTerms = terms
                .Select(ApplySmartCommutivity)
                .OrderBy(GetComplexityScore)
                .ThenBy(t => t.ToString())
                .ToList();

            if (sortedTerms.Count == 1) return sortedTerms[0];
            if (sortedTerms.Count == 0) return CreateFalse();

            var result = sortedTerms.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);

            // Sort terms for better factorization  
            var sortedTerms = terms
                .Select(ApplySmartCommutivity)
                .OrderBy(GetComplexityScore)
                .ThenBy(t => t.ToString())
                .ToList();

            if (sortedTerms.Count == 1) return sortedTerms[0];
            if (sortedTerms.Count == 0) return CreateTrue();

            var result = sortedTerms.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplySmartCommutivity(notNode.Operand));

        return node;
    }

    private int GetComplexityScore(AstNode node)
    {
        // Simple variables have the lowest score
        if (node is VariableNode) return 1;

        // Negations are slightly more complex
        if (node is NotNode) return 2;

        // Binary operations are more complex
        if (node is AndNode andNode)
            return 3 + GetComplexityScore(andNode.Left) + GetComplexityScore(andNode.Right);

        if (node is OrNode orNode)
            return 3 + GetComplexityScore(orNode.Left) + GetComplexityScore(orNode.Right);

        return 100; // Unknown nodes go to the end
    }
}
