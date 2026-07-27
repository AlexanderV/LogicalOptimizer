using LogicalOptimizer.Rewrite;

namespace LogicalOptimizer;

/// <summary>
///     Converts expressions to conjunctive (CNF) and disjunctive (DNF) normal forms.
///     Conversion may blow up exponentially, so the number of distribution rewrites
///     is capped; exceeding the cap throws <see cref="InvalidOperationException" />.
///     Tautological clauses, contradictory terms and duplicates vanish through the
///     factory's construction-time folding during distribution.
/// </summary>
internal class NormalFormConverter
{
    private const int MaxDistributionCalls = 10_000;

    public AstNode ConvertToCNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        // Optimization also pushes negations inward (De Morgan), which distribution requires
        var factory = new FormulaFactory();
        node = new RewriteEngine(factory).Optimize(node);

        return new DistributiveExpander(factory, MaxDistributionCalls).ToCnf(node);
    }

    public AstNode ConvertToDNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var factory = new FormulaFactory();
        node = new RewriteEngine(factory).Optimize(node);

        // Only distribution and construction-time folding here: full optimization
        // could break the normal form
        return new DistributiveExpander(factory, MaxDistributionCalls).ToDnf(node);
    }
}
