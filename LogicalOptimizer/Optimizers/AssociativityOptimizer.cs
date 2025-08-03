using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for associativity laws and term flattening
/// </summary>
public class AssociativityOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplyAssociativityLaws(node);
    }

    private AstNode ApplyAssociativityLaws(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);
            terms = terms.Select(ApplyAssociativityLaws).Distinct(new NodeComparer()).ToList();

            if (terms.Count == 1) return terms[0];
            if (terms.Count == 0) return CreateTrue();

            var result = terms.Aggregate((a, b) => new AndNode(a, b));
            // NOTE: preserve ForceParentheses
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            terms = terms.Select(ApplyAssociativityLaws).Distinct(new NodeComparer()).ToList();

            if (terms.Count == 1) return terms[0];
            if (terms.Count == 0) return CreateFalse();

            var result = terms.Aggregate((a, b) => new OrNode(a, b));
            // NOTE: preserve ForceParentheses
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyAssociativityLaws(notNode.Operand));

        return node;
    }
}
