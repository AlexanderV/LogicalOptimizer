namespace LogicalOptimizer;

/// <summary>
///     Raised when an exact algorithm gives up because it hit an explicit work budget
///     (e.g. Quine–McCluskey prime-generation pair-comparison limit) rather than because
///     of a programming error. It derives from <see cref="InvalidOperationException" /> for
///     backward compatibility with callers that caught the broader type, but its distinct
///     identity lets the facade tell a budget exhaustion (report
///     <c>MinimizationStatus.BudgetExceeded</c>) apart from a genuine invariant violation,
///     which must never be silently swallowed.
/// </summary>
public sealed class ComputationBudgetExceededException : InvalidOperationException
{
    public ComputationBudgetExceededException(string message) : base(message)
    {
    }

    public ComputationBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
