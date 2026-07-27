using System.Collections.Generic;
using System.Linq;
using static LogicalOptimizer.Rewrite.AstPrimitives;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Absorption laws over the node's operand list:
///   standard   A | A&amp;B → A            and dually  A &amp; (A|B) → A
///   extended   A | !A&amp;B → A | B       and dually  A &amp; (!A|B) → A &amp; B
/// The complement check is polarity-agnostic (!X | X&amp;Y → !X | Y also fires) and
/// deep (a compound absorber like a&amp;b cancels a De Morgan-spread !a|!b factor).
/// </summary>
internal sealed class AbsorptionRule : IRewriteRule
{
    public string Name => "Absorption";

    public bool GuardAgainstGrowth => false;

    public AstNode? TryRewrite(AstNode node, FormulaFactory factory)
    {
        switch (node)
        {
            case OrNode or:
                {
                    var result = factory.Or(AbsorbDisjunction(or.Operands.ToList(), factory));
                    return result.Equals(node) ? null : result;
                }
            case AndNode and:
                {
                    var result = factory.And(AbsorbConjunction(and.Operands.ToList(), factory));
                    return result.Equals(node) ? null : result;
                }
            default:
                return null;
        }
    }

    /// <summary>Absorption inside OR: drop subsumed terms, cancel complementary factors.</summary>
    private static List<AstNode> AbsorbDisjunction(List<AstNode> terms, FormulaFactory factory)
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
                var factors = andTerm.Operands;

                for (var j = 0; j < result.Count && !changed; j++)
                {
                    if (i == j) continue;
                    var absorber = result[j];

                    var remaining = factors.Where(f => !AreComplementaryDeep(f, absorber, factory)).ToList();
                    if (remaining.Count == factors.Count) continue;

                    result[i] = remaining.Count == 0 ? factory.True : factory.And(remaining);
                    changed = true;
                }
            }

            if (changed) result = RemoveAbsorbedTerms(result);
        }

        return result;
    }

    /// <summary>Dual absorption inside AND: drop subsumed clauses, cancel complementary literals.</summary>
    private static List<AstNode> AbsorbConjunction(List<AstNode> clauses, FormulaFactory factory)
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
                var literals = orClause.Operands;

                for (var j = 0; j < result.Count && !changed; j++)
                {
                    if (i == j) continue;
                    var absorber = result[j];

                    var remaining = literals.Where(d => !AreComplementaryDeep(d, absorber, factory)).ToList();
                    if (remaining.Count == literals.Count) continue;

                    result[i] = remaining.Count == 0 ? factory.False : factory.Or(remaining);
                    changed = true;
                }
            }

            if (changed) result = RemoveAbsorbedClauses(result);
        }

        return result;
    }
}
