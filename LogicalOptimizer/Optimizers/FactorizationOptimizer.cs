using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for factorization: a & b | a & c = a & (b | c), (a | b) & (a | c) = a | (b & c)
/// </summary>
internal class FactorizationOptimizer : IOptimizer
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
            var clauses = FlattenAnd(andNode).Select(ApplyFactorizationInternal).ToList();

            // Reverse factorization over all clause pairs (not just adjacent children):
            // (X | R1) & (X | R2) = X | (R1 & R2), X may span several common literals
            var changed = true;
            while (changed)
            {
                changed = false;

                for (var i = 0; i < clauses.Count && !changed; i++)
                    for (var j = i + 1; j < clauses.Count && !changed; j++)
                    {
                        if (clauses[i] is not OrNode orI || clauses[j] is not OrNode orJ) continue;

                        var literalsI = FlattenOr(orI);
                        var literalsJ = FlattenOr(orJ);
                        var common = literalsI
                            .Where(l => literalsJ.Contains(l, NodeComparer.Instance))
                            .Distinct(NodeComparer.Instance)
                            .ToList();
                        if (common.Count == 0) continue;

                        var restI = literalsI.Where(l => !common.Contains(l, NodeComparer.Instance)).ToList();
                        var restJ = literalsJ.Where(l => !common.Contains(l, NodeComparer.Instance)).ToList();
                        // A subset clause absorbs the other — absorption's job, not factoring's
                        if (restI.Count == 0 || restJ.Count == 0) continue;

                        var commonNode = common.Count == 1 ? common[0] : common.Aggregate((a, b) => new OrNode(a, b));
                        var restINode = restI.Count == 1 ? restI[0] : restI.Aggregate((a, b) => new OrNode(a, b));
                        var restJNode = restJ.Count == 1 ? restJ[0] : restJ.Aggregate((a, b) => new OrNode(a, b));
                        var remaining = ApplyFactorizationInternal(new AndNode(restINode, restJNode, true));

                        clauses[i] = new OrNode(commonNode, remaining);
                        clauses.RemoveAt(j);
                        changed = true;
                    }
            }

            if (clauses.Count == 1) return clauses[0];

            var result = clauses.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
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
                        var remainingTerms = new List<AstNode>(terms.Count);
                        foreach (var term in terms)
                        {
                            var remainingTerm = RemoveFactor(term, factor);
                            // A term equal to the factor makes the remainder 1,
                            // so the whole disjunction collapses: a | a&X = a
                            if (IsTrue(remainingTerm))
                                return factor;
                            remainingTerms.Add(remainingTerm);
                        }

                        var remaining = remainingTerms.Count == 1
                            ? remainingTerms[0]
                            : remainingTerms.Aggregate((a, b) => new OrNode(a, b));

                        return new AndNode(factor, remaining);
                    }
                }
            }
        }

        // No factor is shared by ALL terms: factor out of the largest subgroup instead
        // (a & b | a & c | d  →  d | a & (b | c))
        return FactorizeLargestSubgroup(terms);
    }

    private AstNode? FactorizeLargestSubgroup(List<AstNode> terms)
    {
        AstNode? bestFactor = null;
        List<AstNode>? bestGroup = null;

        foreach (var term in terms)
        {
            if (term is not AndNode candidateAnd) continue;

            foreach (var factor in FlattenAnd(candidateAnd))
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

        AstNode grouped;
        var remainders = new List<AstNode>(bestGroup.Count);
        var collapsed = false;
        foreach (var term in bestGroup)
        {
            var remainder = RemoveFactor(term, bestFactor);
            if (IsTrue(remainder))
            {
                // A group term equals the factor: the whole group collapses to it
                collapsed = true;
                break;
            }

            remainders.Add(remainder);
        }

        grouped = collapsed
            ? bestFactor
            : new AndNode(bestFactor,
                remainders.Count == 1 ? remainders[0] : remainders.Aggregate((a, b) => new OrNode(a, b)));

        var resultTerms = new List<AstNode> { grouped };
        resultTerms.AddRange(others);
        return resultTerms.Count == 1
            ? resultTerms[0]
            : resultTerms.Aggregate((a, b) => new OrNode(a, b));
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
            var allFactors = FlattenAnd(andTerm);
            var factors = new List<AstNode>(allFactors.Count);

            foreach (var f in allFactors)
            {
                if (!AreEqual(f, factor))
                    factors.Add(f);
            }

            if (factors.Count == 0)
                return CreateTrue();
            if (factors.Count == 1)
                return factors[0];

            return factors.Aggregate((a, b) => new AndNode(a, b));
        }

        return term; // No factor found, return original term
    }
}
