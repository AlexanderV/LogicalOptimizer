using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Correctness gate for the core-guided MaxSAT algorithm (P2.2). Every proven optimum is
///     cross-checked against brute-force enumeration and against the existing linear search, both
///     unweighted and weighted; the three-valued completion status (proven Optimal vs. a sound
///     incumbent vs. hard-UNSAT) is exercised explicitly, and the linear search is confirmed
///     unchanged.
/// </summary>
public class CoreGuidedMaxSatTests
{
    /// <summary>Brute-force optimum over all 2^n assignments (null when the hard clauses are UNSAT).</summary>
    private static long? BruteForceOptimum(int variableCount, List<int[]> hard,
        List<(int Weight, int[] Clause)> soft)
    {
        long? best = null;
        for (var mask = 0; mask < 1 << variableCount; mask++)
        {
            var m = mask;

            bool Holds(int[] clause) => clause.Any(l => ((m >> (Math.Abs(l) - 1)) & 1) == 1 == l > 0);

            if (!hard.All(Holds)) continue;
            var cost = soft.Where(s => !Holds(s.Clause)).Sum(s => (long)s.Weight);
            if (best == null || cost < best) best = cost;
        }

        return best;
    }

    private static int[] RandomClause(Random random, int variableCount)
    {
        var clause = new int[random.Next(1, 3)];
        for (var k = 0; k < clause.Length; k++)
        {
            var variable = random.Next(1, variableCount + 1);
            clause[k] = random.Next(2) == 0 ? variable : -variable;
        }

        return clause;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreGuided_RandomInstances_MatchBruteForceAndLinear(bool weighted)
    {
        var random = new Random(weighted ? 55_512 : 4242);
        for (var instance = 0; instance < 80; instance++)
        {
            var variableCount = random.Next(2, 6);
            var hards = new List<int[]>();
            var softs = new List<(int Weight, int[] Clause)>();

            for (var c = 0; c < random.Next(0, 3); c++)
                hards.Add(RandomClause(random, variableCount));
            for (var c = 0; c < random.Next(1, 7); c++)
            {
                var weight = weighted ? random.Next(1, 6) : 1;
                softs.Add((weight, RandomClause(random, variableCount)));
            }

            var expected = BruteForceOptimum(variableCount, hards, softs);

            var coreGuided = Build(variableCount, hards, softs).Solve(MaxSatAlgorithm.CoreGuided);
            var linear = Build(variableCount, hards, softs).Solve(MaxSatAlgorithm.Linear);

            if (expected == null)
            {
                Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, coreGuided.Status);
                Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, linear.Status);
                continue;
            }

            Assert.Equal(MaxSatStatus.Optimal, coreGuided.Status);
            Assert.Equal(MaxSatStatus.Optimal, linear.Status);
            Assert.True(expected == coreGuided.Cost,
                $"Instance {instance} (weighted={weighted}): brute force {expected}, core-guided {coreGuided.Cost}");
            Assert.True(expected == linear.Cost,
                $"Instance {instance} (weighted={weighted}): brute force {expected}, linear {linear.Cost}");

            // A proven optimum reports coincident bounds.
            Assert.Equal(coreGuided.Cost, coreGuided.LowerBound);
            Assert.Equal(coreGuided.Cost, coreGuided.UpperBound);

            // The reported model actually realizes the optimum cost.
            AssertModelCost(coreGuided, variableCount, hards, softs, expected.Value);
        }
    }

    private static MaxSatSolver Build(int variableCount, List<int[]> hards,
        List<(int Weight, int[] Clause)> softs)
    {
        var solver = new MaxSatSolver(variableCount);
        foreach (var clause in hards) solver.AddHard(clause);
        foreach (var (weight, clause) in softs) solver.AddSoft(weight, clause);
        return solver;
    }

    private static void AssertModelCost(MaxSatResult result, int variableCount, List<int[]> hards,
        List<(int Weight, int[] Clause)> softs, long expected)
    {
        Assert.NotNull(result.Values);

        bool Holds(int[] clause) => clause.Any(l => result.GetValue(Math.Abs(l)) == l > 0);

        Assert.All(hards, h => Assert.True(Holds(h), "The returned model violates a hard clause"));
        var cost = softs.Where(s => !Holds(s.Clause)).Sum(s => (long)s.Weight);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void CoreGuided_ConflictingSoftUnits_KeepsHeavier()
    {
        var solver = new MaxSatSolver(1);
        solver.AddSoft(3, 1);
        solver.AddSoft(1, -1);

        var result = solver.Solve(MaxSatAlgorithm.CoreGuided);

        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(1, result.Cost);
        Assert.True(result.GetValue(1));
    }

    [Fact]
    public void CoreGuided_HardConstraintsRestrictSofts()
    {
        // Hard: a | b. Soft: !a (2), !b (1). Optimum: a=0, b=1 with cost 1.
        var solver = new MaxSatSolver(2);
        solver.AddHard(1, 2);
        solver.AddSoft(2, -1);
        solver.AddSoft(1, -2);

        var result = solver.Solve(MaxSatAlgorithm.CoreGuided);

        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(1, result.Cost);
        Assert.False(result.GetValue(1));
        Assert.True(result.GetValue(2));
    }

    [Fact]
    public void CoreGuided_AllSoftsSatisfiable_ZeroCost()
    {
        var solver = new MaxSatSolver(2);
        solver.AddSoft(1, 1);
        solver.AddSoft(1, -1, 2);

        var result = solver.Solve(MaxSatAlgorithm.CoreGuided);

        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(0, result.Cost);
        Assert.Equal(0, result.LowerBound);
        Assert.Equal(0, result.UpperBound);
    }

    [Fact]
    public void CoreGuided_UnsatisfiableHards_ReportedDistinctlyFromBudget()
    {
        var solver = new MaxSatSolver(1);
        solver.AddHard(1);
        solver.AddHard(-1);
        solver.AddSoft(5, 1);

        var result = solver.Solve(MaxSatAlgorithm.CoreGuided);

        Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, result.Status);
        Assert.Null(result.Values);
        // Hard-UNSAT is NOT budget exhaustion.
        Assert.NotEqual(MaxSatStatus.Unknown, result.Status);
    }

    [Fact]
    public void CoreGuided_TinyBudgetOnHardInstance_ReturnsSoundIncumbentNeverOptimal()
    {
        // Pigeonhole PHP(8 pigeons, 7 holes): "at most one pigeon per hole" is hard (trivially
        // SAT — place nobody), but proving that not all pigeons fit (optimum ≥ 1) requires
        // refuting pigeonhole, which is intractable within a tiny conflict budget.
        var (solver, _, _) = BuildPigeonhole(pigeons: 8, holes: 7);

        var result = solver.Solve(MaxSatAlgorithm.CoreGuided, maxConflictsPerCall: 40);

        Assert.Equal(MaxSatStatus.Unknown, result.Status);
        Assert.NotEqual(MaxSatStatus.Optimal, result.Status);
        Assert.NotNull(result.Values); // a sound incumbent is available
        Assert.True(result.LowerBound <= result.UpperBound,
            $"bounds must bracket the optimum: [{result.LowerBound}, {result.UpperBound}]");
        Assert.Equal(result.Cost, result.UpperBound);
    }

    [Fact]
    public void CoreGuided_Pigeonhole_ProvesSameOptimumAsLinear()
    {
        // A regression instance where the two algorithms take visibly different paths — the
        // linear search improves models from above, the core-guided search raises a proven
        // lower bound from below — yet reach the SAME proven optimum (exactly one pigeon left out).
        var (coreSolver, _, _) = BuildPigeonhole(pigeons: 5, holes: 4);
        var (linearSolver, _, _) = BuildPigeonhole(pigeons: 5, holes: 4);

        var core = coreSolver.Solve(MaxSatAlgorithm.CoreGuided);
        var linear = linearSolver.Solve(MaxSatAlgorithm.Linear);

        Assert.Equal(MaxSatStatus.Optimal, core.Status);
        Assert.Equal(MaxSatStatus.Optimal, linear.Status);
        Assert.Equal(1, core.Cost);
        Assert.Equal(linear.Cost, core.Cost);
    }

    [Fact]
    public void AutoAndDefault_MatchLinear()
    {
        var random = new Random(31_337);
        for (var instance = 0; instance < 40; instance++)
        {
            var variableCount = random.Next(2, 6);
            var hards = new List<int[]>();
            var softs = new List<(int Weight, int[] Clause)>();
            for (var c = 0; c < random.Next(0, 3); c++) hards.Add(RandomClause(random, variableCount));
            for (var c = 0; c < random.Next(1, 6); c++)
                softs.Add((random.Next(1, 5), RandomClause(random, variableCount)));

            var def = Build(variableCount, hards, softs).Solve();
            var auto = Build(variableCount, hards, softs).Solve(MaxSatAlgorithm.Auto);
            var linear = Build(variableCount, hards, softs).Solve(MaxSatAlgorithm.Linear);

            // The default and Auto both route to the linear search: identical status and cost.
            Assert.Equal(linear.Status, def.Status);
            Assert.Equal(linear.Cost, def.Cost);
            Assert.Equal(linear.Status, auto.Status);
            Assert.Equal(linear.Cost, auto.Cost);
        }
    }

    /// <summary>PHP: p_{i,h} = pigeon i in hole h; hard = at most one pigeon per hole; soft = each pigeon placed.</summary>
    private static (MaxSatSolver Solver, List<int[]> Hard, List<(int Weight, int[] Clause)> Soft)
        BuildPigeonhole(int pigeons, int holes)
    {
        int Var(int pigeon, int hole) => (pigeon - 1) * holes + hole; // 1-based

        var variableCount = pigeons * holes;
        var solver = new MaxSatSolver(variableCount);
        var hard = new List<int[]>();
        var soft = new List<(int Weight, int[] Clause)>();

        for (var h = 1; h <= holes; h++)
        for (var i = 1; i <= pigeons; i++)
        for (var j = i + 1; j <= pigeons; j++)
        {
            var clause = new[] { -Var(i, h), -Var(j, h) };
            hard.Add(clause);
            solver.AddHard(clause);
        }

        for (var i = 1; i <= pigeons; i++)
        {
            var clause = Enumerable.Range(1, holes).Select(h => Var(i, h)).ToArray();
            soft.Add((1, clause));
            solver.AddSoft(1, clause);
        }

        return (solver, hard, soft);
    }
}
