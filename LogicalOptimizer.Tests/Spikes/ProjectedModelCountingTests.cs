using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace LogicalOptimizer.Tests.Spikes;

/// <summary>
///     Validation for the P1.4 projected-model-counting DESIGN SPIKE. Both prototype strategies
///     (SAT blocking enumeration and BDD existential abstraction) are checked against an
///     independent brute-force projected-and-deduplicated oracle:
///     <list type="bullet">
///         <item>EXHAUSTIVELY for every Boolean function up to 4 variables and every projection subset;</item>
///         <item>on randomized larger instances (up to 10 variables) with random projections;</item>
///         <item>on the edge cases from roadmap section 9 (empty projection, project-all, many-to-one).</item>
///     </list>
///     All of these are also the acceptance criteria of the eventual public feature. This is spike
///     code: it lives in the test project and adds no public API.
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

    /// <summary>Assert that both prototypes return an exact count equal to the oracle.</summary>
    private static void AssertBothAgree(
        AstNode formula, IReadOnlyList<string> universe, IReadOnlyList<string> projection, BigInteger expected)
    {
        var oracle = ProjectedModelCounting.BruteForceProjected(formula, universe, projection);
        Assert.Equal(expected, oracle);

        var sat = ProjectedModelCounting.SatBlockingEnumeration(formula, universe, projection);
        Assert.Equal(ProjectedCountStatus.Exact, sat.Status);
        Assert.Equal(oracle, sat.Count);

        var bdd = ProjectedModelCounting.BddExistentialAbstraction(formula, universe, projection);
        Assert.Equal(ProjectedCountStatus.Exact, bdd.Status);
        Assert.Equal(oracle, bdd.Count);
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
        var (checks, satTotal, bddTotal) = RunExhaustive(n);
        _output.WriteLine(
            $"n={n}: {checks} (function, projection) checks passed; " +
            $"sum of projected counts sat={satTotal} bdd={bddTotal}");
        Assert.True(checks > 0);
    }

    [Fact]
    [Trait("Category", "SpikeExhaustive")]
    public void ExhaustiveAgreement_AllFourVariableFunctions()
    {
        // 2^(2^4) = 65 536 functions x 16 projection subsets = ~1.05M independent agreements.
        // Heavier than a typical unit test but still well under a minute; the roadmap acceptance
        // criterion demands exhaustive verification for all functions up to 4 variables.
        var (checks, satTotal, bddTotal) = RunExhaustive(4);
        _output.WriteLine(
            $"n=4: {checks} (function, projection) checks passed; " +
            $"sum of projected counts sat={satTotal} bdd={bddTotal}");
        Assert.Equal(65536 * 16, checks);
    }

    /// <summary>
    ///     For all 2^(2^n) functions and all 2^n projection subsets, assert oracle == SAT == BDD.
    ///     Returns the number of checks and the summed projected counts from each prototype (a
    ///     cheap end-to-end fingerprint that both strategies traversed the same search space).
    /// </summary>
    private static (long Checks, BigInteger SatTotal, BigInteger BddTotal) RunExhaustive(int n)
    {
        var variables = Vars(n);
        var rows = 1 << n; // truth-table rows
        var functions = 1 << rows; // number of Boolean functions on n variables
        long checks = 0;
        BigInteger satTotal = 0, bddTotal = 0;

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

                var sat = ProjectedModelCounting.SatBlockingEnumeration(formula, variables, projection);
                var bdd = ProjectedModelCounting.BddExistentialAbstraction(formula, variables, projection);

                if (sat.Status != ProjectedCountStatus.Exact || sat.Count != oracle)
                    Assert.Fail($"SAT mismatch: n={n} f={f} subset={subset} oracle={oracle} sat={sat}");
                if (bdd.Status != ProjectedCountStatus.Exact || bdd.Count != oracle)
                    Assert.Fail($"BDD mismatch: n={n} f={f} subset={subset} oracle={oracle} bdd={bdd}");

                satTotal += sat.Count!.Value;
                bddTotal += bdd.Count!.Value;
                checks++;
            }
        }

        return (checks, satTotal, bddTotal);
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
            var sat = ProjectedModelCounting.SatBlockingEnumeration(ast, universe, projection);
            var bdd = ProjectedModelCounting.BddExistentialAbstraction(ast, universe, projection);

            Assert.True(sat.Status == ProjectedCountStatus.Exact && sat.Count == oracle,
                $"Trial {t}: SAT mismatch for '{expression}' proj=[{string.Join(",", projection)}] oracle={oracle} sat={sat}");
            Assert.True(bdd.Status == ProjectedCountStatus.Exact && bdd.Count == oracle,
                $"Trial {t}: BDD mismatch for '{expression}' proj=[{string.Join(",", projection)}] oracle={oracle} bdd={bdd}");
            trials++;
        }

        Assert.True(trials > 300, $"Expected many non-degenerate trials, got {trials}");
    }

    // -----------------------------------------------------------------------------------------
    //  Edge cases (roadmap section 9 acceptance criteria).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void EmptyProjection_IsOneForSat_AndZeroForUnsat()
    {
        var universe = Vars(2);
        var empty = new List<string>();

        // SAT formula: empty projection has exactly the empty tuple as its single model.
        var sat = RandomExpressions.Parse("x0 | x1");
        Assert.Equal(BigInteger.One, ProjectedModelCounting.BruteForceProjected(sat, universe, empty));
        var satBlock = ProjectedModelCounting.SatBlockingEnumeration(sat, universe, empty);
        var satBdd = ProjectedModelCounting.BddExistentialAbstraction(sat, universe, empty);
        Assert.Equal(ProjectedCountStatus.Exact, satBlock.Status);
        Assert.Equal(BigInteger.One, satBlock.Count);
        Assert.Equal(BigInteger.One, satBdd.Count);

        // UNSAT formula: no models, so the empty projection is empty too.
        var unsat = new AndNode(new VariableNode("x0"), new NotNode(new VariableNode("x0")));
        Assert.Equal(BigInteger.Zero, ProjectedModelCounting.BruteForceProjected(unsat, universe, empty));
        var unsatBlock = ProjectedModelCounting.SatBlockingEnumeration(unsat, universe, empty);
        var unsatBdd = ProjectedModelCounting.BddExistentialAbstraction(unsat, universe, empty);
        Assert.Equal(ProjectedCountStatus.Exact, unsatBlock.Status);
        Assert.Equal(BigInteger.Zero, unsatBlock.Count);
        Assert.Equal(BigInteger.Zero, unsatBdd.Count);
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

            var sat = ProjectedModelCounting.SatBlockingEnumeration(ast, universe, universe);
            var bdd = ProjectedModelCounting.BddExistentialAbstraction(ast, universe, universe);
            Assert.Equal(ProjectedCountStatus.Exact, sat.Status);
            Assert.Equal(countModels, sat.Count);
            Assert.Equal(countModels, bdd.Count);
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
        AssertBothAgree(formula, universe, new List<string> { "x0" }, BigInteger.One);

        // Project onto {x0, x1}: x2 is forgotten. Models with x0=true: (x1,x2) free -> projected
        // {x0=1,x1=0},{x0=1,x1=1} => 2 distinct. x0=false has no models. Expect 2, not 4.
        AssertBothAgree(formula, universe, new List<string> { "x0", "x1" }, new BigInteger(2));
    }

    [Fact]
    public void OvercountTrap_WorkedExample_FromDesignDoc()
    {
        // f = (x1 & y) | (x2 & !y), project the decision variable y OUT onto {x1, x2}.
        //   y=1 branch models projected onto {x1,x2}: (1,0), (1,1)
        //   y=0 branch models projected onto {x1,x2}: (0,1), (1,1)
        // Union = {(1,0),(1,1),(0,1)} => 3 distinct. Naive OR-branch summation gives 2+2 = 4,
        // double-counting (1,1). Both prototypes must report 3.
        var formula = new OrNode(
            new AndNode(new VariableNode("x1"), new VariableNode("y")),
            new AndNode(new VariableNode("x2"), new NotNode(new VariableNode("y"))));
        var universe = new List<string> { "x1", "x2", "y" };

        AssertBothAgree(formula, universe, new List<string> { "x1", "x2" }, new BigInteger(3));
    }

    // -----------------------------------------------------------------------------------------
    //  Status contract: a budget-limited run never passes off a partial count as exact.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void SatBlockingEnumeration_ModelBudget_ReportsPartialNeverExact()
    {
        // OR of 6 variables projected onto all 6 has 63 distinct projected models. With a model
        // budget of 10 the enumerator must stop and report BudgetExhausted with NO count, not a
        // wrong "exact 10".
        var universe = Vars(6);
        AstNode formula = new OrNode(universe.Select(v => (AstNode)new VariableNode(v)).ToList());
        var projection = universe;

        var limited = ProjectedModelCounting.SatBlockingEnumeration(
            formula, universe, projection, maxModels: 10);
        Assert.Equal(ProjectedCountStatus.BudgetExhausted, limited.Status);
        Assert.Null(limited.Count);

        // With a generous budget the same query is exact: 2^6 - ... = 63 models of (OR of 6),
        // projected onto all 6 variables is just #SAT = 63.
        var full = ProjectedModelCounting.SatBlockingEnumeration(formula, universe, projection);
        Assert.Equal(ProjectedCountStatus.Exact, full.Status);
        Assert.Equal(new BigInteger(63), full.Count);
    }

    [Fact]
    public void SatBlockingEnumeration_ConflictBudget_ReportsUnknownNeverExact()
    {
        // A zero conflict budget forces an Unknown verdict on the very first solve of a
        // non-trivial instance; the result must withhold the count.
        var universe = Vars(4);
        var expression = RandomExpressions.Generate(new Random(1), 4, 4);
        var ast = RandomExpressions.Parse(expression);
        var vars = ast.GetVariables().OrderBy(v => v).ToList();
        if (vars.Count == 0) return;

        var result = ProjectedModelCounting.SatBlockingEnumeration(
            ast, vars, vars, maxConflicts: 0);
        // Either it solved with zero conflicts (trivial instance) -> Exact, or it hit the budget
        // -> Unknown. In neither case may a partial be dressed up as exact.
        if (result.Status != ProjectedCountStatus.Exact)
        {
            Assert.Equal(ProjectedCountStatus.Unknown, result.Status);
            Assert.Null(result.Count);
        }
    }
}
