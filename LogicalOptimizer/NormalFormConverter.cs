using LogicalOptimizer.Optimizers;

namespace LogicalOptimizer;

/// <summary>
///     Converts expressions to conjunctive (CNF) and disjunctive (DNF) normal forms.
///     Conversion may blow up exponentially, so the number of distribution rewrites
///     is capped; exceeding the cap throws <see cref="InvalidOperationException" />.
/// </summary>
internal class NormalFormConverter
{
    private const int MaxDistributionCalls = 10_000;

    private int _distributionCalls;

    public AstNode ConvertToCNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        // Optimization also pushes negations inward (De Morgan), which distribution requires
        node = new ExpressionOptimizer().Optimize(node);

        _distributionCalls = 0;
        var cnf = DistributeOrOverAnd(node);
        return SimplifyTautologies(cnf);
    }

    public AstNode ConvertToDNF(AstNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        node = new ExpressionOptimizer().Optimize(node);

        _distributionCalls = 0;
        var dnf = DistributeAndOverOr(node);

        // Only basic simplifications here: full optimization could break the normal form
        return RemoveDuplicateTerms(dnf);
    }

    private void CountDistribution()
    {
        if (++_distributionCalls > MaxDistributionCalls)
            throw new InvalidOperationException(
                $"Normal form conversion aborted: expression expands beyond {MaxDistributionCalls} distribution steps");
    }

    private AstNode DistributeOrOverAnd(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var left = DistributeOrOverAnd(orNode.Left);
            var right = DistributeOrOverAnd(orNode.Right);

            // A | (B & C) = (A | B) & (A | C)
            if (right is AndNode rightAnd)
            {
                CountDistribution();
                return new AndNode(
                    DistributeOrOverAnd(new OrNode(left, rightAnd.Left)),
                    DistributeOrOverAnd(new OrNode(left, rightAnd.Right))
                );
            }

            // (A & B) | C = (A | C) & (B | C)
            if (left is AndNode leftAnd)
            {
                CountDistribution();
                return new AndNode(
                    DistributeOrOverAnd(new OrNode(leftAnd.Left, right)),
                    DistributeOrOverAnd(new OrNode(leftAnd.Right, right))
                );
            }

            return new OrNode(left, right);
        }

        if (node is AndNode andNode)
            return new AndNode(DistributeOrOverAnd(andNode.Left), DistributeOrOverAnd(andNode.Right));

        if (node is NotNode notNode)
            return new NotNode(DistributeOrOverAnd(notNode.Operand));

        return node;
    }

    private AstNode DistributeAndOverOr(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = DistributeAndOverOr(andNode.Left);
            var right = DistributeAndOverOr(andNode.Right);

            // (A | B) & C = A & C | B & C
            if (left is OrNode leftOr)
            {
                CountDistribution();
                return new OrNode(
                    DistributeAndOverOr(new AndNode(leftOr.Left, right)),
                    DistributeAndOverOr(new AndNode(leftOr.Right, right))
                );
            }

            // A & (B | C) = A & B | A & C
            if (right is OrNode rightOr)
            {
                CountDistribution();
                return new OrNode(
                    DistributeAndOverOr(new AndNode(left, rightOr.Left)),
                    DistributeAndOverOr(new AndNode(left, rightOr.Right))
                );
            }

            return new AndNode(left, right);
        }

        if (node is OrNode orNode)
            return new OrNode(DistributeAndOverOr(orNode.Left), DistributeAndOverOr(orNode.Right));

        if (node is NotNode notNode)
            return new NotNode(DistributeAndOverOr(notNode.Operand));

        return node;
    }

    private AstNode SimplifyTautologies(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = SimplifyTautologies(andNode.Left);
            var right = SimplifyTautologies(andNode.Right);

            if (IsTautology(left) || AstUtilities.IsTrue(left)) return right;
            if (IsTautology(right) || AstUtilities.IsTrue(right)) return left;

            if (IsContradiction(left) || AstUtilities.IsFalse(left) ||
                IsContradiction(right) || AstUtilities.IsFalse(right))
                return AstUtilities.CreateFalse();

            return new AndNode(left, right);
        }

        if (node is OrNode orNode)
        {
            var left = SimplifyTautologies(orNode.Left);
            var right = SimplifyTautologies(orNode.Right);

            if (IsTautology(left) || AstUtilities.IsTrue(left) ||
                IsTautology(right) || AstUtilities.IsTrue(right))
                return AstUtilities.CreateTrue();

            if (IsContradiction(left) || AstUtilities.IsFalse(left)) return right;
            if (IsContradiction(right) || AstUtilities.IsFalse(right)) return left;

            return new OrNode(left, right);
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyTautologies(notNode.Operand));

        return node;
    }

    private static bool IsTautology(AstNode node)
    {
        // A clause like (a | b | !a) is always true
        if (node is not OrNode orNode) return false;
        var literals = AstUtilities.FlattenOr(orNode);
        return ContainsComplementPair(literals);
    }

    private static bool IsContradiction(AstNode node)
    {
        // A term like (a & b & !a) is always false
        if (node is not AndNode andNode) return false;
        var literals = AstUtilities.FlattenAnd(andNode);
        return ContainsComplementPair(literals);
    }

    private static bool ContainsComplementPair(List<AstNode> literals)
    {
        for (var i = 0; i < literals.Count; i++)
            for (var j = i + 1; j < literals.Count; j++)
                if (AstUtilities.AreComplementary(literals[i], literals[j]))
                    return true;
        return false;
    }

    private static AstNode RemoveDuplicateTerms(AstNode node)
    {
        if (node is not OrNode orNode) return node;

        var terms = AstUtilities.FlattenOr(orNode)
            .Distinct(AstUtilities.NodeComparer.Instance)
            .ToList();

        return terms.Aggregate((left, right) => new OrNode(left, right));
    }
}
