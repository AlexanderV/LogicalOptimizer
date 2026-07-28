using LogicalOptimizer.Rewrite;

namespace LogicalOptimizer;

/// <summary>
///     Standalone formula transformations usable outside the full optimization pipeline.
///     Subsumption drops absorbed terms/clauses without any truth-table work, so it
///     applies at any scale.
/// </summary>
public static class Transformations
{
    /// <summary>
    ///     DNF subsumption: remove every term absorbed by a more general one
    ///     (a &amp; b | a → a). The input is treated as a disjunction of terms.
    /// </summary>
    public static AstNode SubsumeDnf(AstNode formula)
    {
        if (formula is not OrNode or) return formula;

        var terms = AstPrimitives.RemoveAbsorbedTerms(or.Operands);
        return terms.Count == 1 ? terms[0] : new OrNode(terms);
    }

    /// <summary>
    ///     CNF subsumption: remove every clause absorbed by a more general one
    ///     ((a | b) &amp; a → a). The input is treated as a conjunction of clauses.
    /// </summary>
    public static AstNode SubsumeCnf(AstNode formula)
    {
        if (formula is not AndNode and) return formula;

        var clauses = AstPrimitives.RemoveAbsorbedClauses(and.Operands);
        return clauses.Count == 1 ? clauses[0] : new AndNode(clauses);
    }

    /// <summary>
    ///     Espresso-style heuristic DNF minimization on cube lists (EXPAND → IRREDUNDANT
    ///     → REDUCE with exact cofactor-tautology validation) — no 2^n table, so it
    ///     applies far beyond the exact zone. The input must be a flat sum of products
    ///     over plain literals; anything else is returned unchanged. Sound by
    ///     construction: only cover-preserving steps are ever applied, and the step
    ///     budget merely stops further improvement.
    /// </summary>
    public static AstNode MinimizeDnfHeuristic(AstNode dnf,
        int stepLimit = EspressoLiteMinimizer.DefaultTautologyStepLimit,
        CancellationToken cancellationToken = default)
    {
        var parsed = EspressoLiteMinimizer.TryParseSop(dnf);
        if (parsed == null) return dnf;

        var (cover, variables) = parsed.Value;
        if (cover.Count == 0) return ConstantNode.False;

        var minimized = EspressoLiteMinimizer.Minimize(cover, variables.Count, stepLimit, cancellationToken);
        var rebuilt = EspressoLiteMinimizer.BuildSop(minimized, variables);

        // Never hand back something costlier than the input
        return AstMetrics.CountLiterals(rebuilt) <= AstMetrics.CountLiterals(dnf) ? rebuilt : dnf;
    }

    /// <summary>
    ///     Convert a formula to its Algebraic Normal Form (ANF / Zhegalkin / Reed–Muller
    ///     polynomial): the unique XOR of AND-monomials over the variables. Coefficients
    ///     come from the fast Möbius transform over the truth table, so this is 2^n work
    ///     capped at <see cref="TruthTable.MaxVariables" /> variables (an
    ///     <see cref="ArgumentException" /> is thrown beyond the cap) and honors the
    ///     supplied <paramref name="cancellationToken" />. The empty monomial renders as
    ///     the constant <c>1</c>; the everywhere-false function as the constant <c>0</c>.
    /// </summary>
    public static AstNode ToAlgebraicNormalForm(AstNode formula, CancellationToken cancellationToken = default)
    {
        return AlgebraicNormalFormConverter.Convert(formula, cancellationToken);
    }
}
