namespace LogicalOptimizer;

/// <summary>
///     Raised when a distribution-based normal-form conversion (equivalent CNF/DNF) is
///     abandoned because the expression expands past its distribution-step budget, rather
///     than because of a programming error. It derives from
///     <see cref="InvalidOperationException" /> for backward compatibility, but its distinct
///     identity lets callers fall back cleanly (mark the artifact TooLarge, or switch to the
///     linear-size Tseitin encoding) without also swallowing a genuine invariant violation.
/// </summary>
public sealed class NormalFormTooLargeException : InvalidOperationException
{
    public NormalFormTooLargeException(string message) : base(message)
    {
    }

    public NormalFormTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
