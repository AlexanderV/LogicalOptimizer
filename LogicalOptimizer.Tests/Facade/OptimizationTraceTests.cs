using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Covers the opt-in diagnostic trace (<see cref="OptimizationOptions.IncludeTrace" />): it must
///     answer which engine ran and why, which budgets applied, which candidates were costed, which
///     one was adopted or rejected, how equivalence and minimality were discharged, and why a run
///     ended on a fallback — without ever changing the result.
/// </summary>
public class OptimizationTraceTests
{
    private static OptimizationResult Optimize(string expression, OptimizationOptions? options = null)
    {
        return new BooleanExpressionOptimizer().OptimizeExpression(expression,
            options ?? new OptimizationOptions { IncludeTrace = true });
    }

    [Fact]
    public void Trace_IsNull_WhenNotRequested()
    {
        Assert.Null(new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c").Trace);
        Assert.Null(Optimize("a & b | a & c", new OptimizationOptions { IncludeTrace = false }).Trace);
    }

    [Fact]
    public void Trace_DoesNotChangeTheResult()
    {
        const string expression = "a & b | a & c | b & c";
        var withoutTrace = Optimize(expression, new OptimizationOptions { IncludeTrace = false });
        var withTrace = Optimize(expression);

        Assert.Equal(withoutTrace.Optimized, withTrace.Optimized);
        Assert.Equal(withoutTrace.CNF, withTrace.CNF);
        Assert.Equal(withoutTrace.DNF, withTrace.DNF);
        Assert.Equal(withoutTrace.MinimizationStatus, withTrace.MinimizationStatus);
        Assert.NotNull(withTrace.Trace);
    }

    [Fact]
    public void Trace_ExactZone_ReportsEngineBudgetProofAndFinalStatus()
    {
        var result = Optimize("a & b | a & c");
        var trace = result.Trace!;

        var zone = Assert.Single(trace.Entries, e => e.Step == "ZoneSelection");
        Assert.Equal(OptimizationTraceCategory.EngineSelection, zone.Category);
        Assert.Equal("exact-qm", zone.Data["engine"]);
        Assert.Equal("3", zone.Data["variables"]);
        // The thresholds that drove the choice are reported, not just the outcome.
        Assert.Equal("12", zone.Data["exactGate"]);
        Assert.Equal("10", zone.Data["guaranteeZone"]);

        // Budget actually in force: unbounded inside the guarantee zone.
        var budget = Assert.Single(trace.OfCategory(OptimizationTraceCategory.Budget));
        Assert.Equal("unbounded", budget.Data["qmPairComparisonLimit"]);

        // Which proof path discharged equivalence, and that minimality was proven.
        var guard = Assert.Single(trace.Entries, e => e.Step == "SoundnessGuard");
        Assert.Equal("truth-table", guard.Data["method"]);
        Assert.Equal("true", guard.Data["proven"]);
        Assert.Contains(trace.OfCategory(OptimizationTraceCategory.Proof),
            e => e.Step == "ExactMinimization" && e.Data["minimizationStatus"] == "MinimalProven");

        var status = Assert.Single(trace.Entries, e => e.Step == "Result");
        Assert.Equal(result.MinimizationStatus.ToString(), status.Data["minimizationStatus"]);
        Assert.Equal("4", status.Data["literalsIn"]);
        Assert.Equal("3", status.Data["literalsOut"]);
    }

    [Fact]
    public void Trace_RecordsEveryCandidateWithItsCostAndTheSelectionOutcome()
    {
        var trace = Optimize("a & b | a & c")!.Trace!;

        var candidates = trace.OfCategory(OptimizationTraceCategory.Candidate)
            .Where(e => e.Step == "ExactMinimization")
            .ToList();

        // The rewritten form is compared against both the minimal SOP and its factored form.
        Assert.Equal(new[] { "rewritten", "factored-min-sop", "min-sop" },
            candidates.Select(c => c.Data["candidate"]));
        Assert.All(candidates, c => Assert.True(int.Parse(c.Data["literals"]) > 0));

        // Exactly one selection outcome per selection step: adopted or kept.
        var outcomes = trace.Entries
            .Where(e => e.Step == "ExactMinimization" &&
                        e.Category is OptimizationTraceCategory.Adopted or OptimizationTraceCategory.Rejected)
            .ToList();
        Assert.Single(outcomes);
    }

    [Fact]
    public void Trace_BeyondExactGate_ReportsSatMiterProofAndNonExactEngine()
    {
        // 14 variables: past the exact gate, so equivalence is discharged by the SAT miter and
        // the minimality status must not claim a proof.
        var result = Optimize("a1&a2 | a3&a4 | a5&a6 | a7&a8 | a9&a10 | a11&a12 | a13&a14");
        var trace = result.Trace!;

        var zone = Assert.Single(trace.Entries, e => e.Step == "ZoneSelection");
        Assert.Equal("sat-prime-cover", zone.Data["engine"]);

        var guard = Assert.Single(trace.Entries, e => e.Step == "SoundnessGuard");
        Assert.Equal("sat-miter", guard.Data["method"]);
        Assert.Equal("true", guard.Data["proven"]);

        Assert.Equal(MinimizationStatus.Heuristic, result.MinimizationStatus);
        Assert.DoesNotContain(trace.OfCategory(OptimizationTraceCategory.Proof),
            e => e.Step == "ExactMinimization");
    }

    [Fact]
    public void Trace_ReportsWhyTheAigCandidateWasRejected()
    {
        // A form the AIG rewriter cannot shrink: the rejection must carry a stated reason.
        var trace = Optimize("a & b | a & c")!.Trace!;

        var aig = Assert.Single(trace.Entries, e => e.Step == "AigRewrite");
        Assert.Equal(OptimizationTraceCategory.Rejected, aig.Category);
        Assert.Contains("no structural gain", aig.Message);
    }

    [Fact]
    public void Trace_ReportsTooLargeNormalFormAsFallback()
    {
        // 26 variables: the distributive CNF blows past its cap, which must surface as an
        // explicit fallback rather than a silent "-" in the result.
        var result = Optimize(
            "b1&b2|b3&b4|b5&b6|b7&b8|b9&b10|b11&b12|b13&b14|b15&b16|b17&b18|b19&b20|b21&b22|b23&b24|b25&b26");

        Assert.Equal(ComputationStatus.TooLarge, result.CnfStatus);
        var fallback = Assert.Single(result.Trace!.OfCategory(OptimizationTraceCategory.Fallback),
            e => e.Step == "EquivalentCnf");
        Assert.Equal("TooLarge", fallback.Data["cnfStatus"]);
    }

    [Fact]
    public void Trace_ToString_RendersOneLinePerEntryWithCategoryAndData()
    {
        var trace = Optimize("a & b | a & c")!.Trace!;
        var text = trace.ToString();

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(trace.Entries.Count, lines.Length);
        Assert.Contains("[EngineSelection] ZoneSelection:", text);
        Assert.Contains("engine=exact-qm", text); // structured data is rendered too
    }
}
