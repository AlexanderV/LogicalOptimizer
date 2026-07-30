using Microsoft.Z3;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Heavy differential sweep for MaxSAT: many seeded random weighted partial instances are
///     solved by both algorithms and cross-checked against Z3's <c>Optimize</c> engine AND against
///     brute-force enumeration. Tagged <c>Exhaustive</c> so the fast gate excludes it; degrades
///     gracefully when the native Z3 library cannot be loaded (the brute-force half still runs).
/// </summary>
[Trait("Category", "Exhaustive")]
public class CoreGuidedMaxSatExhaustiveTests
{
    private static bool? _z3Available;

    private static bool Z3Available()
    {
        if (_z3Available.HasValue) return _z3Available.Value;
        try
        {
            using var probe = new Context();
            _ = probe.MkTrue();
            _z3Available = true;
        }
        catch (Exception)
        {
            _z3Available = false;
        }

        return _z3Available.Value;
    }

    private static int[] RandomClause(Random random, int variableCount)
    {
        var clause = new int[random.Next(1, 4)];
        for (var k = 0; k < clause.Length; k++)
        {
            var variable = random.Next(1, variableCount + 1);
            clause[k] = random.Next(2) == 0 ? variable : -variable;
        }

        return clause;
    }

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

    private static long Z3Optimum(Context ctx, int variableCount, List<int[]> hard,
        List<(int Weight, int[] Clause)> soft, out bool hardFeasible)
    {
        var vars = Enumerable.Range(1, variableCount).Select(i => ctx.MkBoolConst($"x{i}")).ToArray();

        BoolExpr ToClause(int[] clause) => ctx.MkOr(clause
            .Select(l => l > 0 ? vars[l - 1] : ctx.MkNot(vars[-l - 1])).ToArray());

        var opt = ctx.MkOptimize();
        foreach (var clause in hard) opt.Assert(ToClause(clause));
        foreach (var (weight, clause) in soft) opt.AssertSoft(ToClause(clause), (uint)weight, "s");

        if (opt.Check() != Status.SATISFIABLE)
        {
            hardFeasible = false;
            return 0;
        }

        hardFeasible = true;
        // The optimum cost is the total weight of soft clauses the optimal model falsifies.
        var model = opt.Model;
        var cost = 0L;
        foreach (var (weight, clause) in soft)
            if (!model.Eval(ToClause(clause), true).IsTrue)
                cost += weight;
        return cost;
    }

    [Fact]
    public void CoreGuidedAndLinear_MatchZ3OptimizeAndBruteForce()
    {
        var runZ3 = Z3Available();
        var random = new Random(2026_0729);
        Context? ctx = runZ3 ? new Context() : null;

        var checkedInstances = 0;
        var z3Confirmed = 0;
        try
        {
            for (var instance = 0; instance < 300; instance++)
            {
                var variableCount = random.Next(2, 7);
                var hards = new List<int[]>();
                var softs = new List<(int Weight, int[] Clause)>();
                for (var c = 0; c < random.Next(0, 4); c++) hards.Add(RandomClause(random, variableCount));
                for (var c = 0; c < random.Next(1, 8); c++)
                    softs.Add((random.Next(1, 6), RandomClause(random, variableCount)));

                var expected = BruteForceOptimum(variableCount, hards, softs);

                var core = Build(variableCount, hards, softs).Solve(MaxSatAlgorithm.CoreGuided);
                var linear = Build(variableCount, hards, softs).Solve(MaxSatAlgorithm.Linear);

                if (expected == null)
                {
                    Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, core.Status);
                    Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, linear.Status);
                }
                else
                {
                    Assert.Equal(MaxSatStatus.Optimal, core.Status);
                    Assert.Equal(MaxSatStatus.Optimal, linear.Status);
                    Assert.True(expected == core.Cost,
                        $"Instance {instance}: brute force {expected}, core-guided {core.Cost}");
                    Assert.True(expected == linear.Cost,
                        $"Instance {instance}: brute force {expected}, linear {linear.Cost}");
                }

                if (ctx != null)
                {
                    var z3 = Z3Optimum(ctx, variableCount, hards, softs, out var hardFeasible);
                    if (!hardFeasible)
                    {
                        Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, core.Status);
                    }
                    else
                    {
                        Assert.True(expected == z3,
                            $"Instance {instance}: brute force {expected}, Z3 {z3}");
                        Assert.True(core.Cost == z3,
                            $"Instance {instance}: core-guided {core.Cost}, Z3 {z3}");
                        Assert.True(linear.Cost == z3,
                            $"Instance {instance}: linear {linear.Cost}, Z3 {z3}");
                    }

                    z3Confirmed++;
                }

                checkedInstances++;
            }
        }
        finally
        {
            ctx?.Dispose();
        }

        Assert.True(checkedInstances == 300, $"Expected 300 instances, checked {checkedInstances}");
        if (runZ3) Assert.True(z3Confirmed > 0, "Z3 was reported available but no instance was cross-checked");
    }

    private static MaxSatSolver Build(int variableCount, List<int[]> hards,
        List<(int Weight, int[] Clause)> softs)
    {
        var solver = new MaxSatSolver(variableCount);
        foreach (var clause in hards) solver.AddHard(clause);
        foreach (var (weight, clause) in softs) solver.AddSoft(weight, clause);
        return solver;
    }
}
