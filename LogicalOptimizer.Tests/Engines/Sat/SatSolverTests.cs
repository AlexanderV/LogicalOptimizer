using Xunit;

namespace LogicalOptimizer.Tests;

public class SatSolverTests
{
    [Fact]
    public void Solve_EmptyProblem_Satisfiable()
    {
        Assert.Equal(SatResult.Satisfiable, new SatSolver(0).Solve());
    }

    [Fact]
    public void Solve_SingleUnit_SatisfiableWithCorrectModel()
    {
        var solver = new SatSolver(1);
        solver.AddClause(-1);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        Assert.False(solver.GetValue(1));
    }

    [Fact]
    public void Solve_ContradictoryUnits_Unsatisfiable()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1);
        solver.AddClause(-1);

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_EmptyClause_Unsatisfiable()
    {
        var solver = new SatSolver(2);
        solver.AddClause(Array.Empty<int>());

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_TautologicalClause_Ignored()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1, -1);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_ChainedImplications_PropagatesToModel()
    {
        // 1 → 2 → 3 → 4, with unit 1: all must be true
        var solver = new SatSolver(4);
        solver.AddClause(1);
        solver.AddClause(-1, 2);
        solver.AddClause(-2, 3);
        solver.AddClause(-3, 4);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        for (var v = 1; v <= 4; v++) Assert.True(solver.GetValue(v));
    }

    [Fact]
    public void Solve_PigeonholeThreeIntoTwo_Unsatisfiable()
    {
        // Variables p_{i,j}: pigeon i (1..3) in hole j (1..2); var index = 2*(i-1)+j
        var solver = new SatSolver(6);
        for (var pigeon = 0; pigeon < 3; pigeon++)
            solver.AddClause(2 * pigeon + 1, 2 * pigeon + 2);
        for (var hole = 1; hole <= 2; hole++)
            for (var a = 0; a < 3; a++)
                for (var b = a + 1; b < 3; b++)
                    solver.AddClause(-(2 * a + hole), -(2 * b + hole));

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_RandomThreeSat_MatchesBruteForce()
    {
        var random = new Random(20260724);
        for (var instance = 0; instance < 150; instance++)
        {
            var variableCount = random.Next(3, 11);
            var clauseCount = random.Next(2, variableCount * 5);
            var clauses = new List<int[]>();
            for (var c = 0; c < clauseCount; c++)
            {
                var length = random.Next(1, 4);
                var clause = new int[length];
                for (var k = 0; k < length; k++)
                {
                    var variable = random.Next(1, variableCount + 1);
                    clause[k] = random.Next(2) == 0 ? variable : -variable;
                }

                clauses.Add(clause);
            }

            var solver = new SatSolver(variableCount);
            foreach (var clause in clauses) solver.AddClause(clause);
            var verdict = solver.Solve();

            var expected = SatTestOracles.BruteForceSatisfiable(variableCount, clauses);
            Assert.True(verdict == (expected ? SatResult.Satisfiable : SatResult.Unsatisfiable),
                $"Instance {instance}: solver={verdict}, bruteforce={expected}");

            if (verdict == SatResult.Satisfiable)
                foreach (var clause in clauses)
                    Assert.True(clause.Any(l => solver.GetValue(Math.Abs(l)) == l > 0),
                        $"Instance {instance}: model does not satisfy clause [{string.Join(",", clause)}]");
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Solve_PhaseTransitionInstances_ResolveWithinBudget()
    {
        // Random 3-SAT at clause ratio ~4.2 (near the hardness peak): exercises the heap,
        // Luby restarts and learnt-database reduction at a scale the small tests never reach
        var random = new Random(60606);
        for (var instance = 0; instance < 5; instance++)
        {
            const int variableCount = 60;
            var solver = new SatSolver(variableCount);
            var clauses = new List<int[]>();
            for (var c = 0; c < 252; c++)
            {
                var vars = new HashSet<int>();
                while (vars.Count < 3) vars.Add(random.Next(1, variableCount + 1));
                var clause = vars.Select(v => random.Next(2) == 0 ? v : -v).ToArray();
                clauses.Add(clause);
                solver.AddClause(clause);
            }

            var verdict = solver.Solve(500_000);
            Assert.NotEqual(SatResult.Unknown, verdict);
            if (verdict == SatResult.Satisfiable)
                Assert.All(clauses, clause =>
                    Assert.Contains(clause, l => solver.GetValue(Math.Abs(l)) == l > 0));
        }
    }

    [Fact]
    public void Luby_ReluctantDoublingPrefix()
    {
        var expected = new[] { 1, 1, 2, 1, 1, 2, 4, 1, 1, 2, 1, 1, 2, 4, 8 };
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], SatSolver.Luby(i));
    }

    [Fact]
    public void FromCnf_TseitinPipeline_SolvesFormula()
    {
        var ast = SatTestOracles.Parse("(a | b) & (!a | c) & !c");
        var cnf = TseitinConverter.Convert(ast);
        var solver = SatSolver.FromCnf(cnf);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        // Model restricted to inputs must satisfy the original formula: b=1, a=0, c=0
        Assert.False(solver.GetValue(1)); // a
        Assert.True(solver.GetValue(2)); // b
        Assert.False(solver.GetValue(3)); // c
    }
}
