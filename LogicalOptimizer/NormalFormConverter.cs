namespace LogicalOptimizer;

public class NormalFormConverter
{
    public AstNode ConvertToCNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var optimizer = new ExpressionOptimizer();
        node = optimizer.Optimize(node);

        return DistributeOrOverAnd(node);
    }

    public AstNode ConvertToDNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        return DistributeAndOverOr(node);
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
}