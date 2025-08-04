namespace LogicalOptimizer;

public class NormalFormConverter
{
    // Разумные пределы для CNF/DNF конверсии
    private const int MAX_DISTRIBUTION_CALLS = 10000;
    private const int MAX_RECURSION_DEPTH = 15;
    public AstNode ConvertToCNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var optimizer = new ExpressionOptimizer();
        node = optimizer.Optimize(node);

        _distributionCalls = 0;
        _maxDepth = 0;
        _limitExceeded = false;
        
        var cnf = DistributeOrOverAnd(node);
        
        if (_limitExceeded)
        {
            Console.WriteLine($"CNF Distribution: LIMIT EXCEEDED (calls: {_distributionCalls}, max depth: {_maxDepth}) - returning '-'");
            return new VariableNode("-");
        }

        var result = SimplifyTautologies(cnf);

        return result;
    }

    public AstNode ConvertToDNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        _distributionCalls = 0;
        _maxDepth = 0;
        _limitExceeded = false;
        
        var dnf = DistributeAndOverOr(node);
        
        if (_limitExceeded)
        {
            Console.WriteLine($"DNF Distribution: LIMIT EXCEEDED (calls: {_distributionCalls}, max depth: {_maxDepth}) - returning '-'");
            return new VariableNode("-");
        }

        // Don't apply full optimization to DNF as it might break the normal form
        // Only apply basic simplifications that preserve DNF structure
        var result = SimplifyDNF(dnf);

        return result;
    }

    private static int _distributionCalls = 0;
    private static int _maxDepth = 0;
    private static bool _limitExceeded = false;

    private AstNode DistributeOrOverAnd(AstNode node, int depth = 0)
    {
        _distributionCalls++;
        _maxDepth = Math.Max(_maxDepth, depth);
        
        // Проверяем ограничения
        if (_distributionCalls > MAX_DISTRIBUTION_CALLS || depth > MAX_RECURSION_DEPTH)
        {
            _limitExceeded = true;
            return node; // Возвращаем исходный узел при превышении лимитов
        }
        
        // Performance guard - log excessive recursion
        if (depth > 10 && _distributionCalls % 1000 == 0)
        {
            Console.WriteLine($"CNF Distribution: depth={depth}, calls={_distributionCalls}");
        }

        if (node is OrNode orNode)
        {
            var left = DistributeOrOverAnd(orNode.Left, depth + 1);
            var right = DistributeOrOverAnd(orNode.Right, depth + 1);

            // A | (B & C) = (A | B) & (A | C)
            if (right is AndNode rightAnd)
                return new AndNode(
                    DistributeOrOverAnd(new OrNode(left, rightAnd.Left), depth + 1),
                    DistributeOrOverAnd(new OrNode(left, rightAnd.Right), depth + 1)
                );

            // (A & B) | C = (A | C) & (B | C)  
            if (left is AndNode leftAnd)
                return new AndNode(
                    DistributeOrOverAnd(new OrNode(leftAnd.Left, right), depth + 1),
                    DistributeOrOverAnd(new OrNode(leftAnd.Right, right), depth + 1)
                );

            return new OrNode(left, right);
        }

        if (node is AndNode andNode)
            return new AndNode(DistributeOrOverAnd(andNode.Left, depth + 1), DistributeOrOverAnd(andNode.Right, depth + 1));

        if (node is NotNode notNode) return new NotNode(DistributeOrOverAnd(notNode.Operand, depth + 1));

        return node;
    }

    private AstNode DistributeAndOverOr(AstNode node, int depth = 0)
    {
        _distributionCalls++;
        _maxDepth = Math.Max(_maxDepth, depth);
        
        // Проверяем ограничения
        if (_distributionCalls > MAX_DISTRIBUTION_CALLS || depth > MAX_RECURSION_DEPTH)
        {
            _limitExceeded = true;
            return node; // Возвращаем исходный узел при превышении лимитов
        }
        
        if (node is AndNode andNode)
        {
            var left = DistributeAndOverOr(andNode.Left, depth + 1);
            var right = DistributeAndOverOr(andNode.Right, depth + 1);

            if (left is OrNode leftOr)
                return new OrNode(
                    DistributeAndOverOr(new AndNode(leftOr.Left, right), depth + 1),
                    DistributeAndOverOr(new AndNode(leftOr.Right, right), depth + 1)
                );

            if (right is OrNode rightOr)
                return new OrNode(
                    DistributeAndOverOr(new AndNode(left, rightOr.Left), depth + 1),
                    DistributeAndOverOr(new AndNode(left, rightOr.Right), depth + 1)
                );

            return new AndNode(left, right);
        }

        if (node is OrNode orNode)
            return new OrNode(DistributeAndOverOr(orNode.Left, depth + 1), DistributeAndOverOr(orNode.Right, depth + 1));

        if (node is NotNode notNode)
            return new NotNode(DistributeAndOverOr(notNode.Operand, depth + 1));

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