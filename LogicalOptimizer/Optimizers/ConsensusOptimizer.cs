using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for consensus rule: (A & B) | (!A & C) → (A & B) | (!A & C) | (B & C)
/// </summary>
public class ConsensusOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplyConsensusRule(node, metrics);
    }

    private AstNode ApplyConsensusRule(AstNode node, OptimizationMetrics? metrics = null)
    {
        if (node is OrNode orNode)
        {
            // Consensus rule for OR: (A & B) | (!A & C) → (A & B) | (!A & C) | (B & C)
            var terms = FlattenOr(orNode);
            var consensusTerms = new List<AstNode>();

            // Look for pairs of terms to apply consensus
            for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
            {
                var consensus = FindConsensus(terms[i], terms[j]);
                if (consensus != null && !ContainsTerm(terms, consensus)) consensusTerms.Add(consensus);
            }

            // If consensus terms found, record metrics
            if (consensusTerms.Count > 0 && metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("Consensus", 0);
                metrics.RuleApplicationCount["Consensus"] += consensusTerms.Count;
                metrics.AppliedRules += consensusTerms.Count;
            }

            // Add consensus terms to original ones
            var allTerms = new List<AstNode>(terms);
            allTerms.AddRange(consensusTerms);

            // Recursively apply to each term
            allTerms = allTerms.Select(t => ApplyConsensusRule(t, metrics)).ToList();

            // Remove absorbed terms
            allTerms = RemoveAbsorbedTerms(allTerms);

            if (allTerms.Count == 0) return CreateFalse();
            if (allTerms.Count == 1) return allTerms[0];

            var result = allTerms.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var left = ApplyConsensusRule(andNode.Left, metrics);
            var right = ApplyConsensusRule(andNode.Right, metrics);
            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyConsensusRule(notNode.Operand, metrics));

        return node;
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
                    // Create consensus from remaining factors
                    var remaining1 = factors1.Where((_, idx) => idx != i).ToList();
                    var remaining2 = factors2.Where((_, idx) => idx != j).ToList();

                    var allRemaining = remaining1.Concat(remaining2).Distinct(new NodeComparer()).ToList();

                    // Check if the consensus term contains contradictions (always false)
                    if (ContainsContradiction(allRemaining)) return null;

                    if (allRemaining.Count == 0) return CreateTrue();
                    if (allRemaining.Count == 1) return allRemaining[0];
                    return allRemaining.Aggregate((a, b) => new AndNode(a, b));
                }
        }

        // Simple variables: A + !A → 1 (tautology)
        if (AreComplementary(term1, term2)) return CreateTrue();

        return null;
    }

    private bool ContainsTerm(List<AstNode> terms, AstNode term)
    {
        return terms.Any(t => AreEqual(t, term));
    }
}
