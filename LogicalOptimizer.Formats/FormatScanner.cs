using System.Globalization;

namespace LogicalOptimizer;

/// <summary>A whitespace-delimited token with its 1-based source position.</summary>
internal readonly record struct Token(string Text, int Line, int Column);

/// <summary>
///     Shared, streaming, budget-aware tokenizer for the line-oriented DIMACS/WCNF/OPB
///     formats. Reads one line at a time from the <see cref="TextReader" /> (never the whole
///     file), skips blank and comment lines (a line whose first non-whitespace character is
///     <c>commentChar</c>), and yields the remaining whitespace-delimited tokens with their
///     positions. Every yielded token counts against
///     <see cref="ResourceBudget.ParseTokenLimit" /> and the cancellation token is observed
///     once per line, so a hostile stream can neither hang nor allocate without bound.
/// </summary>
internal static class FormatScanner
{
    /// <summary>
    ///     Hard cap on any variable index the parsers accept. Bounds the array allocation the
    ///     downstream solver performs for a declared variable count, independent of the token
    ///     budget (a two-token stream can still declare a billion variables).
    /// </summary>
    public const int MaxVariableIndex = 10_000_000;

    public static IEnumerable<Token> Scan(TextReader reader, char commentChar,
        ResourceBudget budget, CancellationToken cancellationToken)
    {
        long tokenCount = 0;
        var lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            var i = 0;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length || line[i] == commentChar) continue;

            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                var start = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;

                tokenCount++;
                if (tokenCount > budget.ParseTokenLimit)
                    throw new ComputationBudgetExceededException(
                        $"Input exceeds the parser token budget of {budget.ParseTokenLimit:N0} tokens");

                yield return new Token(line.Substring(start, i - start), lineNumber, start + 1);
            }
        }
    }

    /// <summary>Parse a token as a base-10 integer; a non-integer or overflow is a syntax error.</summary>
    public static long ParseInteger(Token token, string what)
    {
        if (long.TryParse(token.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out var value))
            return value;
        throw new FormatParseException($"Expected {what} but found '{token.Text}'", token.Line, token.Column);
    }

    /// <summary>Reject a positive variable index outside <c>[1, MaxVariableIndex]</c> as a resource guard.</summary>
    public static int RequireVariableIndex(long value, Token token)
    {
        if (value is < 1 or > MaxVariableIndex)
            throw new ComputationBudgetExceededException(
                $"Variable index {value} at line {token.Line} exceeds the supported range " +
                $"[1, {MaxVariableIndex:N0}]");
        return (int)value;
    }

    /// <summary>Reject a signed literal whose magnitude lies outside <c>[1, MaxVariableIndex]</c>.</summary>
    public static int RequireLiteral(long value, Token token)
    {
        if (value < -MaxVariableIndex || value > MaxVariableIndex || value == 0)
            throw new ComputationBudgetExceededException(
                $"Literal {value} at line {token.Line} exceeds the supported variable range " +
                $"[1, {MaxVariableIndex:N0}]");
        return (int)value;
    }
}
