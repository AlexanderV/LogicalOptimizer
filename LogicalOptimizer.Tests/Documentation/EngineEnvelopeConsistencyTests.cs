using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Keeps the "Engine operating envelope" table in
///     <c>docs-site/articles/budgets-and-zones.md</c> consistent with the code, mechanically:
///     every hard limit the table cites as enforced (a `constant` = value pair) must equal the
///     constant, budget default, or default parameter value that actually enforces it. Soft
///     thresholds are engineering guidance and deliberately not checked here — the table marks
///     them as such. When a constant changes, this test fails until the envelope row is updated
///     (and vice versa), mirroring how <c>DocExamplesTests</c> pins documented outputs.
/// </summary>
public class EngineEnvelopeConsistencyTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }

    /// <summary>The envelope section: from its heading to the next H2 heading or end of file.</summary>
    private static string EnvelopeSection()
    {
        var path = Path.Combine(RepositoryRoot(), "docs-site", "articles", "budgets-and-zones.md");
        var text = File.ReadAllText(path);

        const string heading = "## Engine operating envelope";
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"budgets-and-zones.md no longer contains the '{heading}' section");

        var next = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next >= 0 ? text[start..next] : text[start..];
    }

    /// <summary>Numbers in the article use spaces as thousands separators (e.g. "5 000 000").</summary>
    private static string DocFormat(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");
    }

    /// <summary>Default value of an int parameter, read from the method that enforces it.</summary>
    private static long DefaultParameter(Type type, string method, Type[] signature, int parameterIndex)
    {
        var info = type.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static,
            binder: null, signature, modifiers: null);
        Assert.True(info != null, $"{type.Name}.{method} with the expected signature no longer exists");
        var parameter = info!.GetParameters()[parameterIndex];
        Assert.True(parameter.HasDefaultValue, $"{type.Name}.{method} parameter '{parameter.Name}' lost its default");
        return Convert.ToInt64(parameter.DefaultValue, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Every (documented token, enforcing code value) pair the envelope's hard-limit column
    ///     cites. The expected value comes FROM the code, never from this file, so nothing is
    ///     pinned twice: the doc must contain "`token` … = &lt;current code value&gt;".
    /// </summary>
    private static IEnumerable<(string Token, long Value)> HardLimits()
    {
        // Heuristic rewrite optimizer: fixed input limits and the whole-call deadline.
        yield return ("MAX_EXPRESSION_LENGTH", PerformanceValidator.MAX_EXPRESSION_LENGTH);
        yield return ("MAX_VARIABLES", PerformanceValidator.MAX_VARIABLES);
        yield return ("MAX_PARENTHESES_DEPTH", PerformanceValidator.MAX_PARENTHESES_DEPTH);
        yield return ("MAX_OPTIMIZATION_ITERATIONS", PerformanceValidator.MAX_OPTIMIZATION_ITERATIONS);
        yield return ("MAX_PROCESSING_TIME_SECONDS", PerformanceValidator.MAX_PROCESSING_TIME_SECONDS);

        // Exact Quine-McCluskey zone boundaries and budgets.
        yield return ("EXACT_GUARANTEE_VARIABLES", PerformanceValidator.EXACT_GUARANTEE_VARIABLES);
        yield return ("MAX_EXACT_MINIMIZATION_VARIABLES", PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES);
        yield return ("QmPairComparisonLimit", ResourceBudget.Default.QmPairComparisonLimit);
        yield return ("CoverStepLimit", ResourceBudget.Default.CoverStepLimit);

        // SAT two-level minimizer zone.
        yield return ("MAX_SAT_MINIMIZATION_VARIABLES", PerformanceValidator.MAX_SAT_MINIMIZATION_VARIABLES);
        yield return ("SAT_MINIMIZATION_CUBE_LIMIT", PerformanceValidator.SAT_MINIMIZATION_CUBE_LIMIT);
        yield return ("SAT_MINIMIZATION_QUERY_CONFLICTS", PerformanceValidator.SAT_MINIMIZATION_QUERY_CONFLICTS);
        yield return ("SatConflictLimit", ResourceBudget.Default.SatConflictLimit);

        // Embedded CDCL SAT.
        yield return ("EquivalenceChecker.DefaultMaxConflicts", EquivalenceChecker.DefaultMaxConflicts);
        yield return ("SatSolver.Solve", DefaultParameter(typeof(SatSolver), nameof(SatSolver.Solve),
            new[] { typeof(int), typeof(CancellationToken) }, 0));

        // MaxSAT / pseudo-Boolean.
        yield return ("MaxSatSolver.Solve", DefaultParameter(typeof(MaxSatSolver), nameof(MaxSatSolver.Solve),
            new[] { typeof(int), typeof(CancellationToken) }, 0));
        yield return ("PseudoBooleanProblem.Solve", DefaultParameter(typeof(PseudoBooleanProblem),
            nameof(PseudoBooleanProblem.Solve), new[] { typeof(int), typeof(CancellationToken) }, 0));

        // BDD, including reordering.
        yield return ("BddNodeLimit", ResourceBudget.Default.BddNodeLimit);
        yield return ("maxRebuilds", DefaultParameter(typeof(BinaryDecisionDiagram),
            nameof(BinaryDecisionDiagram.BuildWithSiftedOrder),
            new[] { typeof(AstNode), typeof(int), typeof(int), typeof(CancellationToken) }, 2));

        // d-DNNF compilation.
        yield return ("KnowledgeCompilation.DefaultNodeBudget", KnowledgeCompilation.DefaultNodeBudget);
    }

    [Fact]
    public void EnvelopeTable_HardLimits_MatchTheEnforcingCodeValues()
    {
        var section = EnvelopeSection();
        var mismatches = new List<string>();

        foreach (var (token, value) in HardLimits())
        {
            // The value must follow the backticked token inside the same table cell (no '|'
            // in between) as "= <value>", and the match must be the complete number — a doc
            // saying "= 100" cannot satisfy an expected "= 10".
            var pattern = $@"`{Regex.Escape(token)}`[^|]*?= {Regex.Escape(DocFormat(value))}(?!\d| \d)";
            if (!Regex.IsMatch(section, pattern))
                mismatches.Add($"`{token}` = {DocFormat(value)}");
        }

        Assert.True(mismatches.Count == 0,
            "The 'Engine operating envelope' table in docs-site/articles/budgets-and-zones.md no " +
            "longer states these enforced values (update the table or the constant, deliberately): " +
            string.Join("; ", mismatches));
    }

    [Fact]
    public void EnvelopeTable_MirroredDefaults_StillAgree()
    {
        // The table presents these pairs as the same number; if either side moves, the
        // wording ("mirrored by", the parenthesized constant) becomes a lie.
        Assert.Equal(EquivalenceChecker.DefaultMaxConflicts, ResourceBudget.Default.SatConflictLimit);
        Assert.Equal(BinaryDecisionDiagram.DefaultNodeBudget, ResourceBudget.Default.BddNodeLimit);
    }
}
