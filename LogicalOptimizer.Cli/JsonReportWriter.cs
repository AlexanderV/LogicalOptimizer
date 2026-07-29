using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogicalOptimizer;

/// <summary>
///     Renders an <see cref="OptimizationResult" /> as a stable, machine-readable JSON report
///     (the <c>--format=json</c> CLI mode). The document shape is versioned by
///     <c>schemaVersion</c>; fields are only ever added within a version, never renamed or
///     removed, so a consumer can parse it safely as a CI artifact.
/// </summary>
internal static class JsonReportWriter
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Output goes to stdout/a file, never HTML — keep operators like '&' literal
        // instead of the default & escaping so the artifact is human-readable too.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Write(TextWriter writer, OptimizationResult result)
    {
        var before = CliExpressionMetrics.TryCountLiterals(result.Original);
        var after = CliExpressionMetrics.TryCountLiterals(result.Optimized);

        var report = new JsonReport
        {
            SchemaVersion = SchemaVersion,
            Input = result.Original,
            Optimized = result.Optimized,
            Equivalent = result.IsEquivalent(),
            Minimality = result.MinimizationStatus.ToString(),
            Cost = before is { } b && after is { } a
                ? new JsonCost { OriginalLiterals = b, OptimizedLiterals = a }
                : null,
            Cnf = string.IsNullOrEmpty(result.CNF)
                ? null
                : new JsonForm
                {
                    Expression = result.CNF,
                    Status = result.CnfStatus.ToString(),
                    Minimality = result.CnfMinimizationStatus.ToString()
                },
            Dnf = string.IsNullOrEmpty(result.DNF)
                ? null
                : new JsonForm { Expression = result.DNF, Status = result.DnfStatus.ToString() },
            // Only a genuine XOR/IMP/EQV pattern is worth reporting; when there is none the
            // advanced form just echoes Optimized, so omit it.
            Advanced = !string.IsNullOrEmpty(result.Advanced) && result.Advanced != result.Optimized
                ? result.Advanced
                : null,
            Variables = result.Variables
        };

        writer.WriteLine(JsonSerializer.Serialize(report, Options));
    }

    public static void WriteError(TextWriter writer, string input, string message)
    {
        var report = new JsonReport
        {
            SchemaVersion = SchemaVersion,
            Input = input,
            Error = new JsonError { Code = "processing_error", Message = message }
        };

        writer.WriteLine(JsonSerializer.Serialize(report, Options));
    }

    private sealed class JsonReport
    {
        public int SchemaVersion { get; init; }
        public string Input { get; init; } = "";
        public string? Optimized { get; init; }
        public bool? Equivalent { get; init; }
        public string? Minimality { get; init; }
        public JsonCost? Cost { get; init; }
        public JsonForm? Cnf { get; init; }
        public JsonForm? Dnf { get; init; }
        public string? Advanced { get; init; }
        public IReadOnlyList<string>? Variables { get; init; }
        public JsonError? Error { get; init; }
    }

    private sealed class JsonCost
    {
        public int OriginalLiterals { get; init; }
        public int OptimizedLiterals { get; init; }
    }

    private sealed class JsonForm
    {
        public string Expression { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Minimality { get; init; }
    }

    private sealed class JsonError
    {
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
    }
}
