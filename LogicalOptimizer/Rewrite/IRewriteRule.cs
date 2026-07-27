namespace LogicalOptimizer.Rewrite;

/// <summary>
/// A local rewrite rule applied by <see cref="RewriteEngine" /> during its bottom-up
/// traversal. Rules are strictly local: they inspect only the given node (whose children
/// the engine has already rebuilt and rewritten) and never recurse into subtrees.
/// Composite results must be built through the supplied <see cref="FormulaFactory" />
/// so they carry construction-time canonicalization and interning.
/// </summary>
internal interface IRewriteRule
{
    /// <summary>Rule name used as the metrics counter key.</summary>
    string Name { get; }

    /// <summary>
    /// Whether the engine must discard a rewrite that increases the expression cost
    /// (literal count, then node count; recorded as "{Name}_Rollback" in the metrics).
    /// </summary>
    bool GuardAgainstGrowth { get; }

    /// <summary>
    /// Try to rewrite the node. Returns the replacement, or null when the rule does not
    /// apply. Returning a node equal to the input counts as "no change".
    /// </summary>
    AstNode? TryRewrite(AstNode node, FormulaFactory factory);
}
