using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for factorization: a & b | a & c = a & (b | c), (a | b) & (a | c) = a | (b & c)
/// </summary>
public class FactorizationOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        var original = node;
        var result = ApplyFactorizationInternal(node);

        if (metrics != null && !AreEqual(original, result))
        {
            metrics.RuleApplicationCount.TryAdd("Factorization", 0);
            metrics.RuleApplicationCount["Factorization"]++;
            metrics.AppliedRules++;
        }

        return result;
    }

    private AstNode ApplyFactorizationInternal(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var left = ApplyFactorizationInternal(orNode.Left);
            var right = ApplyFactorizationInternal(orNode.Right);

            // Simple factorization: a & b | a & c = a & (b | c)
            var terms = FlattenOr(new OrNode(left, right));
            var factorized = FactorizeTerms(terms);
            if (factorized != null) return ApplyFactorizationInternal(factorized);

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is AndNode andNode)
        {
            var left = ApplyFactorizationInternal(andNode.Left);
            var right = ApplyFactorizationInternal(andNode.Right);

            // Reverse factorization: (a | b) & (a | c) = a | (b & c)
            if (left is OrNode leftOr && right is OrNode rightOr)
            {
                // Check that all 4 variables are comparable
                if (AreEqual(leftOr.Left, rightOr.Left))
                {
                    var common = leftOr.Left;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Right, rightOr.Right, true);
                    return new OrNode(common, ApplyFactorizationInternal(remaining));
                }

                if (AreEqual(leftOr.Left, rightOr.Right))
                {
                    var common = leftOr.Left;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Right, rightOr.Left, true);
                    return new OrNode(common, ApplyFactorizationInternal(remaining));
                }

                if (AreEqual(leftOr.Right, rightOr.Left))
                {
                    var common = leftOr.Right;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Left, rightOr.Right, true);
                    return new OrNode(common, ApplyFactorizationInternal(remaining));
                }

                if (AreEqual(leftOr.Right, rightOr.Right))
                {
                    var common = leftOr.Right;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Left, rightOr.Left, true);
                    return new OrNode(common, ApplyFactorizationInternal(remaining));
                }
            }

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode) return new NotNode(ApplyFactorizationInternal(notNode.Operand));

        return node;
    }

    private AstNode? FactorizeTerms(List<AstNode> terms)
    {
        if (terms.Count < 2) return null;

        // Look for AND-terms that have common factors
        for (var i = 0; i < terms.Count; i++)
        {
            var currentTerm = terms[i];

            if (currentTerm is AndNode currentAnd)
            {
                var factors = FlattenAnd(currentAnd);

                foreach (var factor in factors)
                {
                    // Check if this factor is contained in all terms
                    var containsInAll = terms.All(term => ContainsFactor(term, factor));

                    if (containsInAll)
                    {
                        // Factor out common factor
                        var remainingTerms = terms
                            .Select(term => RemoveFactor(term, factor))
                            .Where(remaining => !IsTrue(remaining))
                            .ToList();

                        if (remainingTerms.Count == 0) return factor;

                        var remaining = remainingTerms.Count == 1
                            ? remainingTerms[0]
                            : remainingTerms.Aggregate((a, b) => new OrNode(a, b));

                        // Force parentheses: create AND with forced parentheses for OR part
                        AstNode result;
                        if (remaining is OrNode)
                            // Don't create new OR with forced parentheses - they will be added automatically
                            result = new AndNode(factor, remaining);
                        else
                            result = new AndNode(factor, remaining);

                        return result;
                    }
                }
            }
        }

        return null;
    }

    private bool ContainsFactor(AstNode term, AstNode factor)
    {
        if (AreEqual(term, factor))
            return true;

        if (term is AndNode andTerm)
        {
            var factors = FlattenAnd(andTerm);
            return factors.Any(f => AreEqual(f, factor));
        }

        return false;
    }

    private AstNode RemoveFactor(AstNode term, AstNode factor)
    {
        if (AreEqual(term, factor))
            return CreateTrue(); // a / a = 1

        if (term is AndNode andTerm)
        {
            var factors = FlattenAnd(andTerm)
                .Where(f => !AreEqual(f, factor))
                .ToList();

            if (factors.Count == 0)
                return CreateTrue();
            if (factors.Count == 1)
                return factors[0];

            return factors.Aggregate((a, b) => new AndNode(a, b));
        }

        return term; // No factor found, return original term
    }
}
