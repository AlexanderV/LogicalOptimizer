using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogicalOptimizer;

/// <summary>
///     Renders an <see cref="EquivalenceCheckResult" /> as the stable, machine-readable JSON
///     report of the <c>check</c> verb's <c>--format=json</c> mode. A separate document type
///     from <see cref="JsonReportWriter" />'s optimize report — the two answer different
///     questions — with its own published schema (<c>schema/cli-check-report-v1.schema.json</c>)
///     and the same conventions: versioned by <c>schemaVersion</c>, camelCase, additive-only
///     within a version, exactly one document per invocation on stdout.
///     <para>
///         <c>left</c>/<c>right</c> are the two expressions exactly as the CLI received them.
///         <c>verdict</c> is the outcome (<c>equivalent</c> / <c>not_equivalent</c> /
///         <c>unknown</c>); a determined verdict is always PROVEN — equivalence by exhaustive
///         table or UNSAT miter, inequivalence by the concrete <c>counterexample</c> witness.
///         <c>unknown</c> means the conflict budget ran out, and carries neither.
///     </para>
/// </summary>
internal static class CheckReportWriter
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

    public static void Write(TextWriter writer, string left, string right, EquivalenceCheckResult result)
    {
        var report = new CheckReport
        {
            SchemaVersion = SchemaVersion,
            Left = left,
            Right = right,
            Verdict = result.AreEquivalent switch
            {
                true => "equivalent",
                false => "not_equivalent",
                null => "unknown"
            },
            // Omitted (not false) when unknown: absence signals "no verdict", so a consumer
            // branching on `equivalent` alone cannot mistake a timeout for a disproof.
            Equivalent = result.AreEquivalent,
            Counterexample = result.Counterexample is { } cex ? Sorted(cex) : null
        };

        writer.WriteLine(JsonSerializer.Serialize(report, Options));
    }

    /// <param name="side">Which expression failed — <c>left</c> or <c>right</c> — when known.</param>
    public static void WriteError(TextWriter writer, string left, string right, string message,
        ParseDiagnostic? diagnostic = null, string? side = null)
    {
        var error = diagnostic is null
            ? new CheckError { Code = "processing_error", Message = message, Side = side }
            : new CheckError
            {
                Code = diagnostic.Code.ToString(),
                Message = diagnostic.Message,
                Side = side,
                Position = diagnostic.Position,
                Length = diagnostic.Length,
                Expected = diagnostic.Expected.Count > 0 ? diagnostic.Expected : null,
                Snippet = diagnostic.Snippet
            };

        var report = new CheckReport
        {
            SchemaVersion = SchemaVersion,
            Left = left,
            Right = right,
            Error = error
        };
        writer.WriteLine(JsonSerializer.Serialize(report, Options));
    }

    /// <summary>Deterministic key order, so the report is diffable and the golden examples stable.</summary>
    private static SortedDictionary<string, bool> Sorted(IReadOnlyDictionary<string, bool> assignment)
    {
        var sorted = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (variable, value) in assignment) sorted[variable] = value;
        return sorted;
    }

    private sealed class CheckReport
    {
        public int SchemaVersion { get; init; }
        public string Left { get; init; } = "";
        public string Right { get; init; } = "";
        public string? Verdict { get; init; }
        public bool? Equivalent { get; init; }
        public IReadOnlyDictionary<string, bool>? Counterexample { get; init; }
        public CheckError? Error { get; init; }
    }

    private sealed class CheckError
    {
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
        public string? Side { get; init; }
        public int? Position { get; init; }
        public int? Length { get; init; }
        public IReadOnlyList<string>? Expected { get; init; }
        public string? Snippet { get; init; }
    }
}
