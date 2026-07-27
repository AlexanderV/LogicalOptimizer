using System.Globalization;
using System.Text;

namespace LogicalOptimizer.Tests;

/// <summary>
///     A parsed DIMACS CNF instance: variable count, clauses (1-based DIMACS literals),
///     and the free-form <c>c key value</c> header comments (seed, expected verdict, kind).
/// </summary>
internal sealed record DimacsInstance(
    int VariableCount,
    IReadOnlyList<int[]> Clauses,
    IReadOnlyDictionary<string, string> Comments)
{
    /// <summary>Expected verdict recorded in the <c>c expected SAT|UNSAT</c> header.</summary>
    public bool ExpectedSatisfiable =>
        Comments.TryGetValue("expected", out var v)
            ? v.Equals("SAT", StringComparison.OrdinalIgnoreCase)
            : throw new InvalidOperationException("Instance has no 'c expected' header");
}

/// <summary>
///     Minimal, dependency-free DIMACS CNF reader/writer used by the SAT corpus
///     generator and the corpus regression tests. Comment lines of the shape
///     <c>c key value</c> are captured as key/value metadata (seed, expected verdict, etc.).
/// </summary>
internal static class DimacsCnf
{
    public static DimacsInstance Parse(string text)
    {
        var comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clauses = new List<int[]>();
        var current = new List<int>();
        var variableCount = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line[0] == 'c')
            {
                var body = line.Length > 1 ? line[1..].Trim() : "";
                var space = body.IndexOf(' ');
                if (space > 0)
                    comments[body[..space]] = body[(space + 1)..].Trim();
                continue;
            }

            if (line[0] == 'p')
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                // p cnf <vars> <clauses>
                if (parts.Length >= 3) variableCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
                continue;
            }

            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var literal = int.Parse(token, CultureInfo.InvariantCulture);
                if (literal == 0)
                {
                    clauses.Add(current.ToArray());
                    current = new List<int>();
                }
                else
                {
                    current.Add(literal);
                }
            }
        }

        if (current.Count > 0) clauses.Add(current.ToArray());
        return new DimacsInstance(variableCount, clauses, comments);
    }

    public static DimacsInstance Read(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    ///     Serialize to DIMACS text. Ordered comment pairs become <c>c key value</c>
    ///     header lines; output uses <c>\n</c> newlines for byte-stable diffs across OSes.
    /// </summary>
    public static string Write(int variableCount, IReadOnlyList<int[]> clauses,
        IEnumerable<KeyValuePair<string, string>> comments)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in comments)
            builder.Append("c ").Append(key).Append(' ').Append(value).Append('\n');
        builder.Append("p cnf ").Append(variableCount).Append(' ').Append(clauses.Count).Append('\n');
        foreach (var clause in clauses)
        {
            foreach (var literal in clause) builder.Append(literal).Append(' ');
            builder.Append("0\n");
        }

        return builder.ToString();
    }
}
