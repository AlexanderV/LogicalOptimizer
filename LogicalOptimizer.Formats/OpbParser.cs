using System.Globalization;

namespace LogicalOptimizer;

/// <summary>
///     Streaming parser for the OPB (pseudo-Boolean) format: <c>*</c> comment lines
///     (including the <c>* #variable= N #constraint= M</c> header), an optional linear
///     objective <c>min: +c1 x1 -c2 x2 ... ;</c>, and linear constraints
///     <c>+c1 x1 -c2 x2 ... OP b ;</c> with <c>OP</c> one of <c>&gt;=</c>, <c>&lt;=</c>,
///     <c>=</c>. Variables are written <c>x&lt;n&gt;</c> and may be negated as
///     <c>~x&lt;n&gt;</c>; a statement may span several lines up to its terminating <c>;</c>.
///     Reads line by line and never materializes the whole input; budget/variable overruns
///     raise <see cref="ComputationBudgetExceededException" /> and other malformation raises
///     <see cref="FormatParseException" />.
/// </summary>
public static class OpbParser
{
    public static PseudoBooleanProblem Parse(TextReader reader, ResourceBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        budget ??= ResourceBudget.Default;

        var constraints = new List<PseudoBooleanConstraint>();
        (long Coefficient, int Literal)[]? objective = null;
        var declaredVars = -1;
        var maxVar = 0;
        long tokenCount = 0;

        var statement = new List<Token>();
        var lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            var i = 0;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) continue;
            if (line[i] == '*')
            {
                TryReadHeader(line, ref declaredVars);
                continue;
            }

            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                var start = i;
                string text;
                if (line[i] == ';')
                {
                    i++;
                    text = ";";
                }
                else
                {
                    while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != ';') i++;
                    text = line.Substring(start, i - start);
                }

                tokenCount++;
                if (tokenCount > budget.ParseTokenLimit)
                    throw new ComputationBudgetExceededException(
                        $"Input exceeds the parser token budget of {budget.ParseTokenLimit:N0} tokens");

                if (text == ";")
                {
                    ProcessStatement(statement);
                    statement.Clear();
                }
                else
                {
                    statement.Add(new Token(text, lineNumber, start + 1));
                }
            }
        }

        if (statement.Count > 0) ProcessStatement(statement); // tolerate a missing final ';'

        var variableCount = Math.Max(declaredVars < 0 ? 0 : declaredVars, maxVar);
        return new PseudoBooleanProblem(variableCount, constraints.ToArray(), objective);

        void ProcessStatement(List<Token> stmt)
        {
            if (stmt.Count == 0) return;

            if (stmt[0].Text.Equals("min:", StringComparison.OrdinalIgnoreCase))
            {
                if (objective is not null)
                    throw new FormatParseException("Multiple objectives are not supported",
                        stmt[0].Line, stmt[0].Column);
                var index = 1;
                objective = ReadTerms(stmt, ref index, null).ToArray();
                if (index != stmt.Count)
                    throw new FormatParseException("Unexpected token in objective",
                        stmt[index].Line, stmt[index].Column);
                return;
            }

            var cursor = 0;
            var terms = ReadTerms(stmt, ref cursor, ComparisonOperators);
            if (cursor >= stmt.Count)
                throw new FormatParseException("Expected a comparison operator (>=, <=, =)",
                    stmt[^1].Line, stmt[^1].Column);

            var opToken = stmt[cursor++];
            var comparison = opToken.Text switch
            {
                ">=" => PseudoBooleanComparison.GreaterOrEqual,
                "<=" => PseudoBooleanComparison.LessOrEqual,
                "=" => PseudoBooleanComparison.Equal,
                _ => throw new FormatParseException(
                    $"Expected a comparison operator (>=, <=, =) but found '{opToken.Text}'",
                    opToken.Line, opToken.Column)
            };

            if (cursor >= stmt.Count)
                throw new FormatParseException("Expected a constraint bound", opToken.Line, opToken.Column);
            var boundToken = stmt[cursor++];
            var bound = FormatScanner.ParseInteger(boundToken, "the constraint bound");
            if (cursor != stmt.Count)
                throw new FormatParseException("Unexpected token after the constraint bound",
                    stmt[cursor].Line, stmt[cursor].Column);

            constraints.Add(new PseudoBooleanConstraint(terms.ToArray(), comparison, bound));
        }

        List<(long Coefficient, int Literal)> ReadTerms(List<Token> stmt, ref int index, string[]? stopWords)
        {
            var terms = new List<(long, int)>();
            while (index < stmt.Count)
            {
                var token = stmt[index];
                if (stopWords is not null && Array.IndexOf(stopWords, token.Text) >= 0) break;

                var coefficient = FormatScanner.ParseInteger(token, "a coefficient");
                index++;
                if (index >= stmt.Count)
                    throw new FormatParseException("Expected a variable after the coefficient",
                        token.Line, token.Column);

                var literal = ParseVariable(stmt[index]);
                index++;
                terms.Add((coefficient, literal));
                if (Math.Abs(literal) > maxVar) maxVar = Math.Abs(literal);
            }

            return terms;
        }

        int ParseVariable(Token token)
        {
            var text = token.Text;
            var negated = false;
            if (text.StartsWith('~'))
            {
                negated = true;
                text = text[1..];
            }

            if (!text.StartsWith('x') || text.Length < 2)
                throw new FormatParseException(
                    $"Expected a variable like x1 or ~x1 but found '{token.Text}'", token.Line, token.Column);

            if (!long.TryParse(text[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new FormatParseException(
                    $"Invalid variable index in '{token.Text}'", token.Line, token.Column);

            var index = FormatScanner.RequireVariableIndex(value, token);
            return negated ? -index : index;
        }
    }

    private static readonly string[] ComparisonOperators = { ">=", "<=", "=" };

    /// <summary>Extract a declared variable count from a <c>* #variable= N ...</c> header comment.</summary>
    private static void TryReadHeader(string line, ref int declaredVars)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].StartsWith("#variable=", StringComparison.Ordinal)) continue;

            var inline = parts[i]["#variable=".Length..];
            var number = inline.Length > 0
                ? inline
                : i + 1 < parts.Length
                    ? parts[i + 1]
                    : null;
            if (number is not null &&
                int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
                count <= FormatScanner.MaxVariableIndex)
                declaredVars = count;
            return;
        }
    }
}
