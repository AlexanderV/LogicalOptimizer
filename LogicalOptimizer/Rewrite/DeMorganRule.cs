namespace LogicalOptimizer.Rewrite;

/// <summary>
/// NNF normalization rule: a negation over a compound operand is pushed down through
/// De Morgan's laws (!(A &amp; B) → !A | !B, !(A | B) → !A &amp; !B). Double negation
/// and negated constants never reach the rule — the factory folds them at construction.
/// </summary>
internal sealed class DeMorganRule : IRewriteRule
{
    public string Name => "DeMorgan";

    public bool GuardAgainstGrowth => false;

    public AstNode? TryRewrite(AstNode node, FormulaFactory factory)
    {
        if (node is not NotNode { Operand: NaryNode } notNode) return null;

        // NegateNnf(X) ≡ !X, which is exactly this node, pushed fully into NNF
        return AstPrimitives.NegateNnf(notNode.Operand, factory);
    }
}
