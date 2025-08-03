using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Utility methods for AST manipulation shared by optimizers
/// </summary>
public static class AstUtilities
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
        return new VariableNode("1");
    }

    /// <summary>
    /// Create a constant False node
    /// </summary>
    public static AstNode CreateFalse()
    {
        return new VariableNode("0");
    }

    /// <summary>
    /// Check if node represents True (1)
    /// </summary>
    public static bool IsTrue(AstNode node)
    {
        return node is VariableNode {Name: "1"};
    }

    /// <summary>
    /// Check if node represents False (0)
    /// </summary>
    public static bool IsFalse(AstNode node)
    {
        return node is VariableNode {Name: "0"};
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
    /// Remove absorbed terms from a list (if A is in the list, remove any A&B)
    /// </summary>
    public static List<AstNode> RemoveAbsorbedTerms(List<AstNode> terms)
    {
        var result = new List<AstNode>();

        foreach (var term in terms)
        {
            var isAbsorbed = false;

            foreach (var other in terms)
            {
                if (!AreEqual(term, other) && Absorbs(other, term))
                {
                    isAbsorbed = true;
                    break;
                }
            }

            if (!isAbsorbed)
                result.Add(term);
        }

        return result;
    }

    /// <summary>
    /// Check if absorber absorbs absorbed (A absorbs A&B)
    /// </summary>
    public static bool Absorbs(AstNode absorber, AstNode absorbed)
    {
        if (AreEqual(absorber, absorbed)) return true;

        if (absorbed is AndNode absorbedAndNode)
            return AreEqual(absorber, absorbedAndNode.Left) || AreEqual(absorber, absorbedAndNode.Right);

        return false;
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
    public class NodeComparer : IEqualityComparer<AstNode>
    {
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
