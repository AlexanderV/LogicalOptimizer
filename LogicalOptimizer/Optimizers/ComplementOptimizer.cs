using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for complement laws: A & !A = 0, A | !A = 1
/// </summary>
public class ComplementOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplyComplementLaws(node);
    }

    private AstNode ApplyComplementLaws(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = ApplyComplementLaws(andNode.Left);
            var right = ApplyComplementLaws(andNode.Right);

            // Check direct complements
            if (AreComplementary(left, right))
                return CreateFalse();

            // Extended check for AND chains
            var terms = FlattenAnd(new AndNode(left, right));

            // Check all pairs of terms for complementarity
            for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
                if (AreComplementary(terms[i], terms[j]))
                    return CreateFalse(); // A & !A = 0

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is OrNode orNode)
        {
            var left = ApplyComplementLaws(orNode.Left);
            var right = ApplyComplementLaws(orNode.Right);

            // Check direct complements
            if (AreComplementary(left, right))
                return CreateTrue();

            // Extended check for OR chains
            var terms = FlattenOr(new OrNode(left, right));

            // Check all pairs of terms for complementarity
            for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
                if (AreComplementary(terms[i], terms[j]))
                    return CreateTrue(); // A | !A = 1

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyComplementLaws(notNode.Operand));

        return node;
    }
}
