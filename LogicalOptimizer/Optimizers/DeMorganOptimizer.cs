namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for De Morgan's laws: !(A & B) = !A | !B, !(A | B) = !A & !B
/// </summary>
public class DeMorganOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplyDeMorganLaws(node);
    }

    private AstNode ApplyDeMorganLaws(AstNode node)
    {
        if (node is NotNode notNode)
        {
            if (notNode.Operand is AndNode andNode)
                return new OrNode(
                    ApplyDeMorganLaws(new NotNode(andNode.Left)),
                    ApplyDeMorganLaws(new NotNode(andNode.Right))
                );

            if (notNode.Operand is OrNode orNode)
                return new AndNode(
                    ApplyDeMorganLaws(new NotNode(orNode.Left)),
                    ApplyDeMorganLaws(new NotNode(orNode.Right))
                );

            return new NotNode(ApplyDeMorganLaws(notNode.Operand));
        }

        if (node is BinaryNode binaryNode)
        {
            var left = ApplyDeMorganLaws(binaryNode.Left);
            var right = ApplyDeMorganLaws(binaryNode.Right);

            if (node is AndNode originalAnd)
            {
                var result = new AndNode(left, right);
                result.ForceParentheses = originalAnd.ForceParentheses;
                return result;
            }

            if (node is OrNode originalOr)
            {
                var result = new OrNode(left, right);
                result.ForceParentheses = originalOr.ForceParentheses;
                return result;
            }
        }

        return node;
    }
}
