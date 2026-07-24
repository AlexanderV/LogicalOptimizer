using Xunit;

namespace LogicalOptimizer.Tests;

public class IncrementalSatTests
{
    [Fact]
    public void Solve_AssumptionsRestrictModels()
    {
        var solver = new SatSolver(2);
        solver.AddClause(1, 2); // a | b

        Assert.Equal(SatResult.Satisfiable, solver.Solve(new[] { -1 }));
        Assert.False(solver.GetValue(1));
        Assert.True(solver.GetValue(2));

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { -1, -2 }));
        Assert.NotNull(solver.UnsatCore);
        Assert.Superset(new HashSet<int>(solver.UnsatCore!), new HashSet<int> { -1, -2 });
    }

    [Fact]
    public void UnsatCore_ExcludesIrrelevantAssumptions()
    {
        // Only assumptions 1 and 3 clash (via !1 | !3); assumption 2 must not be blamed
        var solver = new SatSolver(3);
        solver.AddClause(-1, -3);

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { 1, 2, 3 }));

        var core = new HashSet<int>(solver.UnsatCore!);
        Assert.Contains(1, core);
        Assert.Contains(3, core);
        Assert.DoesNotContain(2, core);
    }

    [Fact]
    public void UnsatCore_ContradictoryAssumptions()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1, -1); // tautology: formula itself is trivial

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { 1, -1 }));
        Assert.Equal(new HashSet<int> { 1, -1 }, new HashSet<int>(solver.UnsatCore!));
    }

    [Fact]
    public void UnsatCore_EmptyWhenFormulaItselfUnsat()
    {
        var solver = new SatSolver(2);
        solver.AddClause(1);
        solver.AddClause(-1);

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { 2 }));
        Assert.Empty(solver.UnsatCore!);
    }

    [Fact]
    public void AddClause_AfterSolve_IsRespected()
    {
        var solver = new SatSolver(2);
        solver.AddClause(1, 2);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());

        solver.AddClause(-1);
        solver.AddClause(-2);
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_RepeatedCallsWithDifferentAssumptions_ConsistentVerdicts()
    {
        // Chain 1 -> 2 -> 3 -> 4; querying each variable's forced polarity via assumptions
        var solver = new SatSolver(4);
        solver.AddClause(-1, 2);
        solver.AddClause(-2, 3);
        solver.AddClause(-3, 4);

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { 1, -4 }));
        Assert.Equal(SatResult.Satisfiable, solver.Solve(new[] { 1, 4 }));
        Assert.Equal(SatResult.Satisfiable, solver.Solve(new[] { -4 }));
        Assert.False(solver.GetValue(1)); // !4 propagates back to !1
        Assert.Equal(SatResult.Satisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_AssumptionsWithRandomInstances_MatchConditionedBruteForce()
    {
        var random = new Random(555);
        for (var instance = 0; instance < 60; instance++)
        {
            var variableCount = random.Next(3, 9);
            var clauses = new List<int[]>();
            for (var c = 0; c < variableCount * 3; c++)
            {
                var clause = new int[random.Next(1, 4)];
                for (var k = 0; k < clause.Length; k++)
                {
                    var variable = random.Next(1, variableCount + 1);
                    clause[k] = random.Next(2) == 0 ? variable : -variable;
                }

                clauses.Add(clause);
            }

            var assumptionVariable = random.Next(1, variableCount + 1);
            var assumption = random.Next(2) == 0 ? assumptionVariable : -assumptionVariable;

            var solver = new SatSolver(variableCount);
            foreach (var clause in clauses) solver.AddClause(clause);
            var verdict = solver.Solve(new[] { assumption });

            // Brute force with the assumption folded in as a unit clause
            var conditioned = new List<int[]>(clauses) { new[] { assumption } };
            var expected = SatTestOracles.BruteForceSatisfiable(variableCount, conditioned);

            Assert.True(verdict == (expected ? SatResult.Satisfiable : SatResult.Unsatisfiable),
                $"Instance {instance}: assumption {assumption}, solver={verdict}, bruteforce={expected}");
        }
    }
}
