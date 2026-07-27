using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Distribution of AND over OR and its dual. Deliberately NOT part of the rewrite
/// pipeline: distribution grows the tree (potentially exponentially) while the pipeline
/// minimizes size. <see cref="ExpandOnce" /> performs a single bounded bottom-up
/// distribution pass (used by the engine's expand-reduce strategy);
/// <see cref="ToDnf" />/<see cref="ToCnf" /> distribute exhaustively under a step budget
/// (used by the normal-form converter) and throw <see cref="InvalidOperationException" />
/// when the budget is exceeded.
/// </summary>
internal sealed class DistributiveExpander
{
    private readonly FormulaFactory _factory;
    private readonly int _maxDistributionSteps;
    private int _steps;

    public DistributiveExpander(FormulaFactory factory, int maxDistributionSteps = int.MaxValue)
    {
        _factory = factory;
        _maxDistributionSteps = maxDistributionSteps;
    }

    /// <summary>
    /// One bottom-up distribution pass: at each AND node the first OR operand is
    /// distributed over (dually for OR over AND); the freshly created children are not
    /// re-expanded. Repeated calls converge to a normal form, a single call performs
    /// exactly one level of expansion per node.
    /// </summary>
    public AstNode ExpandOnce(AstNode node)
    {
        switch (node)
        {
            case AndNode and:
                {
                    var operands = and.Operands.Select(ExpandOnce).ToList();
                    var orIndex = operands.FindIndex(o => o is OrNode);
                    if (orIndex < 0) return _factory.And(operands);

                    var orOperand = (OrNode)operands[orIndex];
                    var rest = operands.Where((_, k) => k != orIndex).ToList();
                    return _factory.Or(orOperand.Operands.Select(term => _factory.And(rest.Append(term))));
                }
            case OrNode or:
                {
                    var operands = or.Operands.Select(ExpandOnce).ToList();
                    var andIndex = operands.FindIndex(o => o is AndNode);
                    if (andIndex < 0) return _factory.Or(operands);

                    var andOperand = (AndNode)operands[andIndex];
                    var rest = operands.Where((_, k) => k != andIndex).ToList();
                    return _factory.And(andOperand.Operands.Select(factor => _factory.Or(rest.Append(factor))));
                }
            case NotNode not:
                return _factory.Not(ExpandOnce(not.Operand));
            default:
                return node;
        }
    }

    /// <summary>
    /// Full distribution of AND over OR into a disjunction of conjunctions. Tautological
    /// and contradictory combinations vanish through the factory's construction-time
    /// folding, so the result needs no separate simplification pass.
    /// </summary>
    public AstNode ToDnf(AstNode node)
    {
        _steps = 0;
        return DnfRecursive(node);
    }

    /// <summary>Full distribution of OR over AND into a conjunction of clauses (dual of <see cref="ToDnf" />).</summary>
    public AstNode ToCnf(AstNode node)
    {
        _steps = 0;
        return CnfRecursive(node);
    }

    private AstNode DnfRecursive(AstNode node)
    {
        switch (node)
        {
            case OrNode or:
                return _factory.Or(or.Operands.Select(DnfRecursive));
            case AndNode and:
                {
                    // DNF of a conjunction: cross product of the operands' term lists
                    var termChoices = and.Operands
                        .Select(o => TermsOf(DnfRecursive(o)))
                        .ToList();
                    return _factory.Or(CrossProduct(termChoices, (a, b) => _factory.And(a, b), _factory.True));
                }
            case NotNode not when not.Operand is NaryNode:
                // Non-NNF input (possible after a soundness rollback): push the negation down
                return DnfRecursive(AstPrimitives.NegateNnf(not.Operand, _factory));
            default:
                return node;
        }
    }

    private AstNode CnfRecursive(AstNode node)
    {
        switch (node)
        {
            case AndNode and:
                return _factory.And(and.Operands.Select(CnfRecursive));
            case OrNode or:
                {
                    // CNF of a disjunction: cross product of the operands' clause lists
                    var clauseChoices = or.Operands
                        .Select(o => ClausesOf(CnfRecursive(o)))
                        .ToList();
                    return _factory.And(CrossProduct(clauseChoices, (a, b) => _factory.Or(a, b), _factory.False));
                }
            case NotNode not when not.Operand is NaryNode:
                return CnfRecursive(AstPrimitives.NegateNnf(not.Operand, _factory));
            default:
                return node;
        }
    }

    private static IReadOnlyList<AstNode> TermsOf(AstNode dnf)
    {
        return dnf is OrNode or ? or.Operands : new[] { dnf };
    }

    private static IReadOnlyList<AstNode> ClausesOf(AstNode cnf)
    {
        return cnf is AndNode and ? and.Operands : new[] { cnf };
    }

    private List<AstNode> CrossProduct(List<IReadOnlyList<AstNode>> choices,
        Func<AstNode, AstNode, AstNode> combine, AstNode identity)
    {
        // Partials fold through the factory as they grow, so contradictory combinations
        // collapse to a constant immediately instead of accumulating dead structure
        var partials = new List<AstNode> { identity };
        foreach (var options in choices)
        {
            var next = new List<AstNode>(partials.Count * options.Count);
            foreach (var partial in partials)
                foreach (var option in options)
                {
                    CountStep();
                    next.Add(combine(partial, option));
                }

            partials = next;
        }

        return partials;
    }

    private void CountStep()
    {
        if (++_steps > _maxDistributionSteps)
            throw new InvalidOperationException(
                $"Normal form conversion aborted: expression expands beyond {_maxDistributionSteps} distribution steps");
    }
}
