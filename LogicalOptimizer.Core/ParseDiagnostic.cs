using System;
using System.Collections.Generic;

namespace LogicalOptimizer;

/// <summary>
///     Structured, machine-readable description of a parse failure, produced by
///     <see cref="FormulaFactory.TryParse" /> and carried by <see cref="FormulaParseException" />.
///     It reports where the error is (<see cref="Position" />, <see cref="Length" />), a stable
///     <see cref="Code" />, the tokens that would have been valid (<see cref="Expected" />), and a
///     caret <see cref="Snippet" /> for display.
/// </summary>
public sealed class ParseDiagnostic
{
    private static readonly IReadOnlyList<string> NoneExpected = Array.Empty<string>();

    internal ParseDiagnostic(ParseErrorCode code, string message, int position, int length,
        string source, IReadOnlyList<string>? expected = null)
    {
        Code = code;
        Message = message;
        Position = position;
        Length = length;
        Source = source ?? "";
        Expected = expected ?? NoneExpected;
    }

    /// <summary>Stable machine-readable error classification.</summary>
    public ParseErrorCode Code { get; }

    /// <summary>Human-readable message (also the <see cref="Exception.Message" /> when thrown).</summary>
    public string Message { get; }

    /// <summary>Zero-based index into <see cref="Source" /> where the offending span begins.</summary>
    public int Position { get; }

    /// <summary>Length of the offending span; 0 at end of input (rendered as a single caret).</summary>
    public int Length { get; }

    /// <summary>Token descriptions that would have been valid at this position (may be empty).</summary>
    public IReadOnlyList<string> Expected { get; }

    /// <summary>The source text this diagnostic refers to (empty when unavailable).</summary>
    public string Source { get; }

    /// <summary>
    ///     A two-line caret snippet: the source, then a line pointing at the offending span
    ///     with <c>^</c>. Useful for terminals, logs and error UIs.
    /// </summary>
    public string Snippet
    {
        get
        {
            var pad = Position > 0 ? Position : 0;
            var carets = Length > 0 ? Length : 1;
            return Source + "\n" + new string(' ', pad) + new string('^', carets);
        }
    }

    public override string ToString()
    {
        return Message;
    }
}
