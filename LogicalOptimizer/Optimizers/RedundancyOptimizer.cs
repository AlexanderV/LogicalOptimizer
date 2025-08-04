using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for redundant terms and complex expression simplification
/// </summary>
public class RedundancyOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        node = SimplifyRedundantTerms(node);
        node = SimplifyConsensusRedundancy(node, metrics);
        return SimplifyComplexExpressions(node);
    }

    private AstNode SimplifyRedundantTerms(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);
            var simplified = new List<AstNode>(terms.Count);

            foreach (var term in terms)
            {
                var simplifiedTerm = SimplifyRedundantTerms(term);

                var absorbed = false;
                for (var i = 0; i < simplified.Count; i++)
                {
                    if (Absorbs(simplified[i], simplifiedTerm))
                    {
                        absorbed = true;
                        break;
                    }

                    if (Absorbs(simplifiedTerm, simplified[i]))
                    {
                        simplified[i] = simplifiedTerm;
                        absorbed = true;
                        break;
                    }
                }

                if (!absorbed)
                    simplified.Add(simplifiedTerm);
            }

            if (simplified.Count == 0) return CreateTrue();
            if (simplified.Count == 1) return simplified[0];

            var result = simplified.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            var simplified = new List<AstNode>(terms.Count);

            foreach (var term in terms)
            {
                var simplifiedTerm = SimplifyRedundantTerms(term);

                var absorbed = false;
                for (var i = 0; i < simplified.Count; i++)
                {
                    if (Absorbs(simplified[i], simplifiedTerm))
                    {
                        absorbed = true;
                        break;
                    }

                    if (Absorbs(simplifiedTerm, simplified[i]))
                    {
                        simplified[i] = simplifiedTerm;
                        absorbed = true;
                        break;
                    }
                }

                if (!absorbed)
                    simplified.Add(simplifiedTerm);
            }

            if (simplified.Count == 0) return CreateFalse();
            if (simplified.Count == 1) return simplified[0];

            var result = simplified.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyRedundantTerms(notNode.Operand));

        return node;
    }

    private AstNode SimplifyConsensusRedundancy(AstNode node, OptimizationMetrics? metrics = null)
    {
        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            var simplified = new List<AstNode>(terms.Count);
            var rulesApplied = 0;

            foreach (var term in terms)
            {
                var isRedundant = false;

                // Check if this term is redundant consensus
                foreach (var other in terms)
                    if (!AreEqual(term, other) && IsRedundantConsensus(term, other, terms))
                    {
                        isRedundant = true;
                        rulesApplied++;
                        break;
                    }

                if (!isRedundant) simplified.Add(term);
            }

            if (rulesApplied > 0 && metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("ConsensusSimplification", 0);
                metrics.RuleApplicationCount["ConsensusSimplification"] += rulesApplied;
                metrics.AppliedRules += rulesApplied;
            }

            if (simplified.Count == 0) return CreateFalse();
            if (simplified.Count == 1) return simplified[0];

            var result = simplified.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var left = SimplifyConsensusRedundancy(andNode.Left, metrics);
            var right = SimplifyConsensusRedundancy(andNode.Right, metrics);
            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyConsensusRedundancy(notNode.Operand, metrics));

        return node;
    }

    private bool IsRedundantConsensus(AstNode term, AstNode other, List<AstNode> allTerms)
    {
        // Term is redundant if it's consensus of two other terms in the list
        if (!(term is AndNode termAnd) || !(other is AndNode))
            return false;

        var termFactors = FlattenAnd(termAnd);

        // Look for two terms whose consensus gives current term
        for (var i = 0; i < allTerms.Count; i++)
        for (var j = i + 1; j < allTerms.Count; j++)
        {
            if (AreEqual(allTerms[i], term) || AreEqual(allTerms[j], term))
                continue;

            var consensus = FindConsensus(allTerms[i], allTerms[j]);
            if (consensus != null && AreEqual(consensus, term)) return true;
        }

        return false;
    }

    private AstNode? FindConsensus(AstNode term1, AstNode term2)
    {
        // Consensus for AND-terms: (A & B) + (!A & C) → (B & C)
        if (term1 is AndNode and1 && term2 is AndNode and2)
        {
            var factors1 = FlattenAnd(and1);
            var factors2 = FlattenAnd(and2);

            // Look for complementary pair
            for (var i = 0; i < factors1.Count; i++)
            for (var j = 0; j < factors2.Count; j++)
                if (AreComplementary(factors1[i], factors2[j]))
                {
                    // Create consensus from remaining factors more efficiently
                    var remaining = new List<AstNode>(factors1.Count + factors2.Count - 2);
                    
                    // Add remaining factors from first list
                    for (var k = 0; k < factors1.Count; k++)
                        if (k != i) remaining.Add(factors1[k]);
                    
                    // Add remaining factors from second list, avoiding duplicates
                    for (var k = 0; k < factors2.Count; k++)
                    {
                        if (k != j)
                        {
                            var factor = factors2[k];
                            bool isDuplicate = false;
                            for (var l = 0; l < remaining.Count; l++)
                            {
                                if (AreEqual(remaining[l], factor))
                                {
                                    isDuplicate = true;
                                    break;
                                }
                            }
                            if (!isDuplicate) remaining.Add(factor);
                        }
                    }

                    // Check if the consensus term contains contradictions (always false)
                    if (ContainsContradiction(remaining)) return null;

                    if (remaining.Count == 0) return CreateTrue();
                    if (remaining.Count == 1) return remaining[0];
                    return remaining.Aggregate((a, b) => new AndNode(a, b));
                }
        }

        // Simple variables: A + !A → 1 (tautology)
        if (AreComplementary(term1, term2)) return CreateTrue();

        return null;
    }

    private AstNode SimplifyComplexExpressions(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = SimplifyComplexExpressions(andNode.Left);
            var right = SimplifyComplexExpressions(andNode.Right);

            if (left is AndNode leftAndNode && right is AndNode rightAndNode)
                if (AreEqual(leftAndNode.Left, rightAndNode.Left))
                {
                    var result = new AndNode(leftAndNode.Left,
                        SimplifyComplexExpressions(new AndNode(leftAndNode.Right, rightAndNode.Right)));
                    result.ForceParentheses = andNode.ForceParentheses;
                    return result;
                }

            var resultFinal = new AndNode(left, right);
            resultFinal.ForceParentheses = andNode.ForceParentheses;
            return resultFinal;
        }

        if (node is OrNode orNode)
        {
            var left = SimplifyComplexExpressions(orNode.Left);
            var right = SimplifyComplexExpressions(orNode.Right);

            if (left is OrNode leftOrNode && right is OrNode rightOrNode)
                if (AreEqual(leftOrNode.Left, rightOrNode.Left))
                {
                    var result = new OrNode(leftOrNode.Left,
                        SimplifyComplexExpressions(new OrNode(leftOrNode.Right, rightOrNode.Right)));
                    result.ForceParentheses = orNode.ForceParentheses;
                    return result;
                }

            var resultFinal = new OrNode(left, right);
            resultFinal.ForceParentheses = orNode.ForceParentheses;
            return resultFinal;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyComplexExpressions(notNode.Operand));

        return node;
    }
}
