using System.IO;
using System.Text.Json;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Drives the real entry point (<c>Program.Main</c>) for the <c>check</c> verb and pins its
///     contract: the verdict wording, the counterexample, the <c>--format=json</c> document
///     (validated against <c>schema/cli-check-report-v1.schema.json</c>), and the exit codes —
///     <c>0</c> equivalent, <c>3</c> not equivalent, <c>1</c> usage error, <c>2</c> processing
///     error. The counterexample is not just asserted textually: it is substituted back into
///     both expressions to confirm they really evaluate differently on it.
/// </summary>
[Collection(ConsoleCollection.Name)]
public class CliCheckCommandTests
{
    /// <summary>The documented regression pair: the rewrite drops the business-hours guard.</summary>
    private const string OldRule = "admin | (owner & businessHours)";

    private const string BuggyRewrite = "admin | owner";

    private sealed record CliRun(int ExitCode, string StdOut, string StdErr);

    private static CliRun Run(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exitCode = global::LogicalOptimizer.Program.Main(args);
            return new CliRun(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>Parses stdout as exactly ONE document and validates it against the check schema.</summary>
    private static JsonElement SoleCheckReport(CliRun run)
    {
        Assert.False(string.IsNullOrWhiteSpace(run.StdOut),
            $"The CLI wrote nothing to stdout. stderr was:\n{run.StdErr}");

        var document = JsonDocument.Parse(run.StdOut);
        PublishedCliSchema.AssertValidCheck(run.StdOut, "the check report Program.Main wrote to stdout");
        return document.RootElement.Clone();
    }

    private static AstNode Parse(string expression)
    {
        return new FormulaFactory().Parse(expression);
    }

    /// <summary>The whole point of a counterexample: on it, the two sides must actually differ.</summary>
    private static void AssertDistinguishes(Dictionary<string, bool> assignment, string left, string right)
    {
        Assert.NotEqual(
            TruthTable.Evaluate(Parse(left), assignment),
            TruthTable.Evaluate(Parse(right), assignment));
    }

    [Fact]
    public void EquivalentPair_ReportsProvenAndExitsZero()
    {
        var run = Run("check", "a & b | a & c", "a & (b | c)");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Equivalent: proven", run.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Counterexample", run.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void InequivalentPair_ExitsThree_AndPrintsACounterexampleThatDistinguishesTheSides()
    {
        var run = Run("check", OldRule, BuggyRewrite);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("Equivalent: no", run.StdOut, StringComparison.Ordinal);

        // Read the printed assignment back and substitute it into both expressions: the line
        // must name an input where the two rules genuinely disagree, not just look like one.
        var line = run.StdOut.ReplaceLineEndings("\n").Split('\n')
            .Single(l => l.StartsWith("Counterexample: ", StringComparison.Ordinal));
        var assignment = line["Counterexample: ".Length..]
            .Split(", ")
            .Select(pair => pair.Split('='))
            .ToDictionary(parts => parts[0], parts => parts[1] == "1");

        AssertDistinguishes(assignment, OldRule, BuggyRewrite);
    }

    [Fact]
    public void EquivalentPair_JsonReportValidatesAndExitsZero()
    {
        var run = Run("check", "--format=json", "a & b | a & c", "a & (b | c)");

        Assert.Equal(0, run.ExitCode);
        var report = SoleCheckReport(run);

        Assert.Equal(1, report.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("a & b | a & c", report.GetProperty("left").GetString());
        Assert.Equal("a & (b | c)", report.GetProperty("right").GetString());
        Assert.Equal("equivalent", report.GetProperty("verdict").GetString());
        Assert.True(report.GetProperty("equivalent").GetBoolean());
        Assert.False(report.TryGetProperty("counterexample", out _));
    }

    [Fact]
    public void InequivalentPair_JsonCounterexampleDistinguishesTheSides()
    {
        var run = Run("check", "--format=json", OldRule, BuggyRewrite);

        Assert.Equal(3, run.ExitCode);
        var report = SoleCheckReport(run);

        Assert.Equal("not_equivalent", report.GetProperty("verdict").GetString());
        Assert.False(report.GetProperty("equivalent").GetBoolean());

        var assignment = report.GetProperty("counterexample").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetBoolean());
        Assert.NotEmpty(assignment);
        AssertDistinguishes(assignment, OldRule, BuggyRewrite);
    }

    [Fact]
    public void ConstantDifference_ReportsAnEmptyCounterexample()
    {
        // `1` vs `0` differ with no variables to assign: the report must still be well-formed,
        // with `counterexample` present (the witness slot) but empty.
        var run = Run("check", "--format=json", "1", "0");

        Assert.Equal(3, run.ExitCode);
        var report = SoleCheckReport(run);

        Assert.Equal("not_equivalent", report.GetProperty("verdict").GetString());
        Assert.Empty(report.GetProperty("counterexample").EnumerateObject());
    }

    [Fact]
    public void MalformedLeftExpression_JsonErrorReportNamesTheSide()
    {
        var run = Run("check", "--format=json", "a & & b", "a");

        Assert.Equal(2, run.ExitCode);
        var report = SoleCheckReport(run);

        Assert.Equal("a & & b", report.GetProperty("left").GetString());
        Assert.Equal("a", report.GetProperty("right").GetString());
        Assert.False(report.TryGetProperty("verdict", out _));

        var error = report.GetProperty("error");
        Assert.Equal("UnexpectedToken", error.GetProperty("code").GetString());
        Assert.Equal("left", error.GetProperty("side").GetString());
    }

    [Fact]
    public void MalformedRightExpression_TextModeReportsOnStderrAndExitsTwo()
    {
        var run = Run("check", "a", "a & & b");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("right expression", run.StdErr, StringComparison.Ordinal);
        // No verdict was reached, so nothing verdict-like may appear on stdout.
        Assert.DoesNotContain("Equivalent:", run.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSecondExpression_IsAUsageErrorWithNoReport()
    {
        var run = Run("check", "a & b");

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("schemaVersion", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("two expressions", run.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOption_IsAUsageError()
    {
        var run = Run("check", "--frobnicate", "a", "b");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--frobnicate", run.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void SpacedFormatForm_ProducesTheSameJsonReport()
    {
        // The optimize flow accepts `--format json` as well as `--format=json`; check must not
        // quietly diverge on the shared flag surface.
        var run = Run("check", "--format", "json", "a | b", "b | a");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("equivalent", SoleCheckReport(run).GetProperty("verdict").GetString());
    }
}
