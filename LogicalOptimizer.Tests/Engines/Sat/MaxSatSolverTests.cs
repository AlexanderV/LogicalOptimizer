using Xunit;

namespace LogicalOptimizer.Tests;


public class MaxSatSolverTests
{
    [Fact]
    public void Solve_ConflictingSoftUnits_KeepsHeavier()
    {
        var solver = new MaxSatSolver(1);
        solver.AddSoft(3, 1);
        solver.AddSoft(1, -1);

        var result = solver.Solve();

        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(1, result.Cost);
        Assert.True(result.GetValue(1));
    }

    [Fact]
    public void Solve_HardConstraintsRestrictSofts()
    {
        // Hard: a | b. Soft: !a (2), !b (1). Optimum: a=0, b=1 with cost 1.
        var solver = new MaxSatSolver(2);
        solver.AddHard(1, 2);
        solver.AddSoft(2, -1);
        solver.AddSoft(1, -2);

        var result = solver.Solve();

        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(1, result.Cost);
        Assert.False(result.GetValue(1));
        Assert.True(result.GetValue(2));
    }

    [Fact]
    public void Solve_UnsatisfiableHards_Reported()
    {
        var solver = new MaxSatSolver(1);
        solver.AddHard(1);
        solver.AddHard(-1);
        solver.AddSoft(5, 1);

        Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, solver.Solve().Status);
    }

    [Fact]
    public void Solve_AllSoftsSatisfiable_ZeroCost()
    {
        var solver = new MaxSatSolver(2);
        solver.AddSoft(1, 1);
        solver.AddSoft(1, -1, 2);

        var result = solver.Solve();

        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(0, result.Cost);
    }

    [Fact]
    public void Solve_RandomWeightedInstances_MatchBruteForce()
    {
        var random = new Random(9999);
        for (var instance = 0; instance < 30; instance++)
        {
            var variableCount = random.Next(2, 6);
            var solver = new MaxSatSolver(variableCount);
            var hards = new List<int[]>();
            var softs = new List<(int Weight, int[] Clause)>();

            for (var c = 0; c < random.Next(0, 3); c++)
            {
                var clause = RandomClause(random, variableCount);
                hards.Add(clause);
                solver.AddHard(clause);
            }

            for (var c = 0; c < random.Next(1, 6); c++)
            {
                var clause = RandomClause(random, variableCount);
                var weight = random.Next(1, 6);
                softs.Add((weight, clause));
                solver.AddSoft(weight, clause);
            }

            var result = solver.Solve();

            // Brute force optimum
            long? bestCost = null;
            for (var mask = 0; mask < 1 << variableCount; mask++)
            {
                var m = mask;
                bool Holds(int[] clause) => clause.Any(l => ((m >> (Math.Abs(l) - 1)) & 1) == 1 == l > 0);
                if (!hards.All(Holds)) continue;
                var cost = softs.Where(s => !Holds(s.Clause)).Sum(s => (long)s.Weight);
                if (bestCost == null || cost < bestCost) bestCost = cost;
            }

            if (bestCost == null)
            {
                Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, result.Status);
            }
            else
            {
                Assert.Equal(MaxSatStatus.Optimal, result.Status);
                Assert.True(bestCost == result.Cost,
                    $"Instance {instance}: expected {bestCost}, got {result.Cost}");
            }
        }
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
}
