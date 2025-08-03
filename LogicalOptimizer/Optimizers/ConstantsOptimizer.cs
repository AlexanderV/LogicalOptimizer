using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for constants simplification and double negation removal
/// </summary>
public class ConstantsOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        node = SimplifyDoubleNegation(node);
        return SimplifyConstants(node);
    }

    private AstNode SimplifyDoubleNegation(AstNode node)
    {
        if (node is NotNode notNode && notNode.Operand is NotNode innerNot)
            return SimplifyDoubleNegation(innerNot.Operand);

        if (node is BinaryNode binaryNode)
        {
            var left = SimplifyDoubleNegation(binaryNode.Left);
            var right = SimplifyDoubleNegation(binaryNode.Right);

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

        if (node is NotNode singleNot)
            return new NotNode(SimplifyDoubleNegation(singleNot.Operand));

        return node;
    }

    private AstNode SimplifyConstants(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = SimplifyConstants(andNode.Left);
            var right = SimplifyConstants(andNode.Right);

            if (IsFalse(left) || IsFalse(right)) return CreateFalse();
            if (IsTrue(left)) return right;
            if (IsTrue(right)) return left;
            if (AreComplementary(left, right)) return CreateFalse();

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is OrNode orNode)
        {
            var left = SimplifyConstants(orNode.Left);
            var right = SimplifyConstants(orNode.Right);

            if (IsTrue(left) || IsTrue(right)) return CreateTrue();
            if (IsFalse(left)) return right;
            if (IsFalse(right)) return left;
            if (AreComplementary(left, right)) return CreateTrue();

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
        {
            var operand = SimplifyConstants(notNode.Operand);
            if (IsTrue(operand)) return CreateFalse();
            if (IsFalse(operand)) return CreateTrue();
            return new NotNode(operand);
        }

        return node;
    }
}
