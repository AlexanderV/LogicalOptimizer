using System.Text;

namespace LogicalOptimizer;

/// <summary>What kind of decision a <see cref="OptimizationTraceEntry" /> records.</summary>
public enum OptimizationTraceCategory
{
    /// <summary>Which engine/zone was selected for this expression, and on what threshold.</summary>
    EngineSelection,

    /// <summary>A work budget that applied to a phase, and the limit value in force.</summary>
    Budget,

    /// <summary>A candidate expression was produced and costed.</summary>
    Candidate,

    /// <summary>A candidate was adopted as the new best, with the cost that justified it.</summary>
    Adopted,

    /// <summary>A candidate was not adopted, with the reason.</summary>
    Rejected,

    /// <summary>An equivalence/minimality proof was attempted, and how it was discharged.</summary>
    Proof,

    /// <summary>A phase gave up its intended path and continued on a lesser one.</summary>
    Fallback,

    /// <summary>A reported outcome (minimality provenance, computation status).</summary>
    Status
}

/// <summary>
///     One recorded decision from an optimization run. <see cref="Message" /> explains the
///     decision in words; <see cref="Data" /> carries the same facts as machine-readable
///     key/value pairs so a production log or diagnostics UI can filter on them.
/// </summary>
public sealed class OptimizationTraceEntry
{
    private static readonly IReadOnlyDictionary<string, string> NoData =
        new Dictionary<string, string>();

    internal OptimizationTraceEntry(OptimizationTraceCategory category, string step, string message,
        IReadOnlyDictionary<string, string>? data = null)
    {
        Category = category;
        Step = step;
        Message = message;
        Data = data ?? NoData;
    }

    /// <summary>The kind of decision this entry records.</summary>
    public OptimizationTraceCategory Category { get; }

    /// <summary>The pipeline phase that produced the entry, e.g. <c>ExactMinimization</c>.</summary>
    public string Step { get; }

    /// <summary>Human-readable explanation of the decision and its reason.</summary>
    public string Message { get; }

    /// <summary>Machine-readable details (thresholds, costs, limits, verdicts).</summary>
    public IReadOnlyDictionary<string, string> Data { get; }

    public override string ToString()
    {
        var text = $"[{Category}] {Step}: {Message}";
        if (Data.Count == 0) return text;
        var details = string.Join(", ", Data.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{text} ({details})";
    }
}

/// <summary>
///     Opt-in diagnostic record of how an optimization result was reached: which engine was
///     chosen and why, which budgets applied, which candidates were produced, which one was
///     adopted or rejected and on what cost, how equivalence and minimality were discharged,
///     and why a run ended on a fallback or a non-proven status.
///     <para>
///         Enable it with <see cref="OptimizationOptions.IncludeTrace" /> and read it from
///         <see cref="OptimizationResult.Trace" />. It is a diagnostic aid, not a stability
///         contract: entry wording and ordering may change between minor versions, so log it or
///         display it rather than asserting on exact text.
///     </para>
/// </summary>
public sealed class OptimizationTrace
{
    private readonly List<OptimizationTraceEntry> _entries;

    internal OptimizationTrace(List<OptimizationTraceEntry> entries)
    {
        _entries = entries;
    }

    /// <summary>The recorded decisions, in the order the pipeline made them.</summary>
    public IReadOnlyList<OptimizationTraceEntry> Entries => _entries;

    /// <summary>Entries of one category, e.g. every <see cref="OptimizationTraceCategory.Proof" />.</summary>
    public IEnumerable<OptimizationTraceEntry> OfCategory(OptimizationTraceCategory category)
    {
        return _entries.Where(e => e.Category == category);
    }

    /// <summary>One line per entry, suitable for a log or a diagnostics pane.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var entry in _entries) builder.AppendLine(entry.ToString());
        return builder.ToString();
    }
}

/// <summary>
///     Collects trace entries during a run. A single instance is threaded through the pipeline;
///     when tracing is off the facade passes <c>null</c> and every recording site is a null check,
///     so a non-tracing run pays nothing.
/// </summary>
internal sealed class OptimizationTraceRecorder
{
    private readonly List<OptimizationTraceEntry> _entries = new();

    public void Add(OptimizationTraceCategory category, string step, string message,
        IReadOnlyDictionary<string, string>? data = null)
    {
        _entries.Add(new OptimizationTraceEntry(category, step, message, data));
    }

    public OptimizationTrace Build()
    {
        return new OptimizationTrace(_entries);
    }
}
