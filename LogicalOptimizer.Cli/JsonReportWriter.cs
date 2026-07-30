using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogicalOptimizer;

/// <summary>
///     How the expression under analysis reached the CLI. Reported as <c>sourceFormat</c> so a
///     consumer can tell an expression that was analyzed as written from one the tool derived.
/// </summary>
internal enum CliInputSource
{
    /// <summary>A boolean expression, analyzed exactly as received.</summary>
    Expression,

    /// <summary>A CSV truth table — inline text or a <c>*.csv</c> path — that an expression was derived from.</summary>
    Csv
}

/// <summary>
///     Renders an <see cref="OptimizationResult" /> as a stable, machine-readable JSON report
///     (the <c>--format=json</c> CLI mode). The document shape is versioned by
///     <c>schemaVersion</c>; fields are only ever added within a version, never renamed or
///     removed, so a consumer can parse it safely as a CI artifact.
///     <para>
///         <c>input</c> is the argument the CLI RECEIVED, which is what makes the artifact
///         auditable: for a CSV source it names the CSV text or the <c>*.csv</c> path, not the
///         sum-of-products derived from it. That derived expression — the one every verdict in the
///         report is about — is reported separately as <c>analyzedExpression</c>. Both call sites
///         must state the received input explicitly; taking it from
///         <see cref="OptimizationResult.Original" /> is what made the CSV report unable to name
///         its own input.
///     </para>
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

    /// <param name="receivedInput">The argument as the CLI received it — never the derived expression.</param>
    /// <param name="source">What <paramref name="receivedInput" /> is: an expression or a CSV table.</param>
    public static void Write(TextWriter writer, OptimizationResult result, string receivedInput,
        CliInputSource source)
    {
        var before = CliExpressionMetrics.TryCountLiterals(result.Original);
        var after = CliExpressionMetrics.TryCountLiterals(result.Optimized);

        var report = new JsonReport
        {
            SchemaVersion = SchemaVersion,
            Input = receivedInput,
            SourceFormat = Name(source),
            // Omitted when the input WAS the analyzed expression, so the ordinary report is
            // unchanged and presence of the field is itself the signal that a derivation happened.
            AnalyzedExpression = result.Original == receivedInput ? null : result.Original,
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
            Variables = result.Variables,
            // Present only with --trace. Diagnostic: entry wording/order is not part of the
            // schema contract, unlike the fields above.
            Trace = result.Trace?.Entries.Select(e => new JsonTraceEntry
            {
                Category = e.Category.ToString(),
                Step = e.Step,
                Message = e.Message,
                Data = e.Data.Count > 0 ? e.Data : null
            }).ToList()
        };

        writer.WriteLine(JsonSerializer.Serialize(report, Options));
    }

    /// <param name="receivedInput">The argument as the CLI received it — never the derived expression.</param>
    /// <param name="source">What <paramref name="receivedInput" /> is: an expression or a CSV table.</param>
    /// <param name="analyzedExpression">
    ///     The expression derived from <paramref name="receivedInput" />, when the derivation
    ///     succeeded and the failure came later. Null when the input never got that far.
    /// </param>
    public static void WriteError(TextWriter writer, string receivedInput, CliInputSource source,
        string message, ParseDiagnostic? diagnostic = null, string? analyzedExpression = null)
    {
        var error = diagnostic is null
            ? new JsonError { Code = "processing_error", Message = message }
            : new JsonError
            {
                Code = diagnostic.Code.ToString(),
                Message = diagnostic.Message,
                Position = diagnostic.Position,
                Length = diagnostic.Length,
                Expected = diagnostic.Expected.Count > 0 ? diagnostic.Expected : null,
                Snippet = diagnostic.Snippet
            };

        var report = new JsonReport
        {
            SchemaVersion = SchemaVersion,
            Input = receivedInput,
            SourceFormat = Name(source),
            AnalyzedExpression = analyzedExpression == receivedInput ? null : analyzedExpression,
            Error = error
        };
        writer.WriteLine(JsonSerializer.Serialize(report, Options));
    }

    private static string Name(CliInputSource source) => source switch
    {
        CliInputSource.Csv => "csv",
        _ => "expression"
    };

    private sealed class JsonReport
    {
        public int SchemaVersion { get; init; }
        public string Input { get; init; } = "";
        public string SourceFormat { get; init; } = "";
        public string? AnalyzedExpression { get; init; }
        public string? Optimized { get; init; }
        public bool? Equivalent { get; init; }
        public string? Minimality { get; init; }
        public JsonCost? Cost { get; init; }
        public JsonForm? Cnf { get; init; }
        public JsonForm? Dnf { get; init; }
        public string? Advanced { get; init; }
        public IReadOnlyList<string>? Variables { get; init; }
        public IReadOnlyList<JsonTraceEntry>? Trace { get; init; }
        public JsonError? Error { get; init; }
    }

    private sealed class JsonTraceEntry
    {
        public string Category { get; init; } = "";
        public string Step { get; init; } = "";
        public string Message { get; init; } = "";
        public IReadOnlyDictionary<string, string>? Data { get; init; }
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
        public int? Position { get; init; }
        public int? Length { get; init; }
        public IReadOnlyList<string>? Expected { get; init; }
        public string? Snippet { get; init; }
    }
}
