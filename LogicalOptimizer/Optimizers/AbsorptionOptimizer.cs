using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for absorption laws: A & (A | B) = A, A | (A & B) = A, and extended versions
/// </summary>
public class AbsorptionOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplyAbsorptionLaws(node);
    }

    private AstNode ApplyAbsorptionLaws(AstNode node)
    {
        if (node is AndNode andNode)
        {
            // Standard absorption: A & (A | B) → A
            if (andNode.Right is OrNode rightOr && AreEqual(andNode.Left, rightOr.Left))
                return ApplyAbsorptionLaws(andNode.Left);
            if (andNode.Right is OrNode rightOr2 && AreEqual(andNode.Left, rightOr2.Right))
                return ApplyAbsorptionLaws(andNode.Left);
            if (andNode.Left is OrNode leftOr && AreEqual(andNode.Right, leftOr.Left))
                return ApplyAbsorptionLaws(andNode.Right);
            if (andNode.Left is OrNode leftOr2 && AreEqual(andNode.Right, leftOr2.Right))
                return ApplyAbsorptionLaws(andNode.Right);

            // Extended absorption: A & (!A | B) → A & B
            if (andNode.Right is OrNode rightOrExt)
            {
                if (rightOrExt.Left is NotNode leftNot && AreEqual(andNode.Left, leftNot.Operand))
                {
                    // A & (!A | B) → A & B
                    var result = new AndNode(andNode.Left, ApplyAbsorptionLaws(rightOrExt.Right));
                    result.ForceParentheses = andNode.ForceParentheses;
                    return result;
                }

                if (rightOrExt.Right is NotNode rightNot && AreEqual(andNode.Left, rightNot.Operand))
                {
                    // A & (B | !A) → A & B  
                    var result = new AndNode(andNode.Left, ApplyAbsorptionLaws(rightOrExt.Left));
                    result.ForceParentheses = andNode.ForceParentheses;
                    return result;
                }
            }

            if (AreEqual(andNode.Left, andNode.Right))
                return ApplyAbsorptionLaws(andNode.Left);

            var finalResult = new AndNode(ApplyAbsorptionLaws(andNode.Left), ApplyAbsorptionLaws(andNode.Right));
            finalResult.ForceParentheses = andNode.ForceParentheses;
            return finalResult;
        }

        if (node is OrNode orNode)
        {
            // Standard absorption: A | (A & B) → A
            if (orNode.Right is AndNode rightAnd && AreEqual(orNode.Left, rightAnd.Left))
                return ApplyAbsorptionLaws(orNode.Left);
            if (orNode.Right is AndNode rightAnd2 && AreEqual(orNode.Left, rightAnd2.Right))
                return ApplyAbsorptionLaws(orNode.Left);
            if (orNode.Left is AndNode leftAnd && AreEqual(orNode.Right, leftAnd.Left))
                return ApplyAbsorptionLaws(orNode.Right);
            if (orNode.Left is AndNode leftAnd2 && AreEqual(orNode.Right, leftAnd2.Right))
                return ApplyAbsorptionLaws(orNode.Right);

            // Extended absorption: A | (!A & B) → A | B
            if (orNode.Right is AndNode rightAndExt)
            {
                if (rightAndExt.Left is NotNode leftNot && AreEqual(orNode.Left, leftNot.Operand))
                {
                    // A | (!A & B) → A | B
                    var result = new OrNode(orNode.Left, ApplyAbsorptionLaws(rightAndExt.Right));
                    result.ForceParentheses = orNode.ForceParentheses;
                    return result;
                }

                if (rightAndExt.Right is NotNode rightNot && AreEqual(orNode.Left, rightNot.Operand))
                {
                    // A | (B & !A) → A | B
                    var result = new OrNode(orNode.Left, ApplyAbsorptionLaws(rightAndExt.Left));
                    result.ForceParentheses = orNode.ForceParentheses;
                    return result;
                }
            }

            if (AreEqual(orNode.Left, orNode.Right))
                return ApplyAbsorptionLaws(orNode.Left);

            var finalResult = new OrNode(ApplyAbsorptionLaws(orNode.Left), ApplyAbsorptionLaws(orNode.Right));
            finalResult.ForceParentheses = orNode.ForceParentheses;
            return finalResult;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyAbsorptionLaws(notNode.Operand));

        return node;
    }
}
