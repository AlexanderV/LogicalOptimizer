using System.IO;
using System.Text.Json;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Drives the real entry point (<c>Program.Main</c>) and checks the <c>--format=json</c>
///     contract for every way an input can reach it.
///     <para>
///         <see cref="CliReportSchemaTests" /> validates the writer against the schema, but it
///         starts from an <c>OptimizationResult</c> that already exists — which skips CSV
///         preprocessing entirely. That blind spot is exactly where the report stopped naming its
///         own input: the CLI replaced the received argument with the sum-of-products derived from
///         the CSV, and the schema's promise that <c>input</c> is "the argument exactly as
///         received" quietly stopped holding. These tests exercise the seam the two features share.
///     </para>
///     <para>
///         They also pin the properties a machine consumer depends on but a writer-level test
///         cannot see: exactly one JSON document on stdout, human-readable progress kept on stderr,
///         and the documented exit codes.
///     </para>
/// </summary>
[Collection(ConsoleCollection.Name)]
public class CliJsonInputContractTests
{
    /// <summary>A complete truth table for <c>a | b</c>, in the `\n`-escaped form the docs use.</summary>
    private const string CsvSource = "a,b,Result\\n0,0,0\\n0,1,1\\n1,0,1\\n1,1,1";

    private const string Expected = "a | b";

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

    /// <summary>
    ///     Parses stdout as ONE document and validates it. <see cref="JsonDocument.Parse(string,
    ///     JsonDocumentOptions)" /> rejects trailing content, so a second document or any stray
    ///     print into stdout fails here rather than silently corrupting a consumer's parse.
    /// </summary>
    private static JsonElement SoleReport(CliRun run)
    {
        Assert.False(string.IsNullOrWhiteSpace(run.StdOut),
            $"The CLI wrote nothing to stdout. stderr was:\n{run.StdErr}");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(run.StdOut);
        }
        catch (JsonException ex)
        {
            Assert.Fail($"stdout must be exactly one JSON document, but it did not parse: {ex.Message}\n\n" +
                        $"stdout was:\n{run.StdOut}");
            throw;
        }

        PublishedCliSchema.AssertValid(run.StdOut, "the report Program.Main wrote to stdout");
        return document.RootElement.Clone();
    }

    private static string? Optional(JsonElement report, string name) =>
        report.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>Writes <paramref name="content" /> to a fresh <c>*.csv</c> and deletes it after use.</summary>
    private static string WithCsvFile(string content, Func<string, string> use)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logicaloptimizer-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content.Replace("\\n", "\n"));
        try
        {
            return use(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InlineCsv_ReportsTheCsvAsInputAndTheDerivedExpressionSeparately(bool explicitFlag)
    {
        // The flag and the auto-detection (CommandLineProcessor.DetectCsvInput) reach the same
        // code path, so a fix keyed on the flag alone would still misreport the common invocation.
        var run = explicitFlag
            ? Run("--format=json", "--csv", CsvSource)
            : Run("--format=json", CsvSource);

        Assert.Equal(0, run.ExitCode);
        var report = SoleReport(run);

        Assert.Equal(CsvSource, report.GetProperty("input").GetString());
        Assert.Equal("csv", report.GetProperty("sourceFormat").GetString());
        Assert.Equal(Expected, report.GetProperty("optimized").GetString());

        // The derived expression is still reported — it is what every verdict below is about —
        // but as its own field rather than in place of the input.
        var analyzed = report.GetProperty("analyzedExpression").GetString();
        Assert.NotNull(analyzed);
        Assert.NotEqual(CsvSource, analyzed);
        Assert.DoesNotContain(",", analyzed);
    }

    [Fact]
    public void CsvFile_ReportsThePathAsReceived()
    {
        // The file case is the one an audit trail cares about most: without it the report cannot
        // say which of several truth tables in a CI matrix it came from.
        var path = WithCsvFile(CsvSource, csvPath =>
        {
            var run = Run("--format=json", csvPath);
            Assert.Equal(0, run.ExitCode);

            var report = SoleReport(run);
            Assert.Equal(csvPath, report.GetProperty("input").GetString());
            Assert.Equal("csv", report.GetProperty("sourceFormat").GetString());
            Assert.Equal(Expected, report.GetProperty("optimized").GetString());
            Assert.NotNull(Optional(report, "analyzedExpression"));
            return csvPath;
        });

        Assert.False(File.Exists(path), "The temporary CSV should have been cleaned up.");
    }

    [Fact]
    public void PlainExpression_IsUnchangedByTheCsvHandling()
    {
        // Regression guard: the overwhelmingly common invocation must not grow a redundant
        // `analyzedExpression` echoing `input`.
        var run = Run("--format=json", "a & b | a & c");

        Assert.Equal(0, run.ExitCode);
        var report = SoleReport(run);

        Assert.Equal("a & b | a & c", report.GetProperty("input").GetString());
        Assert.Equal("expression", report.GetProperty("sourceFormat").GetString());
        Assert.Null(Optional(report, "analyzedExpression"));
    }

    [Fact]
    public void MalformedCsv_ProducesAnErrorReportNamingTheSameInputAsSuccessWould()
    {
        // The asymmetry this pins down: the error path always wrote the received argument while
        // the success path wrote the derived expression, so a consumer could not compare the two.
        const string malformed = "not a truth table at all";
        var run = Run("--format=json", "--csv", malformed);

        Assert.Equal(2, run.ExitCode);
        var report = SoleReport(run);

        Assert.Equal(malformed, report.GetProperty("input").GetString());
        Assert.Equal("csv", report.GetProperty("sourceFormat").GetString());
        Assert.False(report.TryGetProperty("optimized", out _));

        // No expression was ever derived, so there is nothing to report as analyzed.
        Assert.Null(Optional(report, "analyzedExpression"));

        // The code is what a machine consumer branches on, so pin it. A CSV that is not a truth
        // table carries no per-character parse diagnostic, so the documented catch-all is correct
        // here — but it has to be asserted, not accepted as "any non-empty string".
        Assert.Equal("processing_error", report.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ParseErrorOnAnExpression_StillReportsTheExpressionAsInput()
    {
        var run = Run("--format=json", "a & & b");

        Assert.Equal(2, run.ExitCode);
        var report = SoleReport(run);

        Assert.Equal("a & & b", report.GetProperty("input").GetString());
        Assert.Equal("expression", report.GetProperty("sourceFormat").GetString());
        Assert.Null(Optional(report, "analyzedExpression"));
    }

    [Fact]
    public void CsvProgressGoesToStderr_LeavingStdoutParseable()
    {
        // CsvProcessor narrates what it detected. That narration is useful, and it must not end up
        // in the document a consumer pipes into a parser.
        var run = Run("--format=json", "--csv", CsvSource);

        SoleReport(run);
        Assert.Contains("Detected CSV truth table input", run.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("Detected CSV truth table input", run.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Generated expression from CSV", run.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonWithMultiOutputCsv_IsStillRejectedAsAUsageError()
    {
        // The one CSV/JSON combination that is genuinely unsupported: several expressions cannot
        // fit a single-expression report. Rejecting it at argument validation (exit 1, no document)
        // is what keeps single-output CSV a supported path rather than an accident.
        var run = Run("--format=json", "--outputs=Result", CsvSource);

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("schemaVersion", run.StdOut, StringComparison.Ordinal);
        Assert.Contains("--outputs", run.StdErr, StringComparison.Ordinal);
    }
}
