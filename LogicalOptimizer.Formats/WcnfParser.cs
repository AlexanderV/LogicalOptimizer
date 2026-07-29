namespace LogicalOptimizer;

/// <summary>
///     Streaming parser for the WCNF (weighted partial MaxSAT) format. Two dialects are
///     accepted, the classic one first and by default:
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Classic</b> — a <c>p wcnf &lt;nvars&gt; &lt;nclauses&gt; &lt;top&gt;</c>
///                 header (the trailing <c>top</c> is optional; when absent every clause is
///                 soft), then one <c>&lt;weight&gt; &lt;lit...&gt; 0</c> clause per record
///                 (records may span lines). A clause whose weight equals <c>top</c> is hard.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>New-style</b> (MaxSAT Evaluation 2022+) — no <c>p</c> line; one clause
///                 per line, a leading <c>h</c> marking a hard clause and a leading positive
///                 integer marking a soft clause of that weight. A trailing <c>0</c> is
///                 tolerated but not required. <c>top</c> is synthesized as (Σ soft weights)+1
///                 so the writer can round-trip through the classic form.
///             </description>
///         </item>
///     </list>
///     <c>c</c> comment lines are ignored. Reads line by line and never materializes the whole
///     input; budget/variable overruns raise <see cref="ComputationBudgetExceededException" />
///     and other malformation raises <see cref="FormatParseException" />.
/// </summary>
public static class WcnfParser
{
    public static WeightedCnfProblem Parse(TextReader reader, ResourceBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        budget ??= ResourceBudget.Default;

        using var tokens = FormatScanner.Scan(reader, 'c', budget, cancellationToken).GetEnumerator();
        if (!tokens.MoveNext())
            return new WeightedCnfProblem(0, 1, Array.Empty<int[]>(),
                Array.Empty<(long, int[])>());

        var first = tokens.Current;
        return first.Text == "p" ? ParseClassic(tokens, first) : ParseNewStyle(tokens, first);
    }

    private static WeightedCnfProblem ParseClassic(IEnumerator<Token> tokens, Token pToken)
    {
        // Collect the whole header line so an optional trailing "top" is detected reliably.
        var header = new List<Token> { pToken };
        Token? pending = null;
        while (tokens.MoveNext())
        {
            if (tokens.Current.Line == pToken.Line) header.Add(tokens.Current);
            else
            {
                pending = tokens.Current;
                break;
            }
        }

        if (header.Count < 4 || !header[1].Text.Equals("wcnf", StringComparison.OrdinalIgnoreCase))
            throw new FormatParseException("Expected a 'p wcnf <nvars> <nclauses> [top]' header",
                pToken.Line, pToken.Column);

        var declaredVars = FormatScanner.ParseInteger(header[2], "the variable count");
        if (declaredVars < 0)
            throw new FormatParseException($"Variable count cannot be negative ({declaredVars})",
                header[2].Line, header[2].Column);
        if (declaredVars > FormatScanner.MaxVariableIndex)
            throw new ComputationBudgetExceededException(
                $"Declared variable count {declaredVars} exceeds the supported maximum of " +
                $"{FormatScanner.MaxVariableIndex:N0}");
        FormatScanner.ParseInteger(header[3], "the clause count"); // advisory

        var top = header.Count >= 5
            ? FormatScanner.ParseInteger(header[4], "the top (hard-clause) weight")
            : long.MaxValue;

        var hard = new List<int[]>();
        var soft = new List<(long, int[])>();
        var maxVar = 0;
        long? weight = null;
        var lits = new List<int>();

        void Finalize()
        {
            if (weight!.Value == top) hard.Add(lits.ToArray());
            else soft.Add((RequireSoftWeight(weight.Value), lits.ToArray()));
            weight = null;
            lits.Clear();
        }

        void Consume(Token token)
        {
            if (weight is null)
            {
                var w = FormatScanner.ParseInteger(token, "a clause weight");
                if (w < 1)
                    throw new FormatParseException("Clause weight must be positive", token.Line, token.Column);
                weight = w;
            }
            else
            {
                var value = FormatScanner.ParseInteger(token, "a clause literal or 0");
                if (value == 0)
                {
                    Finalize();
                }
                else
                {
                    var literal = FormatScanner.RequireLiteral(value, token);
                    lits.Add(literal);
                    if (Math.Abs(literal) > maxVar) maxVar = Math.Abs(literal);
                }
            }
        }

        if (pending.HasValue) Consume(pending.Value);
        while (tokens.MoveNext()) Consume(tokens.Current);
        if (weight is not null && lits.Count > 0) Finalize(); // tolerate a missing final 0

        var variableCount = Math.Max((int)declaredVars, maxVar);
        return new WeightedCnfProblem(variableCount, top, hard.ToArray(), soft.ToArray());
    }

    private static WeightedCnfProblem ParseNewStyle(IEnumerator<Token> tokens, Token first)
    {
        var hard = new List<int[]>();
        var soft = new List<(long, int[])>();
        var maxVar = 0;
        long softWeightSum = 0;

        var lineTokens = new List<Token>();
        var current = first;
        while (true)
        {
            lineTokens.Clear();
            lineTokens.Add(current);
            var line = current.Line;
            bool advanced;
            while ((advanced = tokens.MoveNext()) && tokens.Current.Line == line)
                lineTokens.Add(tokens.Current);

            // Drop a tolerated trailing 0 terminator.
            if (lineTokens.Count > 1 && lineTokens[^1].Text == "0") lineTokens.RemoveAt(lineTokens.Count - 1);

            var head = lineTokens[0];
            var isHard = head.Text.Equals("h", StringComparison.OrdinalIgnoreCase);
            long weight = 0;
            if (!isHard)
            {
                weight = FormatScanner.ParseInteger(head, "a clause weight or 'h'");
                if (weight < 1)
                    throw new FormatParseException("Clause weight must be positive", head.Line, head.Column);
                weight = RequireSoftWeight(weight);
            }

            var lits = new int[lineTokens.Count - 1];
            for (var i = 1; i < lineTokens.Count; i++)
            {
                var token = lineTokens[i];
                var value = FormatScanner.ParseInteger(token, "a clause literal");
                var literal = FormatScanner.RequireLiteral(value, token);
                lits[i - 1] = literal;
                if (Math.Abs(literal) > maxVar) maxVar = Math.Abs(literal);
            }

            if (isHard)
            {
                hard.Add(lits);
            }
            else
            {
                soft.Add((weight, lits));
                softWeightSum += weight;
            }

            if (!advanced) break;
            current = tokens.Current;
        }

        // Synthesize a classic 'top' strictly greater than any achievable soft cost.
        var top = softWeightSum + 1;
        return new WeightedCnfProblem(maxVar, top, hard.ToArray(), soft.ToArray());
    }

    private static long RequireSoftWeight(long weight)
    {
        if (weight > int.MaxValue)
            throw new ComputationBudgetExceededException(
                $"Soft-clause weight {weight} exceeds the supported maximum of {int.MaxValue}");
        return weight;
    }
}
