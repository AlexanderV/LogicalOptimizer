using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Makes the <c>--format=json</c> CLI report an enforced contract rather than a convention.
///     Three things are checked mechanically:
///     <list type="number">
///         <item>
///             every golden example under <c>schema/examples/</c> validates against the published
///             Draft 2020-12 schema (<c>schema/cli-report-v1.schema.json</c>);
///         </item>
///         <item>
///             the report the writer produces TODAY for each scenario still equals the committed
///             example — so a field added, renamed or dropped shows up as a reviewed diff;
///         </item>
///         <item>
///             the schema's enums and version still match the CLR enums and the writer, so the
///             schema cannot silently fall behind the code.
///         </item>
///     </list>
///     The schema closes every object (<c>unevaluatedProperties: false</c>), so emitting a NEW
///     field fails validation until the schema is updated deliberately — which is exactly the
///     review gate an additive-only contract needs.
///     To regenerate the examples after an intended change, run with
///     <c>LOGICALOPTIMIZER_REGENERATE_CLIREPORTS=1</c> and commit the diff.
/// </summary>
public class CliReportSchemaTests
{
    private const string RegenerateVariable = "LOGICALOPTIMIZER_REGENERATE_CLIREPORTS";

    /// <summary>
    ///     11 variables: inside the exact-minimization range but past its work budget, so the
    ///     result is sound with minimality reported as BudgetExceeded rather than proven.
    /// </summary>
    private const string BudgetExceededExpression =
        "a01 & b01 | !a01 & c01 | b01 & c01 | a02 & b02 | !a02 & c02 | b02 & c02 | " +
        "a03 & b03 | !a03 & c03 | b03 & c03 | d01 & d02";

    /// <summary>42 variables: past the CNF size budget, so the CNF form comes back TooLarge.</summary>
    private const string TooLargeExpression =
        "a01 & b01 & c01 | a02 & b02 & c02 | a03 & b03 & c03 | a04 & b04 & c04 | " +
        "a05 & b05 & c05 | a06 & b06 & c06 | a07 & b07 & c07 | a08 & b08 & c08 | " +
        "a09 & b09 & c09 | a10 & b10 & c10 | a11 & b11 & c11 | a12 & b12 & c12 | " +
        "a13 & b13 & c13 | a14 & b14 & c14";

    /// <summary>
    ///     One file per documented outcome a consumer has to handle. Each renders exactly what the
    ///     CLI writes to stdout for that case.
    /// </summary>
    public static readonly TheoryData<string> ExampleNames = new(
        "success-minimal-proven",
        "budget-exceeded",
        "form-too-large",
        "advanced-xor",
        "trace",
        "parse-error",
        "processing-error");

    private static string Render(string name) => name switch
    {
        "success-minimal-proven" => RenderResult("a & b | a & c"),
        "budget-exceeded" => RenderResult(BudgetExceededExpression),
        "form-too-large" => RenderResult(TooLargeExpression),
        "advanced-xor" => RenderResult("a & !b | !a & b"),
        "trace" => RenderResult("a & b | a & c", new OptimizationOptions { IncludeTrace = true }),
        // Both error shapes go through the CLI's own error path (Program.ProcessExpression), so
        // the examples show a real diagnostic rather than a hand-written one.
        "parse-error" => RenderError("a & & b"),
        "processing-error" => RenderProcessingError("a & b", "Simulated processing failure"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown example")
    };

    private static string RenderResult(string expression, OptimizationOptions? options = null)
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            options ?? new OptimizationOptions());
        var writer = new StringWriter();
        JsonReportWriter.Write(writer, result);
        return Normalize(writer.ToString());
    }

    private static string RenderError(string expression)
    {
        var writer = new StringWriter();
        try
        {
            new BooleanExpressionOptimizer().OptimizeExpression(expression);
            throw new InvalidOperationException($"'{expression}' was expected to fail to parse");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Same unwrapping as the CLI: the facade wraps the parse failure.
            var diagnostic =
                (ex as FormulaParseException ?? ex.InnerException as FormulaParseException)?.Diagnostic;
            Assert.NotNull(diagnostic);
            JsonReportWriter.WriteError(writer, expression, ex.Message, diagnostic);
        }

        return Normalize(writer.ToString());
    }

    private static string RenderProcessingError(string expression, string message)
    {
        var writer = new StringWriter();
        JsonReportWriter.WriteError(writer, expression, message);
        return Normalize(writer.ToString());
    }

    private static string Normalize(string json) => json.ReplaceLineEndings("\n").TrimEnd() + "\n";

    private static string SchemaDirectory => Path.Combine(AppContext.BaseDirectory, "Schema");

    private static string ExamplePath(string name) =>
        Path.Combine(SchemaDirectory, "examples", name + ".json");

    private static JsonSchema Schema
    {
        get
        {
            var path = Path.Combine(SchemaDirectory, "cli-report-v1.schema.json");
            Assert.True(File.Exists(path), $"Published CLI schema missing at {path}");
            return JsonSchema.FromText(File.ReadAllText(path));
        }
    }

    private static JsonNode SchemaNode
    {
        get
        {
            var path = Path.Combine(SchemaDirectory, "cli-report-v1.schema.json");
            return JsonNode.Parse(File.ReadAllText(path))!;
        }
    }

    private static readonly EvaluationOptions StrictEvaluation = new()
    {
        OutputFormat = OutputFormat.List,
        // Without this, `unevaluatedProperties: false` is not reported per-instance-location and an
        // unexpected extra field could slip through as a passing evaluation.
        RequireFormatValidation = true
    };

    private static void AssertValidatesAgainstSchema(string json, string what)
    {
        var instance = JsonNode.Parse(json);
        var evaluation = Schema.Evaluate(instance, StrictEvaluation);
        if (evaluation.IsValid) return;

        var errors = evaluation.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Key} -> {e.Value}"))
            .ToArray();
        Assert.Fail($"{what} does not satisfy schema/cli-report-v1.schema.json:\n" +
                    string.Join("\n", errors) + "\n\nDocument was:\n" + json);
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void CommittedExample_ValidatesAgainstThePublishedSchema(string name)
    {
        var path = ExamplePath(name);
        Assert.True(File.Exists(path),
            $"Example missing at {path}. Run once with {RegenerateVariable}=1.");
        AssertValidatesAgainstSchema(File.ReadAllText(path), $"schema/examples/{name}.json");
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void FreshReport_ValidatesAgainstThePublishedSchema(string name)
    {
        // Guards the direction the committed examples cannot: today's writer output must satisfy
        // today's schema, even if nobody remembered to regenerate the examples.
        AssertValidatesAgainstSchema(Render(name), $"a freshly rendered '{name}' report");
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void FreshReport_StillEqualsTheCommittedExample(string name)
    {
        var actual = Render(name);

        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            // Write into the SOURCE tree so the diff lands in review.
            var sourcePath = Path.Combine(RepositoryRoot(), "schema", "examples", name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual);
            return;
        }

        var path = ExamplePath(name);
        Assert.True(File.Exists(path),
            $"Example missing at {path}. Run once with {RegenerateVariable}=1.");

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd() + "\n";
        if (expected == actual) return;

        Assert.Fail($"The '{name}' CLI report drifted from schema/examples/{name}.json. " +
                    $"If the change is intended, regenerate with {RegenerateVariable}=1, review the " +
                    "diff, and update schema/README.md (additive vs breaking).\n\nActual:\n" + actual);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        // Proves the schema is actually closed: were it not, adding a field to JsonReportWriter
        // would pass CI silently and the "additive within a version" promise would be unverified.
        var withExtra = JsonNode.Parse(Render("success-minimal-proven"))!.AsObject();
        withExtra["somethingNew"] = "value";
        Assert.False(Schema.Evaluate(withExtra, StrictEvaluation).IsValid,
            "The schema must reject an undeclared field, otherwise it cannot gate schema changes.");
    }

    [Fact]
    public void MissingResultAndError_IsRejected()
    {
        // Every report is either a success (has `optimized`) or a failure (has `error`).
        var neither = JsonNode.Parse("""{"schemaVersion": 1, "input": "a & b"}""");
        Assert.False(Schema.Evaluate(neither, StrictEvaluation).IsValid);
    }

    [Fact]
    public void SchemaVersion_MatchesWhatTheWriterEmits()
    {
        var emitted = JsonDocument.Parse(Render("success-minimal-proven"))
            .RootElement.GetProperty("schemaVersion").GetInt32();
        var declared = SchemaNode["properties"]!["schemaVersion"]!["const"]!.GetValue<int>();
        Assert.Equal(emitted, declared);

        // The file name encodes the version too; a v2 belongs in a NEW file (see schema/README.md).
        Assert.True(File.Exists(Path.Combine(SchemaDirectory, $"cli-report-v{declared}.schema.json")));
    }

    [Theory]
    [InlineData(typeof(MinimizationStatus), "properties/minimality")]
    [InlineData(typeof(ComputationStatus), "$defs/form/properties/status")]
    [InlineData(typeof(MinimizationStatus), "$defs/form/properties/minimality")]
    [InlineData(typeof(OptimizationTraceCategory), "properties/trace/items/properties/category")]
    public void SchemaEnum_ListsExactlyTheClrEnumNames(Type clrEnum, string pointer)
    {
        // A new status added in C# but not in the schema would make a legitimate report invalid;
        // a stale name left in the schema would document an outcome that can no longer occur.
        Assert.Equal(Enum.GetNames(clrEnum).OrderBy(n => n, StringComparer.Ordinal),
            EnumAt(pointer).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ErrorCodeEnum_ListsEveryParseErrorCodePlusTheCatchAll()
    {
        var expected = Enum.GetNames<ParseErrorCode>().Append("processing_error");
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal),
            EnumAt("properties/error/properties/code").OrderBy(n => n, StringComparer.Ordinal));
    }

    private static IEnumerable<string> EnumAt(string pointer)
    {
        var node = SchemaNode;
        foreach (var segment in pointer.Split('/'))
            node = node[segment] ?? throw new InvalidOperationException($"No '{segment}' at {pointer}");
        return node["enum"]!.AsArray().Select(v => v!.GetValue<string>());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }
}
