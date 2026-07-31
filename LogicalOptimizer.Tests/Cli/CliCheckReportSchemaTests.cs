using System.IO;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Makes the <c>check</c> verb's <c>--format=json</c> report an enforced contract, the same
///     way <see cref="CliReportSchemaTests" /> does for the optimize report: every golden example
///     under <c>schema/examples/</c> validates against the published schema
///     (<c>schema/cli-check-report-v1.schema.json</c>), the report the writer produces TODAY still
///     byte-equals its committed example, and the schema stays closed so a new field cannot ship
///     without a reviewed schema diff. To regenerate the examples after an intended change, run
///     with <c>LOGICALOPTIMIZER_REGENERATE_CLIREPORTS=1</c> and commit the diff.
/// </summary>
public class CliCheckReportSchemaTests
{
    private const string RegenerateVariable = "LOGICALOPTIMIZER_REGENERATE_CLIREPORTS";

    /// <summary>The documented regression pair; the truth-table path makes the witness deterministic.</summary>
    private const string OldRule = "admin | (owner & businessHours)";

    private const string BuggyRewrite = "admin | owner";

    public static readonly TheoryData<string> ExampleNames = new(
        "check-equivalent",
        "check-not-equivalent",
        "check-parse-error");

    private static string Render(string name) => name switch
    {
        "check-equivalent" => RenderVerdict("a & b | a & c", "a & (b | c)"),
        "check-not-equivalent" => RenderVerdict(OldRule, BuggyRewrite),
        "check-parse-error" => RenderParseError("a & & b", "a"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown example")
    };

    private static string RenderVerdict(string left, string right)
    {
        var result = EquivalenceChecker.Check(left, right);
        var writer = new StringWriter();
        CheckReportWriter.Write(writer, left, right, result);
        return Normalize(writer.ToString());
    }

    /// <summary>The error path the CLI walks: the left side fails to parse, the diagnostic names it.</summary>
    private static string RenderParseError(string left, string right)
    {
        var writer = new StringWriter();
        try
        {
            // Same parse entry point as CheckCommand, so the diagnostic (and its snippet)
            // matches what the CLI actually reports.
            new FormulaFactory().Parse(left);
            throw new InvalidOperationException($"'{left}' was expected to fail to parse");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var diagnostic =
                (ex as FormulaParseException ?? ex.InnerException as FormulaParseException)?.Diagnostic;
            Assert.NotNull(diagnostic);
            CheckReportWriter.WriteError(writer, left, right, ex.Message, diagnostic, "left");
        }

        return Normalize(writer.ToString());
    }

    private static string Normalize(string json) => json.ReplaceLineEndings("\n").TrimEnd() + "\n";

    private static string ExamplePath(string name) =>
        Path.Combine(PublishedCliSchema.Directory, "examples", name + ".json");

    private static JsonSchema Schema => PublishedCliSchema.CheckSchema;

    private static JsonNode SchemaNode => PublishedCliSchema.CheckNode;

    private static EvaluationOptions StrictEvaluation => PublishedCliSchema.StrictEvaluation;

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void CommittedExample_ValidatesAgainstThePublishedSchema(string name)
    {
        var path = ExamplePath(name);
        Assert.True(File.Exists(path),
            $"Example missing at {path}. Run once with {RegenerateVariable}=1.");
        PublishedCliSchema.AssertValidCheck(File.ReadAllText(path), $"schema/examples/{name}.json");
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void FreshReport_ValidatesAgainstThePublishedSchema(string name)
    {
        // Guards the direction the committed examples cannot: today's writer output must satisfy
        // today's schema, even if nobody remembered to regenerate the examples.
        PublishedCliSchema.AssertValidCheck(Render(name), $"a freshly rendered '{name}' report");
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void FreshReport_StillEqualsTheCommittedExample(string name)
    {
        var actual = Render(name);

        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            // Write into the SOURCE tree so the diff lands in review.
            var sourcePath = Path.Combine(PublishedCliSchema.RepositoryRoot(),
                "schema", "examples", name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual);
            return;
        }

        var path = ExamplePath(name);
        Assert.True(File.Exists(path),
            $"Example missing at {path}. Run once with {RegenerateVariable}=1.");

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd() + "\n";
        if (expected == actual) return;

        Assert.Fail($"The '{name}' check report drifted from schema/examples/{name}.json. " +
                    $"If the change is intended, regenerate with {RegenerateVariable}=1, review the " +
                    "diff, and update schema/README.md (additive vs breaking).\n\nActual:\n" + actual);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        // Proves the schema is actually closed: were it not, adding a field to CheckReportWriter
        // would pass CI silently and the "additive within a version" promise would be unverified.
        var withExtra = JsonNode.Parse(Render("check-equivalent"))!.AsObject();
        withExtra["somethingNew"] = "value";
        Assert.False(Schema.Evaluate(withExtra, StrictEvaluation).IsValid,
            "The schema must reject an undeclared field, otherwise it cannot gate schema changes.");
    }

    [Fact]
    public void VerdictAndErrorTogether_IsRejected()
    {
        var both = JsonNode.Parse(Render("check-equivalent"))!.AsObject();
        both["error"] = JsonNode.Parse("""{ "code": "processing_error", "message": "contradiction" }""");
        Assert.False(Schema.Evaluate(both, StrictEvaluation).IsValid,
            "A report carrying both `verdict` and `error` must not validate.");
    }

    [Fact]
    public void NotEquivalentWithoutACounterexample_IsRejected()
    {
        // The counterexample IS the disproof; a `not_equivalent` verdict without its witness is
        // an unsupported claim and must not validate.
        var report = JsonNode.Parse(Render("check-not-equivalent"))!.AsObject();
        report.Remove("counterexample");
        Assert.False(Schema.Evaluate(report, StrictEvaluation).IsValid);
    }

    [Fact]
    public void VerdictContradictingTheEquivalentFlag_IsRejected()
    {
        // The redundant boolean exists for convenience; the schema keeps it from ever
        // disagreeing with the verdict a consumer might branch on instead.
        var report = JsonNode.Parse(Render("check-equivalent"))!.AsObject();
        report["equivalent"] = false;
        Assert.False(Schema.Evaluate(report, StrictEvaluation).IsValid);
    }

    [Fact]
    public void UnknownVerdictCarryingAnEquivalentFlag_IsRejected()
    {
        // `unknown` is a non-answer: letting it carry `equivalent` would let a timeout be read
        // as a verdict.
        var report = JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "left": "a",
              "right": "b",
              "verdict": "unknown",
              "equivalent": true
            }
            """);
        Assert.False(Schema.Evaluate(report, StrictEvaluation).IsValid);

        var bare = JsonNode.Parse("""
            { "schemaVersion": 1, "left": "a", "right": "b", "verdict": "unknown" }
            """);
        Assert.True(Schema.Evaluate(bare, StrictEvaluation).IsValid,
            "A bare `unknown` verdict is the legitimate budget-exhausted report and must validate.");
    }

    [Fact]
    public void ErrorCodeEnum_ListsEveryParseErrorCodePlusTheCatchAll()
    {
        // Same guard as the optimize report: a new code added in C# but not in the schema would
        // make a legitimate report invalid.
        var expected = Enum.GetNames<ParseErrorCode>().Append("processing_error");
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal),
            EnumAt("properties/error/properties/code").OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void SchemaVersion_MatchesWhatTheWriterEmits()
    {
        var emitted = JsonNode.Parse(Render("check-equivalent"))!["schemaVersion"]!.GetValue<int>();
        var declared = SchemaNode["properties"]!["schemaVersion"]!["const"]!.GetValue<int>();
        Assert.Equal(emitted, declared);

        // The file name encodes the version too; a v2 belongs in a NEW file (see schema/README.md).
        Assert.True(File.Exists(Path.Combine(PublishedCliSchema.Directory,
            $"cli-check-report-v{declared}.schema.json")));
    }

    private static IEnumerable<string> EnumAt(string pointer)
    {
        var node = SchemaNode;
        foreach (var segment in pointer.Split('/'))
            node = node[segment] ?? throw new InvalidOperationException($"No '{segment}' at {pointer}");
        return node["enum"]!.AsArray().Select(v => v!.GetValue<string>());
    }
}
