using System.Numerics;
using LogicalOptimizer.Tests.Spikes;
using Xunit;
using Xunit.Abstractions;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Production tests for the public <see cref="FormulaAnalysis.CountProjectedModels" /> API
///     (P1.4). Projected model counting counts the number of DISTINCT assignments over a chosen
///     subset of variables that extend to some model of the formula. The shipped SAT
///     blocking-enumeration engine is checked against an independent brute-force
///     projected-and-deduplicated oracle:
///     <list type="bullet">
///         <item>EXHAUSTIVELY for every Boolean function up to 4 variables and every projection subset;</item>
///         <item>on randomized larger instances (up to 10 variables) with random projections;</item>
///         <item>on the edge cases from the decision doc (empty projection, project-all,
///             many-to-one overcount trap, free projection variables, budget, cancellation).</item>
///     </list>
///     The oracle (<see cref="ProjectedModelCounting.BruteForceProjected" />) and truth-table
///     builder are reused from the design spike; the assertions here target the PUBLIC API.
/// </summary>
public class ProjectedModelCountingTests
{
    private readonly ITestOutputHelper _output;

    public ProjectedModelCountingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static IReadOnlyList<string> Vars(int n) =>
        Enumerable.Range(0, n).Select(i => $"x{i}").ToList();

    /// <summary>
    ///     Assert the public API returns an exact count equal to the independent oracle over the
    ///     given universe (the formula's variables union the projection).
    /// </summary>
    private static void AssertExactEqualsOracle(
        AstNode formula, IReadOnlyList<string> universe, IReadOnlyList<string> projection, BigInteger expected)
    {
        var oracle = ProjectedModelCounting.BruteForceProjected(formula, universe, projection);
        Assert.Equal(expected, oracle);

        var result = FormulaAnalysis.CountProjectedModels(formula, projection);
        Assert.Equal(ProjectedCountStatus.Exact, result.Status);
        Assert.Equal(oracle, result.Count);
    }

    // -----------------------------------------------------------------------------------------
    //  Exhaustive: EVERY Boolean function up to 4 variables, EVERY projection subset.
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExhaustiveAgreement_AllFunctions_UpToThreeVariables(int n)
    {
        var (checks, total) = RunExhaustive(n);
        _output.WriteLine($"n={n}: {checks} (function, projection) checks passed; sum of projected counts={total}");
        Assert.True(checks > 0);
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void ExhaustiveAgreement_AllFourVariableFunctions()
    {
        // 2^(2^4) = 65 536 functions x 16 projection subsets = ~1.05M independent agreements.
        // One SAT enumeration per check runs over ~a few seconds, so Category=Exhaustive (matched
        // EXACTLY by the CI gate's Category!=Exhaustive filter) keeps it out of the fast PR gate
        // while it still runs in the full/nightly suite. The roadmap acceptance criterion demands
        // exhaustive verification for all functions up to 4 variables.
        var (checks, total) = RunExhaustive(4);
        _output.WriteLine($"n=4: {checks} (function, projection) checks passed; sum of projected counts={total}");
        Assert.Equal(65536 * 16, checks);
    }

    /// <summary>
    ///     For all 2^(2^n) functions and all 2^n projection subsets, assert the public API is
    ///     exact and equal to the oracle. Returns the number of checks and the summed projected
    ///     count (a cheap end-to-end fingerprint of the traversed search space).
    /// </summary>
    private static (long Checks, BigInteger Total) RunExhaustive(int n)
    {
        var variables = Vars(n);
        var rows = 1 << n; // truth-table rows
        var functions = 1 << rows; // number of Boolean functions on n variables
        long checks = 0;
        BigInteger total = 0;

        for (var f = 0; f < functions; f++)
        {
            var table = new bool[rows];
            for (var m = 0; m < rows; m++) table[m] = (f & (1 << m)) != 0;
            var formula = ProjectedModelCounting.TruthTableToAst(table, variables);

            for (var subset = 0; subset < 1 << n; subset++)
            {
                var projection = new List<string>();
                for (var j = 0; j < n; j++)
                    if ((subset & (1 << j)) != 0) projection.Add(variables[j]);

                var oracle = ProjectedModelCounting.BruteForceProjected(formula, variables, projection);
                var result = FormulaAnalysis.CountProjectedModels(formula, projection);

                if (result.Status != ProjectedCountStatus.Exact || result.Count != oracle)
                    Assert.Fail($"mismatch: n={n} f={f} subset={subset} oracle={oracle} " +
                                $"status={result.Status} count={result.Count}");

                total += result.Count!.Value;
                checks++;
            }
        }

        return (checks, total);
    }

    // -----------------------------------------------------------------------------------------
    //  Randomized larger instances with random projection subsets (up to ~10 variables).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void RandomizedAgreement_LargerInstances_RandomProjections()
    {
        var rng = new Random(20260729);
        var trials = 0;
        for (var t = 0; t < 400; t++)
        {
            var variableCount = rng.Next(1, 11);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = RandomExpressions.Parse(expression);
            var universe = ast.GetVariables().OrderBy(v => v).ToList();
            if (universe.Count == 0) continue; // constant-folded formula; covered by edge-case tests

            // Random projection subset over the universe.
            var projection = universe.Where(_ => rng.Next(2) == 0).ToList();

            var oracle = ProjectedModelCounting.BruteForceProjected(ast, universe, projection);
            var result = FormulaAnalysis.CountProjectedModels(ast, projection);

            Assert.True(result.Status == ProjectedCountStatus.Exact && result.Count == oracle,
                $"Trial {t}: mismatch for '{expression}' proj=[{string.Join(",", projection)}] " +
                $"oracle={oracle} status={result.Status} count={result.Count}");
            trials++;
        }

        Assert.True(trials > 300, $"Expected many non-degenerate trials, got {trials}");
    }

    // -----------------------------------------------------------------------------------------
    //  Edge cases (decision doc / roadmap section 9 acceptance criteria).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void EmptyProjection_IsOneForSat_AndZeroForUnsat()
    {
        var empty = new List<string>();

        // SAT formula: the empty projection has exactly the empty tuple as its single model.
        var sat = RandomExpressions.Parse("x0 | x1");
        var satResult = FormulaAnalysis.CountProjectedModels(sat, empty);
        Assert.Equal(ProjectedCountStatus.Exact, satResult.Status);
        Assert.Equal(BigInteger.One, satResult.Count);

        // UNSAT formula: no models, so the empty projection is empty too.
        var unsat = new AndNode(new VariableNode("x0"), new NotNode(new VariableNode("x0")));
        var unsatResult = FormulaAnalysis.CountProjectedModels(unsat, empty);
        Assert.Equal(ProjectedCountStatus.Exact, unsatResult.Status);
        Assert.Equal(BigInteger.Zero, unsatResult.Count);
    }

    [Fact]
    public void ProjectingAllVariables_EqualsCountModels()
    {
        var rng = new Random(4242);
        for (var t = 0; t < 60; t++)
        {
            var variableCount = rng.Next(1, 9);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5));
            var ast = RandomExpressions.Parse(expression);
            var universe = ast.GetVariables().OrderBy(v => v).ToList();
            if (universe.Count == 0) continue;

            // Ground truth: exact #SAT over all variables via the shipped d-DNNF counter.
            var countModels = KnowledgeCompilation.CompileToDnnf(ast).CountModels();

            var result = FormulaAnalysis.CountProjectedModels(ast, universe);
            Assert.Equal(ProjectedCountStatus.Exact, result.Status);
            Assert.Equal(countModels, result.Count);
        }
    }

    [Fact]
    public void ManyToOneProjection_DoesNotOvercount()
    {
        // f depends only on x0, but the universe is {x0, x1, x2}. All four full models with
        // x0 = true (x1, x2 free) collapse onto the single projected model {x0 = true}. A naive
        // "count full models" would report 4; the correct projected count is 1.
        var universe = Vars(3);
        var formula = new VariableNode("x0");
        AssertExactEqualsOracle(formula, universe, new List<string> { "x0" }, BigInteger.One);

        // Project onto {x0, x1}: x2 is forgotten. Models with x0=true: (x1,x2) free -> projected
        // {x0=1,x1=0},{x0=1,x1=1} => 2 distinct. x0=false has no models. Expect 2, not 4.
        AssertExactEqualsOracle(formula, universe, new List<string> { "x0", "x1" }, new BigInteger(2));
    }

    [Fact]
    public void OvercountTrap_WorkedExample_FromDesignDoc()
    {
        // f = (x1 & y) | (x2 & !y), project the decision variable y OUT onto {x1, x2}.
        //   y=1 branch models projected onto {x1,x2}: (1,0), (1,1)
        //   y=0 branch models projected onto {x1,x2}: (0,1), (1,1)
        // Union = {(1,0),(1,1),(0,1)} => 3 distinct. Naive OR-branch summation gives 2+2 = 4,
        // double-counting (1,1). The shipped engine must report 3.
        var formula = new OrNode(
            new AndNode(new VariableNode("x1"), new VariableNode("y")),
            new AndNode(new VariableNode("x2"), new NotNode(new VariableNode("y"))));
        var universe = new List<string> { "x1", "x2", "y" };

        AssertExactEqualsOracle(formula, universe, new List<string> { "x1", "x2" }, new BigInteger(3));
    }

    [Fact]
    public void FreeProjectionVariable_MultipliesCountByTwo()
    {
        // f = x0 (SAT). Projecting onto {x0} alone gives 1 distinct projected model. Adding a
        // variable NOT in the formula ("z") is not an error: z is free, both its values extend
        // to a model, so it doubles the count. {x0} -> 1, {x0, z} -> 2, {x0, z, w} -> 4.
        var formula = new VariableNode("x0");

        var one = FormulaAnalysis.CountProjectedModels(formula, new List<string> { "x0" });
        Assert.Equal(ProjectedCountStatus.Exact, one.Status);
        Assert.Equal(BigInteger.One, one.Count);

        var two = FormulaAnalysis.CountProjectedModels(formula, new List<string> { "x0", "z" });
        Assert.Equal(new BigInteger(2), two.Count);

        var four = FormulaAnalysis.CountProjectedModels(formula, new List<string> { "x0", "z", "w" });
        Assert.Equal(new BigInteger(4), four.Count);

        // A purely free projection over a SAT formula: 2^(free) distinct projected tuples.
        var justFree = FormulaAnalysis.CountProjectedModels(formula, new List<string> { "z", "w" });
        Assert.Equal(new BigInteger(4), justFree.Count);
    }

    [Fact]
    public void UnsatFormula_FreeProjectionVariables_StaysZero()
    {
        // Free variables never manufacture models: an unsatisfiable formula has 0 projected
        // models no matter how many free projection variables are added (0 * 2^k = 0).
        var unsat = new AndNode(new VariableNode("x0"), new NotNode(new VariableNode("x0")));
        var result = FormulaAnalysis.CountProjectedModels(unsat, new List<string> { "x0", "z", "w" });
        Assert.Equal(ProjectedCountStatus.Exact, result.Status);
        Assert.Equal(BigInteger.Zero, result.Count);
    }

    // -----------------------------------------------------------------------------------------
    //  Budget / cancellation contract: a partial run is NEVER passed off as exact.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ModelBudget_ReportsBudgetExhausted_WithNullCount_NeverExact()
    {
        // OR of 6 variables projected onto all 6 has 63 distinct projected models. A budget that
        // caps the enumerated-model count at 10 must stop and report BudgetExhausted with NO
        // count, not a wrong "exact 10".
        var universe = Vars(6);
        AstNode formula = new OrNode(universe.Select(v => (AstNode)new VariableNode(v)).ToList());

        var limited = FormulaAnalysis.CountProjectedModels(formula, universe,
            new ResourceBudget { CoverStepLimit = 10 });
        Assert.Equal(ProjectedCountStatus.BudgetExhausted, limited.Status);
        Assert.Null(limited.Count);

        // With the default budget the same query is exact: #SAT of (OR of 6) = 63.
        var full = FormulaAnalysis.CountProjectedModels(formula, universe);
        Assert.Equal(ProjectedCountStatus.Exact, full.Status);
        Assert.Equal(new BigInteger(63), full.Count);
    }

    [Fact]
    public void ConflictBudget_NeverPassesPartialAsExact()
    {
        // A zero conflict budget can force an Unknown SAT verdict before a count is reached; the
        // result must then be BudgetExhausted with the count withheld — never a partial as exact.
        var expression = RandomExpressions.Generate(new Random(1), 4, 4);
        var ast = RandomExpressions.Parse(expression);
        var vars = ast.GetVariables().OrderBy(v => v).ToList();
        if (vars.Count == 0) return;

        var result = FormulaAnalysis.CountProjectedModels(ast, vars,
            new ResourceBudget { SatConflictLimit = 0 });

        // Either the instance solved with zero conflicts (trivial) -> Exact, or the budget
        // tripped -> BudgetExhausted with no count. In neither case is a partial dressed as exact.
        if (result.Status != ProjectedCountStatus.Exact)
        {
            Assert.Equal(ProjectedCountStatus.BudgetExhausted, result.Status);
            Assert.Null(result.Count);
        }
    }

    [Fact]
    public void PreCancelledToken_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var universe = Vars(6);
        AstNode formula = new OrNode(universe.Select(v => (AstNode)new VariableNode(v)).ToList());

        Assert.Throws<OperationCanceledException>(() =>
            FormulaAnalysis.CountProjectedModels(formula, universe, cancellationToken: source.Token));
    }

    [Fact]
    public void Cancellation_ObservedMidEnumeration_OnLargeInstance()
    {
        // OR of 20 variables projected onto all 20 has 2^20 - 1 = 1,048,575 distinct projected
        // models, each requiring an incremental solve — far more work than can complete in the
        // cancellation window. Cancelling shortly after start must surface as an
        // OperationCanceledException from the enumeration loop, not run to completion.
        var universe = Vars(20);
        AstNode formula = new OrNode(universe.Select(v => (AstNode)new VariableNode(v)).ToList());

        using var source = new CancellationTokenSource();
        source.CancelAfter(TimeSpan.FromMilliseconds(100));

        Assert.Throws<OperationCanceledException>(() =>
            FormulaAnalysis.CountProjectedModels(formula, universe, cancellationToken: source.Token));
    }
}
