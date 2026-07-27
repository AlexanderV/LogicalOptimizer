using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Rewrite.AstPrimitives;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Consensus rule over an OR node's term list: (A &amp; B) | (!A &amp; C) may gain the
/// consensus term (B &amp; C). Local acceptance criterion: the added consensus terms must
/// pay for themselves at THIS node by enabling absorption; otherwise the
/// absorption-only baseline is kept (whole-tree rollback vetoed improvements in one
/// subtree because of growth in another).
/// </summary>
internal sealed class ConsensusRule : IRewriteRule
{
    public string Name => "Consensus";

    public bool GuardAgainstGrowth => false;

    public AstNode? TryRewrite(AstNode node, FormulaFactory factory)
    {
        if (node is not OrNode or) return null;

        var terms = or.Operands;
        var consensusTerms = new List<AstNode>();

        // Look for pairs of terms to apply consensus
        for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
            {
                var consensus = FindConsensus(terms[i], terms[j], factory);
                if (consensus != null && !ContainsTerm(terms, consensus)) consensusTerms.Add(consensus);
            }

        var baseline = factory.Or(RemoveAbsorbedTerms(terms));
        if (consensusTerms.Count == 0) return baseline.Equals(node) ? null : baseline;

        var enrichedTerms = new List<AstNode>(terms);
        enrichedTerms.AddRange(consensusTerms);
        var enriched = factory.Or(RemoveAbsorbedTerms(enrichedTerms));

        var accepted = AstMetrics.CountNodes(enriched) < AstMetrics.CountNodes(baseline) ? enriched : baseline;
        return accepted.Equals(node) ? null : accepted;
    }

    private static bool ContainsTerm(IReadOnlyList<AstNode> terms, AstNode term)
    {
        return terms.Any(t => SameTermSet(t, term));
    }
}
