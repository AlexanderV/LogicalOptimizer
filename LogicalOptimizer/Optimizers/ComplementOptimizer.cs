using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for complement laws: A & !A = 0, A | !A = 1
/// </summary>
internal class ComplementOptimizer : IOptimizer
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

            // Compound complement: (a | b) & !a & !b = 0
            // (De Morgan has already expanded !(a | b) into separate !a & !b terms)
            foreach (var term in terms)
                if (term is OrNode orTerm &&
                    FlattenOr(orTerm).All(literal => terms.Any(other => AreComplementary(literal, other))))
                    return CreateFalse();

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

            // Compound complement: (a & b) | !a | !b = 1
            // (De Morgan has already expanded !(a & b) into separate !a | !b terms)
            foreach (var term in terms)
                if (term is AndNode andTerm &&
                    FlattenAnd(andTerm).All(factor => terms.Any(other => AreComplementary(factor, other))))
                    return CreateTrue();

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyComplementLaws(notNode.Operand));

        return node;
    }
}
