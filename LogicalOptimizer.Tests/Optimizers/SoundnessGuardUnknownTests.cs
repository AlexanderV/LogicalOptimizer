using System.Linq;
using LogicalOptimizer.Rewrite;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The soundness guard accepts a rewrite only when equivalence is positively PROVEN.
///     Beyond the 12-variable truth-table range the proof is a SAT miter; a budget-exhausted
///     Unknown verdict must be treated as a failure to verify (roll back to the input), never
///     as tacit success. These tests pin that contract.
/// </summary>
public class SoundnessGuardUnknownTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    [Fact]
    public void SatUnknown_RollsBackToInput_InsteadOfShippingUnverifiedRewrite()
    {
        // 13 variables => the guard uses the SAT miter (beyond the truth-table range).
        // x01 | (x01 & x02 & ... & x13) absorbs to x01, so the engine WOULD simplify it.
        var vars = Enumerable.Range(1, 13).Select(i => $"x{i:D2}").ToList();
        var expression = $"{vars[0]} | " + string.Join(" & ", vars);
        var input = Parse(expression);

        // A zero conflict budget cannot prove the (UNSAT) miter => Unknown.
        var budget = new ResourceBudget { SoundnessGuardConflictLimit = 0 };
        var metrics = new OptimizationMetrics();
        var optimized = new RewriteEngine().Optimize(input, metrics, default, budget);

        // Unknown is treated as unverified: the rewrite is discarded and the guard rolls back
        // to the exact input reference, not merely to some equivalent form.
        Assert.True(metrics.RuleApplicationCount.ContainsKey("SoundnessRollback"),
            "Guard did not roll back on an unverifiable (Unknown) rewrite");
        Assert.Same(input, optimized);
    }

    [Fact]
    public void GenerousBudget_ProvesEquivalence_AndKeepsTheSimplification()
    {
        // Same expression, but now the guard has enough budget to prove the miter UNSAT,
        // so the verified absorption to x01 is kept: Unknown-rollback must not fire for
        // rewrites that CAN be proven.
        var vars = Enumerable.Range(1, 13).Select(i => $"x{i:D2}").ToList();
        var expression = $"{vars[0]} | " + string.Join(" & ", vars);
        var input = Parse(expression);

        var metrics = new OptimizationMetrics();
        var optimized = new RewriteEngine().Optimize(input, metrics, default, ResourceBudget.Default);

        Assert.False(metrics.RuleApplicationCount.ContainsKey("SoundnessRollback"));
        Assert.Equal("x01", optimized.ToString());
    }
}
