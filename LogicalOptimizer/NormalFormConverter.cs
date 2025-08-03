namespace LogicalOptimizer;

public class NormalFormConverter
{
    public AstNode ConvertToCNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var optimizer = new ExpressionOptimizer();
        node = optimizer.Optimize(node);

        var cnf = DistributeOrOverAnd(node);
        return SimplifyTautologies(cnf);
    }

    public AstNode ConvertToDNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var dnf = DistributeAndOverOr(node);

        // Don't apply full optimization to DNF as it might break the normal form
        // Only apply basic simplifications that preserve DNF structure
        return SimplifyDNF(dnf);
    }

    private AstNode DistributeOrOverAnd(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var left = DistributeOrOverAnd(orNode.Left);
            var right = DistributeOrOverAnd(orNode.Right);

            // A | (B & C) = (A | B) & (A | C)
            if (right is AndNode rightAnd)
                return new AndNode(
                    DistributeOrOverAnd(new OrNode(left, rightAnd.Left)),
                    DistributeOrOverAnd(new OrNode(left, rightAnd.Right))
                );

            // (A & B) | C = (A | C) & (B | C)  
            if (left is AndNode leftAnd)
                return new AndNode(
                    DistributeOrOverAnd(new OrNode(leftAnd.Left, right)),
                    DistributeOrOverAnd(new OrNode(leftAnd.Right, right))
                );

            return new OrNode(left, right);
        }

        if (node is AndNode andNode)
            return new AndNode(DistributeOrOverAnd(andNode.Left), DistributeOrOverAnd(andNode.Right));

        if (node is NotNode notNode) return new NotNode(DistributeOrOverAnd(notNode.Operand));

        return node;
    }

    private AstNode DistributeAndOverOr(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = DistributeAndOverOr(andNode.Left);
            var right = DistributeAndOverOr(andNode.Right);

            if (left is OrNode leftOr)
                return new OrNode(
                    DistributeAndOverOr(new AndNode(leftOr.Left, right)),
                    DistributeAndOverOr(new AndNode(leftOr.Right, right))
                );

            if (right is OrNode rightOr)
                return new OrNode(
                    DistributeAndOverOr(new AndNode(left, rightOr.Left)),
                    DistributeAndOverOr(new AndNode(left, rightOr.Right))
                );

            return new AndNode(left, right);
        }

        if (node is OrNode orNode)
            return new OrNode(DistributeAndOverOr(orNode.Left), DistributeAndOverOr(orNode.Right));

        if (node is NotNode notNode)
            return new NotNode(DistributeAndOverOr(notNode.Operand));

        return node;
    }

    private AstNode SimplifyTautologies(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = SimplifyTautologies(andNode.Left);
            var right = SimplifyTautologies(andNode.Right);

            // Check if either side is a tautology (always true)
            if (IsTautology(left)) return right;
            if (IsTautology(right)) return left;

            // Check if either side is a contradiction (always false)
            if (IsContradiction(left) || IsContradiction(right))
                return CreateFalse();

            return new AndNode(left, right);
        }

        if (node is OrNode orNode)
        {
            var left = SimplifyTautologies(orNode.Left);
            var right = SimplifyTautologies(orNode.Right);

            // Check if either side is a tautology (always true)
            if (IsTautology(left) || IsTautology(right))
                return CreateTrue();

            // Check if either side is a contradiction (always false)
            if (IsContradiction(left)) return right;
            if (IsContradiction(right)) return left;

            return new OrNode(left, right);
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyTautologies(notNode.Operand));

        return node;
    }

    private bool IsTautology(AstNode node)
    {
        // Check for patterns like (a | !a), (!b | b), etc.
        if (node is OrNode orNode) return IsComplementPair(orNode.Left, orNode.Right);
        return false;
    }

    private bool IsContradiction(AstNode node)
    {
        // Check for patterns like (a & !a), (!b & b), etc.
        if (node is AndNode andNode) return IsComplementPair(andNode.Left, andNode.Right);
        return false;
    }

    private bool IsComplementPair(AstNode node1, AstNode node2)
    {
        // Check if node1 and node2 are complements (a and !a)
        if (node1 is NotNode notNode1) return AreEqual(notNode1.Operand, node2);
        if (node2 is NotNode notNode2) return AreEqual(node1, notNode2.Operand);
        return false;
    }

    private bool AreEqual(AstNode node1, AstNode node2)
    {
        if (node1.GetType() != node2.GetType()) return false;

        if (node1 is VariableNode var1 && node2 is VariableNode var2)
            return var1.Name == var2.Name;

        if (node1 is NotNode not1 && node2 is NotNode not2)
            return AreEqual(not1.Operand, not2.Operand);

        if (node1 is BinaryNode bin1 && node2 is BinaryNode bin2)
            return AreEqual(bin1.Left, bin2.Left) && AreEqual(bin1.Right, bin2.Right);

        return false;
    }

    private AstNode CreateTrue()
    {
        return new VariableNode("1");
    }

    private AstNode CreateFalse()
    {
        return new VariableNode("0");
    }

    /// <summary>
    ///     Simplify DNF while preserving the disjunctive normal form structure
    /// </summary>
    private AstNode SimplifyDNF(AstNode dnf)
    {
        // Only remove duplicate terms and obvious contradictions
        // Don't factor or apply transformations that break DNF structure
        return RemoveDuplicateTerms(dnf);
    }

    private AstNode RemoveDuplicateTerms(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var left = RemoveDuplicateTerms(orNode.Left);
            var right = RemoveDuplicateTerms(orNode.Right);

            // If terms are identical, return just one
            if (AreEqual(left, right))
                return left;

            return new OrNode(left, right);
        }

        return node;
    }
}