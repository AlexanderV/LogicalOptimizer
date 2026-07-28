namespace LogicalOptimizer;

/// <summary>
///     Raised when a decision-diagram / knowledge-compilation build gives up because it hit
///     an explicit node budget (BDD or d-DNNF), rather than because of a programming error.
///     It derives from <see cref="InvalidOperationException" /> for backward compatibility with
///     callers that caught the broader type, but its distinct identity lets a fallback path
///     (try the next variable order, return an Unknown verdict) tell a budget exhaustion apart
///     from a genuine invariant violation, which must never be silently swallowed.
/// </summary>
public sealed class NodeBudgetExceededException : InvalidOperationException
{
    public NodeBudgetExceededException(string message) : base(message)
    {
    }

    public NodeBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
