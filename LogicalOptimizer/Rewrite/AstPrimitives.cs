using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Operand-list primitives shared by the rewrite rules. All helpers treat an
/// <see cref="AndNode" /> as its flat factor list and an <see cref="OrNode" /> as its
/// flat literal list (factory invariants guarantee the lists are already flattened),
/// and every composite result is built through the <see cref="FormulaFactory" /> so it
/// is canonical and interned.
/// </summary>
internal static class AstPrimitives
{
    /// <summary>Check if node represents constant true.</summary>
    public static bool IsTrue(AstNode node)
    {
        return node is ConstantNode { Value: true };
    }

    /// <summary>Check if node represents constant false.</summary>
    public static bool IsFalse(AstNode node)
    {
        return node is ConstantNode { Value: false };
    }

    /// <summary>The flat AND-factor list of a term (a non-AND term is its own single factor).</summary>
    public static IReadOnlyList<AstNode> TermFactors(AstNode term)
    {
        return term is AndNode and ? and.Operands : new[] { term };
    }

    /// <summary>The flat OR-literal list of a clause (a non-OR clause is its own single literal).</summary>
    public static IReadOnlyList<AstNode> ClauseLiterals(AstNode clause)
    {
        return clause is OrNode or ? or.Operands : new[] { clause };
    }

    /// <summary>Structural complement check: one node is exactly the negation of the other.</summary>
    public static bool AreComplementary(AstNode node1, AstNode node2)
    {
        if (node1 is NotNode notNode1 && notNode1.Operand.Equals(node2)) return true;
        if (node2 is NotNode notNode2 && notNode2.Operand.Equals(node1)) return true;
        return false;
    }

    /// <summary>
    /// Negation pushed into NNF through the factory: !(A &amp; B) → !A | !B,
    /// !(A | B) → !A &amp; !B, !!A → A. The result is canonical.
    /// </summary>
    public static AstNode NegateNnf(AstNode node, FormulaFactory factory)
    {
        return node switch
        {
            NotNode not => not.Operand,
            AndNode and => factory.Or(and.Operands.Select(o => NegateNnf(o, factory))),
            OrNode or => factory.And(or.Operands.Select(o => NegateNnf(o, factory))),
            _ => factory.Not(node)
        };
    }

    /// <summary>
    /// Deep complement check: node1 ≡ !node2 up to De Morgan and canonical reordering.
    /// Catches compound pairs like (!a &amp; !b) vs (a | b) that the structural
    /// <see cref="AreComplementary" /> misses. Both nodes must be factory-canonical.
    /// </summary>
    public static bool AreComplementaryDeep(AstNode node1, AstNode node2, FormulaFactory factory)
    {
        if (AreComplementary(node1, node2)) return true;

        // Both plain variables: the structural check above was already exhaustive
        if (node1 is VariableNode && node2 is VariableNode) return false;

        return NegateNnf(node1, factory).Equals(node2);
    }

    /// <summary>Check if a list of factors contains a contradiction (x together with !x).</summary>
    public static bool ContainsContradiction(IReadOnlyList<AstNode> factors)
    {
        for (var i = 0; i < factors.Count; i++)
            for (var j = i + 1; j < factors.Count; j++)
                if (AreComplementary(factors[i], factors[j]))
                    return true;
        return false;
    }

    /// <summary>
    /// Check if absorber absorbs absorbed (A absorbs A&amp;B): every factor of the
    /// absorber must appear among the factors of the absorbed conjunction.
    /// </summary>
    public static bool Absorbs(AstNode absorber, AstNode absorbed)
    {
        if (absorber.Equals(absorbed)) return true;

        if (absorbed is not AndNode absorbedAnd) return false;

        var absorbedFactors = absorbedAnd.Operands;
        var absorberFactors = TermFactors(absorber);
        return absorberFactors.All(factor => absorbedFactors.Contains(factor, NodeComparer.Instance));
    }

    /// <summary>
    /// Dual of <see cref="Absorbs" /> for clauses: (A | B) absorbs (A | B | C)
    /// because in a conjunction the wider clause is implied by the narrower one.
    /// </summary>
    public static bool AbsorbsClause(AstNode absorber, AstNode absorbed)
    {
        if (absorber.Equals(absorbed)) return true;

        if (absorbed is not OrNode absorbedOr) return false;

        var absorbedLiterals = absorbedOr.Operands;
        var absorberLiterals = ClauseLiterals(absorber);
        return absorberLiterals.All(literal => absorbedLiterals.Contains(literal, NodeComparer.Instance));
    }

    /// <summary>
    /// Remove absorbed terms from a list (if A is in the list, remove any A&amp;B).
    /// Two terms with the same factor set absorb each other; dropping both would lose
    /// minterms, so exactly one survivor is kept.
    /// </summary>
    public static List<AstNode> RemoveAbsorbedTerms(IReadOnlyList<AstNode> terms)
    {
        return RemoveAbsorbed(terms, Absorbs);
    }

    /// <summary>
    /// Remove absorbed clauses from a conjunction list; mirror of
    /// <see cref="RemoveAbsorbedTerms" /> with the same one-survivor protection.
    /// </summary>
    public static List<AstNode> RemoveAbsorbedClauses(IReadOnlyList<AstNode> clauses)
    {
        return RemoveAbsorbed(clauses, AbsorbsClause);
    }

    private static List<AstNode> RemoveAbsorbed(IReadOnlyList<AstNode> items, Func<AstNode, AstNode, bool> absorbs)
    {
        var result = new List<AstNode>();

        foreach (var item in items)
        {
            var isAbsorbed = false;

            // Strictly absorbed by some other item (absorber is a proper subset)
            foreach (var other in items)
            {
                if (ReferenceEquals(other, item)) continue;
                if (absorbs(other, item) && !absorbs(item, other))
                {
                    isAbsorbed = true;
                    break;
                }
            }

            // Mutual absorption (identical sets): keep only the first occurrence
            if (!isAbsorbed)
                foreach (var kept in result)
                    if (absorbs(kept, item))
                    {
                        isAbsorbed = true;
                        break;
                    }

            if (!isAbsorbed)
                result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Resolvent of two clauses: (A | B) and (!A | C) give (B | C).
    /// Returns null when no complementary pair exists or the resolvent is tautological.
    /// </summary>
    public static AstNode? FindResolvent(AstNode clause1, AstNode clause2, FormulaFactory factory)
    {
        if (clause1 is OrNode or1 && clause2 is OrNode or2)
        {
            var literals1 = or1.Operands;
            var literals2 = or2.Operands;

            for (var i = 0; i < literals1.Count; i++)
                for (var j = 0; j < literals2.Count; j++)
                    if (AreComplementary(literals1[i], literals2[j]))
                    {
                        var remaining = literals1.Where((_, idx) => idx != i)
                            .Concat(literals2.Where((_, idx) => idx != j))
                            .Distinct(NodeComparer.Instance)
                            .ToList();

                        // A tautological resolvent (contains x and !x) is useless
                        if (ContainsContradiction(remaining)) return null;

                        if (remaining.Count == 0) return factory.False;
                        return factory.Or(remaining);
                    }
        }

        // Single literals: A and !A resolve to the empty (false) clause
        if (AreComplementary(clause1, clause2)) return factory.False;

        return null;
    }

    /// <summary>
    /// Consensus of two AND-terms: (A &amp; B) and (!A &amp; C) give (B &amp; C).
    /// Returns null when no complementary pair exists or the consensus is contradictory.
    /// </summary>
    public static AstNode? FindConsensus(AstNode term1, AstNode term2, FormulaFactory factory)
    {
        if (term1 is AndNode and1 && term2 is AndNode and2)
        {
            var factors1 = and1.Operands;
            var factors2 = and2.Operands;

            for (var i = 0; i < factors1.Count; i++)
                for (var j = 0; j < factors2.Count; j++)
                    if (AreComplementary(factors1[i], factors2[j]))
                    {
                        var remaining = factors1.Where((_, idx) => idx != i)
                            .Concat(factors2.Where((_, idx) => idx != j))
                            .Distinct(NodeComparer.Instance)
                            .ToList();

                        if (ContainsContradiction(remaining)) return null;

                        if (remaining.Count == 0) return factory.True;
                        return factory.And(remaining);
                    }
        }

        // Simple variables: A + !A → 1 (tautology)
        if (AreComplementary(term1, term2)) return factory.True;

        return null;
    }

    /// <summary>Compare two nodes as flattened AND-factor sets (order-insensitive).</summary>
    public static bool SameTermSet(AstNode term1, AstNode term2)
    {
        return SameNodeSet(TermFactors(term1), TermFactors(term2));
    }

    /// <summary>Compare two nodes as flattened OR-literal sets (order-insensitive).</summary>
    public static bool SameClauseSet(AstNode clause1, AstNode clause2)
    {
        return SameNodeSet(ClauseLiterals(clause1), ClauseLiterals(clause2));
    }

    private static bool SameNodeSet(IReadOnlyList<AstNode> first, IReadOnlyList<AstNode> second)
    {
        if (first.Count != second.Count) return false;
        return first.All(f => second.Contains(f, NodeComparer.Instance)) &&
               second.All(s => first.Contains(s, NodeComparer.Instance));
    }

    /// <summary>
    /// Recreate a derived binary node of the same type with new children.
    /// Used by the display-oriented pattern code, never by the rewrite pipeline.
    /// </summary>
    public static BinaryNode RebuildDerived(BinaryNode template, AstNode left, AstNode right)
    {
        return template switch
        {
            XorNode => new XorNode(left, right),
            NandNode => new NandNode(left, right),
            NorNode => new NorNode(left, right),
            EqvNode => new EqvNode(left, right),
            ImpNode => new ImpNode(left, right),
            _ => throw new InvalidOperationException($"Unknown binary node type: {template.GetType().Name}")
        };
    }

    /// <summary>Structural-equality comparer for AST nodes.</summary>
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
