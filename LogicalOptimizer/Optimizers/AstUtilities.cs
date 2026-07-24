using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Utility methods for AST manipulation shared by optimizers
/// </summary>
internal static class AstUtilities
{
    /// <summary>
    /// Check if two AST nodes are equal
    /// </summary>
    public static bool AreEqual(AstNode node1, AstNode node2)
    {
        return node1.Equals(node2);
    }

    /// <summary>
    /// Create a constant True node
    /// </summary>
    public static AstNode CreateTrue()
    {
        return ConstantNode.True;
    }

    /// <summary>
    /// Create a constant False node
    /// </summary>
    public static AstNode CreateFalse()
    {
        return ConstantNode.False;
    }

    /// <summary>
    /// Check if node represents True (1)
    /// </summary>
    public static bool IsTrue(AstNode node)
    {
        return node is ConstantNode { Value: true };
    }

    /// <summary>
    /// Check if node represents False (0)
    /// </summary>
    public static bool IsFalse(AstNode node)
    {
        return node is ConstantNode { Value: false };
    }

    /// <summary>
    /// Check if two nodes are complementary (a and !a)
    /// </summary>
    public static bool AreComplementary(AstNode node1, AstNode node2)
    {
        if (node1 is NotNode notNode1 && AreEqual(notNode1.Operand, node2))
            return true;

        if (node2 is NotNode notNode2 && AreEqual(notNode2.Operand, node1))
            return true;

        return false;
    }

    private static readonly CommutativityOptimizer Canonicalizer = new();

    /// <summary>
    /// Negation pushed into NNF: !(A &amp; B) → !A | !B, !(A | B) → !A &amp; !B, !!A → A
    /// </summary>
    public static AstNode NegateNnf(AstNode node)
    {
        return node switch
        {
            NotNode not => not.Operand,
            AndNode and => new OrNode(NegateNnf(and.Left), NegateNnf(and.Right)),
            OrNode or => new AndNode(NegateNnf(or.Left), NegateNnf(or.Right)),
            _ => new NotNode(node)
        };
    }

    /// <summary>
    /// Deep complement check: node1 ≡ !node2 up to De Morgan and commutative reordering.
    /// Catches compound pairs like (!a &amp; !b) vs (a | b) that the structural
    /// <see cref="AreComplementary" /> misses.
    /// </summary>
    public static bool AreComplementaryDeep(AstNode node1, AstNode node2)
    {
        if (AreComplementary(node1, node2)) return true;

        // Both plain variables: structural check above was already exhaustive
        if (node1 is VariableNode && node2 is VariableNode) return false;

        return Canonicalizer.Optimize(NegateNnf(node1)).Equals(Canonicalizer.Optimize(node2));
    }

    /// <summary>
    /// Flatten AND node into list of terms
    /// </summary>
    public static List<AstNode> FlattenAnd(AndNode node)
    {
        var terms = new List<AstNode>();
        FlattenAndRecursive(node, terms);
        return terms;
    }

    private static void FlattenAndRecursive(AstNode node, List<AstNode> terms)
    {
        if (node is AndNode andNode)
        {
            FlattenAndRecursive(andNode.Left, terms);
            FlattenAndRecursive(andNode.Right, terms);
        }
        else
        {
            terms.Add(node);
        }
    }

    /// <summary>
    /// Flatten OR node into list of terms
    /// </summary>
    public static List<AstNode> FlattenOr(OrNode node)
    {
        var terms = new List<AstNode>();
        FlattenOrRecursive(node, terms);
        return terms;
    }

    private static void FlattenOrRecursive(AstNode node, List<AstNode> terms)
    {
        if (node is OrNode orNode)
        {
            FlattenOrRecursive(orNode.Left, terms);
            FlattenOrRecursive(orNode.Right, terms);
        }
        else
        {
            terms.Add(node);
        }
    }

    /// <summary>
    /// Check if terms contain a contradiction (a & !a)
    /// </summary>
    public static bool ContainsContradiction(List<AstNode> terms)
    {
        for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
                if (AreComplementary(terms[i], terms[j]))
                    return true;
        return false;
    }

    /// <summary>
    /// Remove absorbed terms from a list (if A is in the list, remove any A&B).
    /// Two terms with the same factor set (e.g. "!a &amp; !c" and "!c &amp; !a") absorb each
    /// other; dropping both would lose minterms, so exactly one survivor is kept.
    /// </summary>
    public static List<AstNode> RemoveAbsorbedTerms(List<AstNode> terms)
    {
        var result = new List<AstNode>();

        foreach (var term in terms)
        {
            var isAbsorbed = false;

            // Strictly absorbed by some other term (absorber is a proper subset)
            foreach (var other in terms)
            {
                if (ReferenceEquals(other, term)) continue;
                if (Absorbs(other, term) && !Absorbs(term, other))
                {
                    isAbsorbed = true;
                    break;
                }
            }

            // Mutual absorption (identical factor sets): keep only the first occurrence
            if (!isAbsorbed)
                foreach (var kept in result)
                    if (Absorbs(kept, term))
                    {
                        isAbsorbed = true;
                        break;
                    }

            if (!isAbsorbed)
                result.Add(term);
        }

        return result;
    }

    /// <summary>
    /// Check if absorber absorbs absorbed (A absorbs A&B): every factor of the
    /// absorber must appear among the factors of the absorbed conjunction.
    /// </summary>
    public static bool Absorbs(AstNode absorber, AstNode absorbed)
    {
        if (AreEqual(absorber, absorbed)) return true;

        if (absorbed is not AndNode absorbedAndNode) return false;

        var absorbedFactors = FlattenAnd(absorbedAndNode);
        var absorberFactors = absorber is AndNode absorberAndNode
            ? FlattenAnd(absorberAndNode)
            : new List<AstNode> { absorber };

        return absorberFactors.All(factor => absorbedFactors.Contains(factor, NodeComparer.Instance));
    }

    /// <summary>
    /// Dual of <see cref="Absorbs" /> for clauses: (A | B) absorbs (A | B | C)
    /// because in a conjunction the wider clause is implied by the narrower one.
    /// </summary>
    public static bool AbsorbsClause(AstNode absorber, AstNode absorbed)
    {
        if (AreEqual(absorber, absorbed)) return true;

        if (absorbed is not OrNode absorbedOrNode) return false;

        var absorbedLiterals = FlattenOr(absorbedOrNode);
        var absorberLiterals = absorber is OrNode absorberOrNode
            ? FlattenOr(absorberOrNode)
            : new List<AstNode> { absorber };

        return absorberLiterals.All(literal => absorbedLiterals.Contains(literal, NodeComparer.Instance));
    }

    /// <summary>
    /// Remove absorbed clauses from a conjunction list; mirror of <see cref="RemoveAbsorbedTerms" />
    /// with the same one-survivor protection for clauses that absorb each other.
    /// </summary>
    public static List<AstNode> RemoveAbsorbedClauses(List<AstNode> clauses)
    {
        var result = new List<AstNode>();

        foreach (var clause in clauses)
        {
            var isAbsorbed = false;

            foreach (var other in clauses)
            {
                if (ReferenceEquals(other, clause)) continue;
                if (AbsorbsClause(other, clause) && !AbsorbsClause(clause, other))
                {
                    isAbsorbed = true;
                    break;
                }
            }

            if (!isAbsorbed)
                foreach (var kept in result)
                    if (AbsorbsClause(kept, clause))
                    {
                        isAbsorbed = true;
                        break;
                    }

            if (!isAbsorbed)
                result.Add(clause);
        }

        return result;
    }

    /// <summary>
    /// Resolvent of two clauses: (A | B) and (!A | C) give (B | C).
    /// Returns null when no complementary pair exists or the resolvent is tautological.
    /// </summary>
    public static AstNode? FindResolvent(AstNode clause1, AstNode clause2)
    {
        if (clause1 is OrNode or1 && clause2 is OrNode or2)
        {
            var literals1 = FlattenOr(or1);
            var literals2 = FlattenOr(or2);

            for (var i = 0; i < literals1.Count; i++)
                for (var j = 0; j < literals2.Count; j++)
                    if (AreComplementary(literals1[i], literals2[j]))
                    {
                        var remaining = literals1.Where((_, idx) => idx != i)
                            .Concat(literals2.Where((_, idx) => idx != j))
                            .Distinct(NodeComparer.Instance)
                            .ToList();

                        // A tautological resolvent (contains x and !x) is useless
                        for (var a = 0; a < remaining.Count; a++)
                            for (var b = a + 1; b < remaining.Count; b++)
                                if (AreComplementary(remaining[a], remaining[b]))
                                    return null;

                        if (remaining.Count == 0) return CreateFalse();
                        if (remaining.Count == 1) return remaining[0];
                        return remaining.Aggregate((a, b) => new OrNode(a, b));
                    }
        }

        // Single literals: A and !A resolve to the empty (false) clause
        if (AreComplementary(clause1, clause2)) return CreateFalse();

        return null;
    }

    /// <summary>
    /// Compare two nodes as flattened AND-factor sets (order-insensitive)
    /// </summary>
    public static bool SameTermSet(AstNode term1, AstNode term2)
    {
        var factors1 = term1 is AndNode and1 ? FlattenAnd(and1) : new List<AstNode> { term1 };
        var factors2 = term2 is AndNode and2 ? FlattenAnd(and2) : new List<AstNode> { term2 };
        return SameNodeSet(factors1, factors2);
    }

    /// <summary>
    /// Compare two nodes as flattened OR-literal sets (order-insensitive)
    /// </summary>
    public static bool SameClauseSet(AstNode clause1, AstNode clause2)
    {
        var literals1 = clause1 is OrNode or1 ? FlattenOr(or1) : new List<AstNode> { clause1 };
        var literals2 = clause2 is OrNode or2 ? FlattenOr(or2) : new List<AstNode> { clause2 };
        return SameNodeSet(literals1, literals2);
    }

    private static bool SameNodeSet(List<AstNode> first, List<AstNode> second)
    {
        if (first.Count != second.Count) return false;
        return first.All(f => second.Contains(f, NodeComparer.Instance)) &&
               second.All(s => first.Contains(s, NodeComparer.Instance));
    }

    /// <summary>
    /// Consensus of two AND-terms: (A &amp; B) and (!A &amp; C) give (B &amp; C).
    /// Returns null when no complementary pair exists or the consensus is contradictory.
    /// </summary>
    public static AstNode? FindConsensus(AstNode term1, AstNode term2)
    {
        if (term1 is AndNode and1 && term2 is AndNode and2)
        {
            var factors1 = FlattenAnd(and1);
            var factors2 = FlattenAnd(and2);

            for (var i = 0; i < factors1.Count; i++)
                for (var j = 0; j < factors2.Count; j++)
                    if (AreComplementary(factors1[i], factors2[j]))
                    {
                        var remaining = factors1.Where((_, idx) => idx != i)
                            .Concat(factors2.Where((_, idx) => idx != j))
                            .Distinct(NodeComparer.Instance)
                            .ToList();

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

    /// <summary>
    /// Create a node of the same type as the template with new children,
    /// preserving the ForceParentheses display hint
    /// </summary>
    public static BinaryNode Rebuild(BinaryNode template, AstNode left, AstNode right)
    {
        return template switch
        {
            AndNode => new AndNode(left, right, template.ForceParentheses),
            OrNode => new OrNode(left, right, template.ForceParentheses),
            XorNode => new XorNode(left, right, template.ForceParentheses),
            NandNode => new NandNode(left, right, template.ForceParentheses),
            NorNode => new NorNode(left, right, template.ForceParentheses),
            EqvNode => new EqvNode(left, right, template.ForceParentheses),
            ImpNode => new ImpNode(left, right, template.ForceParentheses),
            _ => throw new InvalidOperationException($"Unknown binary node type: {template.GetType().Name}")
        };
    }

    /// <summary>
    /// Apply optimization rule with rollback if it increases node count
    /// </summary>
    public static AstNode ApplyOptimizationRuleWithRollback(AstNode node,
        Func<AstNode, OptimizationMetrics?, AstNode> optimizationRule,
        OptimizationMetrics? metrics,
        string ruleName)
    {
        var originalNodeCount = AstMetrics.CountNodes(node);
        var optimizedNode = optimizationRule(node, metrics);
        var optimizedNodeCount = AstMetrics.CountNodes(optimizedNode);

        // If the rule increased the node count, roll back
        if (optimizedNodeCount > originalNodeCount)
        {
            if (metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd($"{ruleName}_Rollback", 0);
                metrics.RuleApplicationCount[$"{ruleName}_Rollback"]++;
            }

            return node; // Return original node
        }

        return optimizedNode;
    }

    /// <summary>
    /// Node comparer for AST nodes
    /// </summary>
    internal sealed class NodeComparer : IEqualityComparer<AstNode>
    {
        public static readonly NodeComparer Instance = new();

        public bool Equals(AstNode? x, AstNode? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(AstNode? obj)
        {
            return obj?.GetHashCode() ?? 0;
        }
    }
}
