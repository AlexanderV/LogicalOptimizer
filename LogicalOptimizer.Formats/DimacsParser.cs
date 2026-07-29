namespace LogicalOptimizer;

/// <summary>
///     Streaming parser for the DIMACS CNF format: <c>c</c> comment lines, a
///     <c>p cnf &lt;nvars&gt; &lt;nclauses&gt;</c> header, and clauses of space-separated
///     non-zero signed integers each terminated by <c>0</c> (a clause may span several lines).
///     Reads line by line from the <see cref="TextReader" /> and never materializes the whole
///     input; oversized variable indices or an overrun of
///     <see cref="ResourceBudget.ParseTokenLimit" /> raise
///     <see cref="ComputationBudgetExceededException" />, and any other malformation raises
///     <see cref="FormatParseException" /> with the offending line/column.
/// </summary>
public static class DimacsParser
{
    public static CnfProblem Parse(TextReader reader, ResourceBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        budget ??= ResourceBudget.Default;

        var clauses = new List<int[]>();
        var current = new List<int>();
        var declaredVars = -1;
        var maxVar = 0;

        using var tokens = FormatScanner.Scan(reader, 'c', budget, cancellationToken).GetEnumerator();
        while (tokens.MoveNext())
        {
            var token = tokens.Current;
            if (token.Text == "p")
            {
                declaredVars = ReadHeader(tokens, token);
                continue;
            }

            var value = FormatScanner.ParseInteger(token, "a clause literal or 0");
            if (value == 0)
            {
                clauses.Add(current.ToArray());
                current.Clear();
                continue;
            }

            var literal = FormatScanner.RequireLiteral(value, token);
            current.Add(literal);
            var variable = Math.Abs(literal);
            if (variable > maxVar) maxVar = variable;
        }

        // Tolerate a final clause that omits its terminating 0 rather than crashing.
        if (current.Count > 0) clauses.Add(current.ToArray());

        var variableCount = Math.Max(declaredVars < 0 ? 0 : declaredVars, maxVar);
        return new CnfProblem(variableCount, clauses.ToArray());
    }

    private static int ReadHeader(IEnumerator<Token> tokens, Token pToken)
    {
        var format = Next(tokens, pToken, "the CNF format keyword");
        if (!format.Text.Equals("cnf", StringComparison.OrdinalIgnoreCase))
            throw new FormatParseException($"Expected 'cnf' header but found '{format.Text}'",
                format.Line, format.Column);

        var varsToken = Next(tokens, format, "the variable count");
        var declaredVars = FormatScanner.ParseInteger(varsToken, "the variable count");
        if (declaredVars < 0)
            throw new FormatParseException($"Variable count cannot be negative ({declaredVars})",
                varsToken.Line, varsToken.Column);
        if (declaredVars > FormatScanner.MaxVariableIndex)
            throw new ComputationBudgetExceededException(
                $"Declared variable count {declaredVars} exceeds the supported maximum of " +
                $"{FormatScanner.MaxVariableIndex:N0}");

        var clausesToken = Next(tokens, varsToken, "the clause count");
        FormatScanner.ParseInteger(clausesToken, "the clause count"); // validated; the value is advisory
        return (int)declaredVars;
    }

    private static Token Next(IEnumerator<Token> tokens, Token previous, string what)
    {
        if (!tokens.MoveNext())
            throw new FormatParseException($"Unexpected end of input; expected {what}",
                previous.Line, previous.Column);
        return tokens.Current;
    }
}
