using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for consensus rule: (A & B) | (!A & C) → (A & B) | (!A & C) | (B & C)
/// </summary>
internal class ConsensusOptimizer : IOptimizer
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
            var terms = FlattenOr(orNode).Select(t => ApplyConsensusRule(t, metrics)).ToList();
            var consensusTerms = new List<AstNode>();

            // Look for pairs of terms to apply consensus
            for (var i = 0; i < terms.Count; i++)
                for (var j = i + 1; j < terms.Count; j++)
                {
                    var consensus = FindConsensus(terms[i], terms[j]);
                    if (consensus != null && !ContainsTerm(terms, consensus)) consensusTerms.Add(consensus);
                }

            // Local acceptance: added consensus terms must pay for themselves at THIS
            // node by enabling absorption; otherwise keep the absorption-only baseline.
            // (Replaces the old whole-tree rollback, which vetoed improvements in one
            // subtree because of growth in another.)
            var baseline = BuildOr(RemoveAbsorbedTerms(terms), orNode.ForceParentheses);
            if (consensusTerms.Count == 0) return baseline;

            var enrichedTerms = new List<AstNode>(terms);
            enrichedTerms.AddRange(consensusTerms);
            var enriched = BuildOr(RemoveAbsorbedTerms(enrichedTerms), orNode.ForceParentheses);

            if (AstMetrics.CountNodes(enriched) >= AstMetrics.CountNodes(baseline)) return baseline;

            if (metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("Consensus", 0);
                metrics.RuleApplicationCount["Consensus"] += consensusTerms.Count;
                metrics.AppliedRules += consensusTerms.Count;
            }

            return enriched;
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

    private static AstNode BuildOr(List<AstNode> terms, bool forceParens)
    {
        if (terms.Count == 0) return CreateFalse();
        if (terms.Count == 1) return terms[0];

        var result = terms.Aggregate((a, b) => new OrNode(a, b));
        if (result is OrNode resultOr && forceParens) resultOr.ForceParentheses = true;
        return result;
    }

    private bool ContainsTerm(List<AstNode> terms, AstNode term)
    {
        return terms.Any(t => SameTermSet(t, term));
    }
}
