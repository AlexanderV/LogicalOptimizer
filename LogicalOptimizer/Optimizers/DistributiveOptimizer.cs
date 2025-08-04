using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for distributive laws: a & (b | c) = a & b | a & c, a | (b & c) = (a | b) & (a | c)
/// </summary>
public class DistributiveOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        var original = node;
        var result = ApplyDistributiveInternal(node);

        if (metrics != null && !AreEqual(original, result))
        {
            metrics.RuleApplicationCount.TryAdd("Distributive", 0);
            metrics.RuleApplicationCount["Distributive"]++;
            metrics.AppliedRules++;
        }

        return result;
    }

    private AstNode ApplyDistributiveInternal(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = ApplyDistributiveInternal(andNode.Left);
            var right = ApplyDistributiveInternal(andNode.Right);

            // Apply distributive law: a & (b | c) = a & b | a & c
            if (right is OrNode rightOr)
            {
                var leftTerm = left;
                var rightLeft = rightOr.Left;
                var rightRight = rightOr.Right;
                
                var term1 = new AndNode(leftTerm, rightLeft);
                var term2 = new AndNode(leftTerm, rightRight);
                return new OrNode(term1, term2);
            }
            
            if (left is OrNode leftOr)
            {
                var rightTerm = right;
                var leftLeft = leftOr.Left;
                var leftRight = leftOr.Right;
                
                var term1 = new AndNode(leftLeft, rightTerm);
                var term2 = new AndNode(leftRight, rightTerm);
                return new OrNode(term1, term2);
            }

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is OrNode orNode)
        {
            var left = ApplyDistributiveInternal(orNode.Left);
            var right = ApplyDistributiveInternal(orNode.Right);

            // Apply distributive law: a | (b & c) = (a | b) & (a | c)
            if (right is AndNode rightAnd)
            {
                var leftTerm = left;
                var rightLeft = rightAnd.Left;
                var rightRight = rightAnd.Right;
                
                var term1 = new OrNode(leftTerm, rightLeft);
                var term2 = new OrNode(leftTerm, rightRight);
                return new AndNode(term1, term2);
            }
            
            if (left is AndNode leftAnd)
            {
                var rightTerm = right;
                var leftLeft = leftAnd.Left;
                var leftRight = leftAnd.Right;
                
                var term1 = new OrNode(leftLeft, rightTerm);
                var term2 = new OrNode(leftRight, rightTerm);
                return new AndNode(term1, term2);
            }

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
        {
            return new NotNode(ApplyDistributiveInternal(notNode.Operand));
        }

        return node;
    }
}
