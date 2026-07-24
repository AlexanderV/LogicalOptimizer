namespace LogicalOptimizer;

/// <summary>
///     Pluggable equivalence backend. The core library ships two implementations:
///     <see cref="HybridEquivalenceChecker" /> (truth table small / SAT miter large) and
///     <see cref="BddEquivalenceChecker" /> (canonical diagrams, best for repeated queries).
///     External adapters (e.g. an optional Z3 package) implement the same contract for
///     production verification at scales beyond the built-in budgets.
/// </summary>
public interface IEquivalenceChecker
{
    EquivalenceCheckResult Check(AstNode left, AstNode right, CancellationToken cancellationToken = default);
}

/// <summary>
///     Default backend: exhaustive truth-table comparison up to the guard range, SAT-based
///     miter proof beyond it. Verdicts are exact; Unknown appears only when the conflict
///     budget runs out on very large instances.
/// </summary>
public sealed class HybridEquivalenceChecker : IEquivalenceChecker
{
    private readonly int _maxConflicts;

    public HybridEquivalenceChecker(int maxConflicts = EquivalenceChecker.DefaultMaxConflicts)
    {
        _maxConflicts = maxConflicts;
    }

    public EquivalenceCheckResult Check(AstNode left, AstNode right, CancellationToken cancellationToken = default)
    {
        return EquivalenceChecker.Check(left, right, _maxConflicts, cancellationToken);
    }
}

/// <summary>
///     BDD backend: builds both sides in one manager and compares canonical roots.
///     Returns Unknown when the node budget is exceeded (no counterexample extraction).
/// </summary>
public sealed class BddEquivalenceChecker : IEquivalenceChecker
{
    private readonly int _nodeBudget;

    public BddEquivalenceChecker(int nodeBudget = BinaryDecisionDiagram.DefaultNodeBudget)
    {
        _nodeBudget = nodeBudget;
    }

    public EquivalenceCheckResult Check(AstNode left, AstNode right, CancellationToken cancellationToken = default)
    {
        var variables = left.GetVariables();
        variables.UnionWith(right.GetVariables());
        var manager = new BinaryDecisionDiagram(variables, _nodeBudget, cancellationToken);

        try
        {
            if (manager.FromAst(left) == manager.FromAst(right))
                return EquivalenceCheckResult.Equivalent();
        }
        catch (InvalidOperationException)
        {
            return EquivalenceCheckResult.Unknown();
        }

        // Roots differ: canonical form guarantees non-equivalence. Extract a
        // counterexample from the XOR of both functions (any path to the true terminal).
        var difference = manager.Ite(manager.FromAst(left),
            manager.Negate(manager.FromAst(right)), manager.FromAst(right));
        return EquivalenceCheckResult.NotEquivalent(manager.FindSatisfyingAssignment(difference));
    }
}
