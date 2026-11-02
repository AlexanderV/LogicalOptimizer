using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Shared access to the published CLI report contracts (<c>schema/cli-report-v1.schema.json</c>
///     for the optimize flow, <c>schema/cli-check-report-v1.schema.json</c> for the <c>check</c>
///     verb; both copied into the test output as <c>Schema/</c>). The writer-level contract suites
///     (<see cref="CliReportSchemaTests" />, <see cref="CliCheckReportSchemaTests" />) and the
///     end-to-end CLI suites (<see cref="CliJsonInputContractTests" />,
///     <see cref="CliCheckCommandTests" />) validate against the SAME files, so none can drift
///     onto a private copy of the rules.
/// </summary>
internal static class PublishedCliSchema
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Schema");

    // Built once per test run: JsonSchema.Net registers every built schema under its `$id` in the
    // process-wide SchemaRegistry and refuses to register the same `$id` twice.
    private static readonly Lazy<JsonSchema> ReportSchema = new(() => Load("cli-report-v1.schema.json"));

    private static readonly Lazy<JsonSchema> CheckReportSchema = new(() => Load("cli-check-report-v1.schema.json"));

    public static JsonSchema Schema => ReportSchema.Value;

    public static JsonSchema CheckSchema => CheckReportSchema.Value;

    private static JsonSchema Load(string fileName)
    {
        var path = Path.Combine(Directory, fileName);
        Assert.True(File.Exists(path), $"Published CLI schema missing at {path}");
        return JsonSchema.FromText(File.ReadAllText(path));
    }

    public static JsonNode Node =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "cli-report-v1.schema.json")))!;

    public static JsonNode CheckNode =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Directory, "cli-check-report-v1.schema.json")))!;

    public static readonly EvaluationOptions StrictEvaluation = new()
    {
        OutputFormat = OutputFormat.List,
        // Without this, `unevaluatedProperties: false` is not reported per-instance-location and an
        // unexpected extra field could slip through as a passing evaluation.
        RequireFormatValidation = true
    };

    public static void AssertValid(string json, string what) =>
        AssertValid(Schema, "cli-report-v1.schema.json", json, what);

    public static void AssertValidCheck(string json, string what) =>
        AssertValid(CheckSchema, "cli-check-report-v1.schema.json", json, what);

    private static void AssertValid(JsonSchema schema, string schemaName, string json, string what)
    {
        var instance = JsonNode.Parse(json);
        var evaluation = schema.Evaluate(instance, StrictEvaluation);
        if (evaluation.IsValid) return;

        var errors = (evaluation.Details ?? [evaluation])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Key} -> {e.Value}"))
            .ToArray();
        Assert.Fail($"{what} does not satisfy schema/{schemaName}:\n" +
                    string.Join("\n", errors) + "\n\nDocument was:\n" + json);
    }

    /// <summary>
    ///     JsonSchema.Net 8 evaluates <see cref="JsonElement" /> instances only; the suites build and
    ///     mutate their documents as <see cref="JsonNode" />, so this bridges the two at the call site.
    /// </summary>
    public static EvaluationResults Evaluate(this JsonSchema schema, JsonNode? instance, EvaluationOptions options) =>
        schema.Evaluate(JsonSerializer.SerializeToElement(instance), options);

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }
}
