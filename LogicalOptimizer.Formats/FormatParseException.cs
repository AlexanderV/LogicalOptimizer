namespace LogicalOptimizer;

/// <summary>
///     Raised when a standard-format stream (DIMACS CNF, WCNF or OPB) is syntactically
///     malformed. Carries the 1-based <see cref="Line" /> and <see cref="Column" /> of the
///     offending token so callers can point at the exact position in the input. A resource
///     limit being hit is reported separately as
///     <see cref="ComputationBudgetExceededException" />, never as this type.
/// </summary>
public sealed class FormatParseException : Exception
{
    public FormatParseException(string message, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
    }

    /// <summary>1-based line number of the offending token.</summary>
    public int Line { get; }

    /// <summary>1-based column of the offending token within its line.</summary>
    public int Column { get; }
}
