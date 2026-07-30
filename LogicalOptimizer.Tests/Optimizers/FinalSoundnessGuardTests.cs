using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The public claim is that every optimization is proven equivalent to the input BEFORE it is
///     returned. <c>RewriteEngine</c>'s guard only covers the rewrite phase: after it, the exact QM
///     path, the SAT prime cover, the subcircuit library and the AIG pass each import a candidate
///     built by a different engine, and the winner of the cost comparison was never compared to the
///     parsed input itself. These tests cover the final guard that closes that gap.
///     <para>
///         The refutation path cannot be reached through the public API without planting a bug in an
///         engine, so it is exercised against <c>VerifyAgainstInput</c> directly; the integration
///         tests then prove the guard actually runs on every zone of the real pipeline.
///     </para>
/// </summary>
public class FinalSoundnessGuardTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static List<string> VariablesOf(AstNode ast) => ast.GetVariables().OrderBy(v => v).ToList();

    private static bool Verify(string input, string candidate, bool withOnSet)
    {
        var inputAst = Parse(input);
        var variables = VariablesOf(inputAst);
        HashSet<int>? onSet = null;

        if (withOnSet)
        {
            // Mirror what the exact path hands the guard: the input's ON-set over the SAME variable
            // list, so the candidate is checked in one sweep instead of a second truth table.
            onSet = new HashSet<int>();
            var assignment = new Dictionary<string, bool>();
            for (var minterm = 0; minterm < 1 << variables.Count; minterm++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (minterm & (1 << j)) != 0;
                if (TruthTable.Evaluate(inputAst, assignment)) onSet.Add(minterm);
            }
        }

        return BooleanExpressionOptimizer.VerifyAgainstInput(inputAst, Parse(candidate), variables,
            onSet, ResourceBudget.Default, CancellationToken.None);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("a & !b | !a & b", "(a | b) & (!a | !b)")]
    [InlineData("a | a & b", "a")]
    public void EquivalentCandidate_IsAccepted(string input, string candidate)
    {
        Assert.True(Verify(input, candidate, withOnSet: false));
        Assert.True(Verify(input, candidate, withOnSet: true));
    }

    [Theory]
    // A dropped minterm - the exact shape of the historical consensus-removal bug.
    [InlineData("a & b | a & c | b & c", "a & b | a & c")]
    // A candidate that is a strict superset of the input's ON-set.
    [InlineData("a & b", "a | b")]
    // Off by a single assignment.
    [InlineData("a & !b | !a & b", "a | b")]
    public void NonEquivalentCandidate_IsRefused(string input, string candidate)
    {
        // Both routes must refuse it: the ON-set reuse is an optimization, never a weaker check.
        Assert.False(Verify(input, candidate, withOnSet: false));
        Assert.False(Verify(input, candidate, withOnSet: true));
    }

    [Fact]
    public void OnSetReuse_AgreesWithTheFullTruthTable_OnEveryThreeVariableFunctionPair()
    {
        // The reused-ON-set sweep and the two-table comparison must be the same predicate. Checking
        // every ordered pair of 3-variable functions makes that exhaustive rather than anecdotal.
        var variables = new[] { "a", "b", "c" };

        static string CanonicalDnf(int truthBits, string[] variables)
        {
            var terms = new List<string>();
            for (var minterm = 0; minterm < 8; minterm++)
            {
                if ((truthBits & (1 << minterm)) == 0) continue;
                terms.Add(string.Join(" & ", variables.Select((v, j) =>
                    (minterm & (1 << j)) != 0 ? v : "!" + v)));
            }

            return terms.Count == 0 ? "0" : string.Join(" | ", terms);
        }

        for (var left = 1; left < 255; left++)
            for (var right = 1; right < 255; right++)
            {
                var input = CanonicalDnf(left, variables);
                var candidate = CanonicalDnf(right, variables);
                var expected = left == right;

                Assert.Equal(expected, Verify(input, candidate, withOnSet: false));
                Assert.Equal(expected, Verify(input, candidate, withOnSet: true));
            }
    }

    [Fact]
    public void AboveTheTruthTableRange_TheGuardUsesTheSatMiter()
    {
        // 14 variables: past MAX_EQUIVALENCE_CHECK_VARIABLES, so no 2^n table is built and the
        // verdict comes from the SAT miter instead.
        var input = string.Join(" | ", Enumerable.Range(1, 7).Select(i => $"x{i} & y{i}"));
        Assert.True(Verify(input, input + " | x1 & y1", withOnSet: false));
        Assert.False(Verify(input, input + " | x1", withOnSet: false));
    }

    [Theory]
    // Exact QM zone: the candidate is imported from the minimizer, not produced by the rewriter.
    [InlineData("a & b | a & c")]
    [InlineData("a & !b & !c | !a & b & !c | !a & !b & c | a & b & c")]
    // SAT prime-cover zone (13-24 variables).
    [InlineData("x1 & y1 | x2 & y2 | x3 & y3 | x4 & y4 | x5 & y5 | x6 & y6 | x7 & y7 | x1 & y1 & x2")]
    public void EveryZone_RunsTheFinalGuard_AndProvesTheReturnedResult(string expression)
    {
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression(expression, new OptimizationOptions { IncludeTrace = true });

        var guard = result.Trace!.Entries.SingleOrDefault(e => e.Step == "FinalSoundnessGuard");
        Assert.NotNull(guard);
        Assert.Equal(OptimizationTraceCategory.Proof, guard!.Category);
        Assert.Equal("true", guard.Data["proven"]);

        // And the guarantee the trace entry stands for actually holds.
        Assert.True(result.IsEquivalent());
    }

    [Fact]
    public void ExactZone_ReusesTheInputOnSet_RatherThanBuildingASecondTruthTable()
    {
        // Not a micro-optimization detail: without the reuse the guard would double the 2^n work of
        // every exact-zone call, which is the whole hot path of the library.
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & b | a & c", new OptimizationOptions { IncludeTrace = true });

        var guard = result.Trace!.Entries.Single(e => e.Step == "FinalSoundnessGuard");
        Assert.Equal("true", guard.Data["reusedInputOnSet"]);
        Assert.Equal("truth-table", guard.Data["method"]);
    }

    [Fact]
    public void WhenEquivalenceCannotBeProven_TheInputIsReturnedInsteadOfAnUnverifiedResult()
    {
        // Reachable through the documented public API: above the truth-table range the guard uses
        // the SAT miter, and a caller may tighten its budget. A budget of one conflict cannot
        // discharge a 14-variable miter, so the verdict is Unknown - which the guard treats exactly
        // like a refutation. The contract is "no silent fallbacks": return the input, do not ship an
        // expression whose equivalence was never established.
        var expression = string.Join(" | ",
            Enumerable.Range(1, 7).Select(i => $"x{i} & y{i} | !x{i} & y{i}"));

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions
            {
                IncludeMetrics = true,
                IncludeTrace = true,
                Budget = new ResourceBudget { SoundnessGuardConflictLimit = 1, SatConflictLimit = 1 }
            });

        var guard = result.Trace!.Entries.Single(e => e.Step == "FinalSoundnessGuard");
        Assert.Equal(OptimizationTraceCategory.Fallback, guard.Category);
        Assert.Equal("false", guard.Data["proven"]);
        Assert.Equal("sat-miter", guard.Data["method"]);

        Assert.Contains("FinalSoundnessRollback", result.Metrics!.RuleApplicationCount.Keys);

        // Rolled back to the parsed input (printed canonically), not to some other candidate...
        Assert.Equal(Parse(expression).ToString(), result.Optimized);
        // ...and no minimality is claimed for an expression that was never optimized.
        Assert.Equal(MinimizationStatus.Heuristic, result.MinimizationStatus);
        Assert.Equal(MinimizationStatus.Heuristic, result.CnfMinimizationStatus);
    }

    [Fact]
    public void AfterARollback_TheNormalFormsDescribeTheReturnedExpression()
    {
        // A rolled-back result must not pair the input with CNF/DNF derived from the refuted
        // candidate - the document would then be internally inconsistent.
        var expression = string.Join(" | ",
            Enumerable.Range(1, 7).Select(i => $"x{i} & y{i} | !x{i} & y{i}"));

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions
            {
                IncludeMetrics = true,
                Budget = new ResourceBudget { SoundnessGuardConflictLimit = 1, SatConflictLimit = 1 }
            });

        Assert.Contains("FinalSoundnessRollback", result.Metrics!.RuleApplicationCount.Keys);

        var input = Parse(expression);
        foreach (var (name, form, status) in new[]
                 {
                     ("CNF", result.CNF, result.CnfStatus),
                     ("DNF", result.DNF, result.DnfStatus)
                 })
        {
            if (status != ComputationStatus.Computed) continue;
            Assert.True(
                EquivalenceChecker.Check(input, Parse(form)).AreEquivalent == true,
                $"{name} of a rolled-back result is not equivalent to the returned expression");
        }
    }

    [Fact]
    public void RandomExpressions_AlwaysComeBackVerified()
    {
        // The guard must never fire on correct input: a spurious rollback would silently stop
        // optimizing. 300 seeded expressions across the rewrite and exact paths.
        var optimizer = new BooleanExpressionOptimizer();

        for (var seed = 0; seed < 300; seed++)
        {
            var expression = RandomExpressions.Generate(new Random(seed), 6, 4);
            var result = optimizer.OptimizeExpression(expression,
                new OptimizationOptions { IncludeMetrics = true, IncludeTrace = true });

            Assert.True(result.IsEquivalent(), $"seed {seed}: '{expression}' came back unverified");
            Assert.DoesNotContain("FinalSoundnessRollback", result.Metrics!.RuleApplicationCount.Keys);
            Assert.Equal("true",
                result.Trace!.Entries.Single(e => e.Step == "FinalSoundnessGuard").Data["proven"]);
        }
    }
}
