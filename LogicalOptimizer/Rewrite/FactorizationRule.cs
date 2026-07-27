using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Rewrite.AstPrimitives;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Factorization over operand lists: a &amp; b | a &amp; c → a &amp; (b | c) (a factor
/// shared by every term, else by the largest subgroup) and the dual reverse
/// factorization (a | b) &amp; (a | c) → a | (b &amp; c) over all clause pairs.
/// Factoring can grow multi-level structure, so the engine applies this rule under a
/// cost rollback guard (literals, then nodes; <see cref="GuardAgainstGrowth" />).
/// </summary>
internal sealed class FactorizationRule : IRewriteRule
{
    public string Name => "Factorization";

    public bool GuardAgainstGrowth => true;

    public AstNode? TryRewrite(AstNode node, FormulaFactory factory)
    {
        return node switch
        {
            OrNode or => FactorizeTerms(or.Operands, factory),
            AndNode and => FactorizeClauses(and, factory),
            _ => null
        };
    }

    private AstNode? FactorizeTerms(IReadOnlyList<AstNode> terms, FormulaFactory factory)
    {
        if (terms.Count < 2) return null;

        // Look for a factor contained in ALL terms
        foreach (var currentTerm in terms)
        {
            if (currentTerm is not AndNode currentAnd) continue;

            foreach (var factor in currentAnd.Operands)
            {
                if (!terms.All(term => ContainsFactor(term, factor))) continue;

                var remainingTerms = new List<AstNode>(terms.Count);
                foreach (var term in terms)
                {
                    var remainingTerm = RemoveFactor(term, factor, factory);
                    // A term equal to the factor makes the remainder 1,
                    // so the whole disjunction collapses: a | a&X = a
                    if (IsTrue(remainingTerm))
                        return factor;
                    remainingTerms.Add(remainingTerm);
                }

                return factory.And(factor, factory.Or(remainingTerms));
            }
        }

        // No factor is shared by ALL terms: factor out of the largest subgroup instead
        // (a & b | a & c | d  →  d | a & (b | c))
        return FactorizeLargestSubgroup(terms, factory);
    }

    private static AstNode? FactorizeLargestSubgroup(IReadOnlyList<AstNode> terms, FormulaFactory factory)
    {
        AstNode? bestFactor = null;
        List<AstNode>? bestGroup = null;

        foreach (var term in terms)
        {
            if (term is not AndNode candidateAnd) continue;

            foreach (var factor in candidateAnd.Operands)
            {
                var group = terms.Where(t => ContainsFactor(t, factor)).ToList();
                if (group.Count < 2 || group.Count == terms.Count) continue;

                if (bestGroup == null || group.Count > bestGroup.Count)
                {
                    bestGroup = group;
                    bestFactor = factor;
                }
            }
        }

        if (bestGroup == null || bestFactor == null) return null;

        var groupSet = new HashSet<AstNode>(bestGroup, ReferenceEqualityComparer.Instance);
        var others = terms.Where(t => !groupSet.Contains(t)).ToList();

        var remainders = new List<AstNode>(bestGroup.Count);
        var collapsed = false;
        foreach (var term in bestGroup)
        {
            var remainder = RemoveFactor(term, bestFactor, factory);
            if (IsTrue(remainder))
            {
                // A group term equals the factor: the whole group collapses to it
                collapsed = true;
                break;
            }

            remainders.Add(remainder);
        }

        var grouped = collapsed
            ? bestFactor
            : factory.And(bestFactor, factory.Or(remainders));

        var resultTerms = new List<AstNode> { grouped };
        resultTerms.AddRange(others);
        return factory.Or(resultTerms);
    }

    /// <summary>
    /// Reverse factorization over all clause pairs (not just adjacent ones):
    /// (X | R1) &amp; (X | R2) = X | (R1 &amp; R2), where X may span several common literals.
    /// </summary>
    private AstNode? FactorizeClauses(AndNode node, FormulaFactory factory)
    {
        var clauses = node.Operands.ToList();
        var changedAny = false;

        var changed = true;
        while (changed)
        {
            changed = false;

            for (var i = 0; i < clauses.Count && !changed; i++)
                for (var j = i + 1; j < clauses.Count && !changed; j++)
                {
                    if (clauses[i] is not OrNode orI || clauses[j] is not OrNode orJ) continue;

                    var literalsI = orI.Operands;
                    var literalsJ = orJ.Operands;
                    var common = literalsI
                        .Where(l => literalsJ.Contains(l, NodeComparer.Instance))
                        .Distinct(NodeComparer.Instance)
                        .ToList();
                    if (common.Count == 0) continue;

                    var restI = literalsI.Where(l => !common.Contains(l, NodeComparer.Instance)).ToList();
                    var restJ = literalsJ.Where(l => !common.Contains(l, NodeComparer.Instance)).ToList();
                    // A subset clause absorbs the other — absorption's job, not factoring's
                    if (restI.Count == 0 || restJ.Count == 0) continue;

                    var remaining = factory.And(factory.Or(restI), factory.Or(restJ));
                    // The merged remainder may itself expose factorable structure
                    remaining = TryRewrite(remaining, factory) ?? remaining;

                    clauses[i] = factory.Or(common.Append(remaining));
                    clauses.RemoveAt(j);
                    changed = true;
                    changedAny = true;
                }
        }

        if (!changedAny) return null;

        var result = factory.And(clauses);
        return result.Equals(node) ? null : result;
    }

    private static bool ContainsFactor(AstNode term, AstNode factor)
    {
        if (term.Equals(factor)) return true;

        if (term is AndNode andTerm)
            return andTerm.Operands.Any(f => f.Equals(factor));

        return false;
    }

    private static AstNode RemoveFactor(AstNode term, AstNode factor, FormulaFactory factory)
    {
        if (term.Equals(factor))
            return factory.True; // a / a = 1

        if (term is AndNode andTerm)
        {
            var factors = andTerm.Operands.Where(f => !f.Equals(factor)).ToList();

            if (factors.Count == 0) return factory.True;
            return factory.And(factors);
        }

        return term; // No factor found, return original term
    }
}
