using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for redundant terms and complex expression simplification
/// </summary>
internal class RedundancyOptimizer : IOptimizer
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
            // The consensus theorem only licenses dropping a term while BOTH of its
            // parent terms are still present. Removing all candidates in one pass is
            // unsound: two terms that justify each other's removal would both vanish,
            // losing minterms. Remove one term at a time, re-checking against the
            // CURRENT list after every removal.
            var simplified = FlattenOr(orNode);
            var rulesApplied = 0;

            var removedSomething = true;
            while (removedSomething && simplified.Count > 1)
            {
                removedSomething = false;

                for (var i = 0; i < simplified.Count; i++)
                {
                    if (simplified[i] is not AndNode) continue;
                    if (!IsConsensusOfOtherTerms(i, simplified)) continue;

                    simplified.RemoveAt(i);
                    rulesApplied++;
                    removedSomething = true;
                    break;
                }
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
            // Dual (clause-side) consensus: in a conjunction, a clause that is the
            // resolvent of two other present clauses is redundant. Same one-at-a-time
            // removal discipline as the OR side: both parents must remain.
            var clauses = FlattenAnd(andNode)
                .Select(clause => SimplifyConsensusRedundancy(clause, metrics))
                .ToList();
            var rulesApplied = 0;

            var removedSomething = true;
            while (removedSomething && clauses.Count > 1)
            {
                removedSomething = false;

                for (var i = 0; i < clauses.Count; i++)
                {
                    if (clauses[i] is not OrNode) continue;
                    if (!IsResolventOfOtherClauses(i, clauses)) continue;

                    clauses.RemoveAt(i);
                    rulesApplied++;
                    removedSomething = true;
                    break;
                }
            }

            if (rulesApplied > 0 && metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("ConsensusSimplification", 0);
                metrics.RuleApplicationCount["ConsensusSimplification"] += rulesApplied;
                metrics.AppliedRules += rulesApplied;
            }

            if (clauses.Count == 0) return CreateTrue();
            if (clauses.Count == 1) return clauses[0];

            var result = clauses.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyConsensusRedundancy(notNode.Operand, metrics));

        return node;
    }

    private static bool IsResolventOfOtherClauses(int clauseIndex, List<AstNode> allClauses)
    {
        var clause = allClauses[clauseIndex];

        for (var i = 0; i < allClauses.Count; i++)
            for (var j = i + 1; j < allClauses.Count; j++)
            {
                if (i == clauseIndex || j == clauseIndex)
                    continue;

                var resolvent = FindResolvent(allClauses[i], allClauses[j]);
                if (resolvent != null && SameClauseSet(resolvent, clause)) return true;
            }

        return false;
    }

    private static bool IsConsensusOfOtherTerms(int termIndex, List<AstNode> allTerms)
    {
        var term = allTerms[termIndex];

        for (var i = 0; i < allTerms.Count; i++)
            for (var j = i + 1; j < allTerms.Count; j++)
            {
                if (i == termIndex || j == termIndex)
                    continue;

                var consensus = FindConsensus(allTerms[i], allTerms[j]);
                if (consensus != null && SameTermSet(consensus, term)) return true;
            }

        return false;
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
