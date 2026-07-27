using System.Collections.Generic;
using static LogicalOptimizer.Rewrite.AstPrimitives;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Consensus-redundancy elimination. In a disjunction, a term that is the consensus of
/// two other present terms is redundant; dually, in a conjunction a clause that is the
/// resolvent of two other present clauses is redundant. The consensus theorem only
/// licenses dropping a term while BOTH of its parents are still present, so removal is
/// one-at-a-time, re-checking against the CURRENT list after every removal — removing
/// all candidates in one pass would let two terms justify each other's removal and
/// lose minterms.
/// </summary>
internal sealed class RedundancyRule : IRewriteRule
{
    public string Name => "ConsensusSimplification";

    public bool GuardAgainstGrowth => false;

    public AstNode? TryRewrite(AstNode node, FormulaFactory factory)
    {
        switch (node)
        {
            case OrNode or:
                {
                    var terms = new List<AstNode>(or.Operands);
                    if (!RemoveRedundant(terms, factory, isTermSide: true)) return null;
                    var result = factory.Or(terms);
                    return result.Equals(node) ? null : result;
                }
            case AndNode and:
                {
                    var clauses = new List<AstNode>(and.Operands);
                    if (!RemoveRedundant(clauses, factory, isTermSide: false)) return null;
                    var result = factory.And(clauses);
                    return result.Equals(node) ? null : result;
                }
            default:
                return null;
        }
    }

    private static bool RemoveRedundant(List<AstNode> items, FormulaFactory factory, bool isTermSide)
    {
        var removedAny = false;
        var removedSomething = true;
        while (removedSomething && items.Count > 1)
        {
            removedSomething = false;

            for (var i = 0; i < items.Count; i++)
            {
                if (isTermSide ? items[i] is not AndNode : items[i] is not OrNode) continue;
                if (!IsDerivedFromOthers(i, items, factory, isTermSide)) continue;

                items.RemoveAt(i);
                removedAny = true;
                removedSomething = true;
                break;
            }
        }

        return removedAny;
    }

    private static bool IsDerivedFromOthers(int index, List<AstNode> items, FormulaFactory factory, bool isTermSide)
    {
        var candidate = items[index];

        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
            {
                if (i == index || j == index)
                    continue;

                var derived = isTermSide
                    ? FindConsensus(items[i], items[j], factory)
                    : FindResolvent(items[i], items[j], factory);
                if (derived == null) continue;

                var matches = isTermSide
                    ? SameTermSet(derived, candidate)
                    : SameClauseSet(derived, candidate);
                if (matches) return true;
            }

        return false;
    }
}
