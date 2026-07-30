using System;

namespace LogicalOptimizer;

/// <summary>
///     Thrown when a string is not a valid boolean expression. Derives from
///     <see cref="ArgumentException" /> for backward compatibility, and exposes a structured
///     <see cref="Diagnostic" /> (position, length, expected tokens, machine-readable code and a
///     caret snippet). Use <see cref="FormulaFactory.TryParse" /> to handle invalid input without
///     exceptions.
/// </summary>
public sealed class FormulaParseException : ArgumentException
{
    internal FormulaParseException(ParseDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    /// <summary>Structured information about the parse failure.</summary>
    public ParseDiagnostic Diagnostic { get; }
}
