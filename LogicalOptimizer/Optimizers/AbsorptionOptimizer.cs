using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Optimizer for absorption laws over flattened term lists:
///   standard   A | A&amp;B → A            and dually  A &amp; (A|B) → A
///   extended   A | !A&amp;B → A | B       and dually  A &amp; (!A|B) → A &amp; B
/// The complement check is polarity-agnostic (!X | X&amp;Y → !X | Y also fires) and
/// deep (a compound absorber like a&amp;b cancels a De Morgan-spread !a|!b factor).
/// </summary>
internal class AbsorptionOptimizer : IOptimizer
{
    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        return ApplyAbsorptionLaws(node);
    }

    private AstNode ApplyAbsorptionLaws(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode).Select(ApplyAbsorptionLaws).ToList();
            terms = AbsorbDisjunction(terms);

            if (terms.Count == 0) return CreateFalse();
            if (terms.Count == 1) return terms[0];

            var result = terms.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var clauses = FlattenAnd(andNode).Select(ApplyAbsorptionLaws).ToList();
            clauses = AbsorbConjunction(clauses);

            if (clauses.Count == 0) return CreateTrue();
            if (clauses.Count == 1) return clauses[0];

            var result = clauses.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyAbsorptionLaws(notNode.Operand));

        return node;
    }

    /// <summary>Absorption inside OR: drop subsumed terms, cancel complementary factors.</summary>
    private static List<AstNode> AbsorbDisjunction(List<AstNode> terms)
    {
        var result = RemoveAbsorbedTerms(terms);

        var changed = true;
        while (changed)
        {
            changed = false;

            // Extended absorption: absorber A cancels a factor f ≡ !A inside another term
            for (var i = 0; i < result.Count && !changed; i++)
            {
                if (result[i] is not AndNode andTerm) continue;
                var factors = FlattenAnd(andTerm);

                for (var j = 0; j < result.Count && !changed; j++)
                {
                    if (i == j) continue;
                    var absorber = result[j];

                    var remaining = factors.Where(f => !AreComplementaryDeep(f, absorber)).ToList();
                    if (remaining.Count == factors.Count) continue;

                    result[i] = remaining.Count == 0
                        ? CreateTrue()
                        : remaining.Aggregate((a, b) => new AndNode(a, b));
                    changed = true;
                }
            }

            if (changed) result = RemoveAbsorbedTerms(result);
        }

        return result;
    }

    /// <summary>Dual absorption inside AND: drop subsumed clauses, cancel complementary literals.</summary>
    private static List<AstNode> AbsorbConjunction(List<AstNode> clauses)
    {
        var result = RemoveAbsorbedClauses(clauses);

        var changed = true;
        while (changed)
        {
            changed = false;

            // Extended absorption: absorber A cancels a literal d ≡ !A inside another clause
            for (var i = 0; i < result.Count && !changed; i++)
            {
                if (result[i] is not OrNode orClause) continue;
                var literals = FlattenOr(orClause);

                for (var j = 0; j < result.Count && !changed; j++)
                {
                    if (i == j) continue;
                    var absorber = result[j];

                    var remaining = literals.Where(d => !AreComplementaryDeep(d, absorber)).ToList();
                    if (remaining.Count == literals.Count) continue;

                    result[i] = remaining.Count == 0
                        ? CreateFalse()
                        : remaining.Aggregate((a, b) => new OrNode(a, b));
                    changed = true;
                }
            }

            if (changed) result = RemoveAbsorbedClauses(result);
        }

        return result;
    }
}
