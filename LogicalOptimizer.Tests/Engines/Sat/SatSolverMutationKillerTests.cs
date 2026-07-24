using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Mutation-testing killers for SatSolver survivors that represent REAL coverage
///     gaps (argument validation, the growth-gated subsumption preprocessing that
///     ordinary tests almost never trigger, and the semantic contract of unsat cores).
///     Survivors NOT addressed here are equivalent mutants: VSIDS activity/heap
///     ordering, restart scheduling, learnt-DB reduction triggers and phase saving
///     change only the search path, never the verdict.
/// </summary>
public class SatSolverMutationKillerTests
{
    [Fact]
    public void Constructor_ZeroVariables_IsValidAndTriviallySatisfiable()
    {
        // variableCount < 0 is the contract; a '<= 0' mutant breaks the empty solver
        var solver = new SatSolver(0);
        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SatSolver(-1));
    }

    [Fact]
    public void AddClause_OutOfRangeLiterals_ThrowOnEitherSide()
    {
        // The range check is variable < 1 OR variable > count; an '&&' mutant lets
        // both invalid sides through
        var solver = new SatSolver(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.AddClause(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.AddClause(-4));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.AddClause(1, 0));
    }

    [Fact]
    public void ForcedPreprocessing_RandomInstances_VerdictsStayCorrect()
    {
        // Subsumption/self-subsumption runs only after clause growth trips the gate
        // (2x + 16), which small tests never do — a broken subsumption check could
        // silently delete load-bearing clauses and flip verdicts
        var rng = new Random(70707);
        for (var trial = 0; trial < 40; trial++)
        {
            var variableCount = rng.Next(3, 8);
            var solver = new SatSolver(variableCount);
            var clauses = new List<int[]>();

            void AddRandomClauses(int count)
            {
                for (var c = 0; c < count; c++)
                {
                    var width = rng.Next(1, Math.Min(3, 2 * variableCount) + 1);
                    var clause = new HashSet<int>();
                    while (clause.Count < width)
                    {
                        var v = rng.Next(1, variableCount + 1);
                        clause.Add(rng.Next(2) == 0 ? v : -v);
                    }

                    clauses.Add(clause.ToArray());
                    solver.AddClause(clause.ToArray());
                }
            }

            // First batch + solve (subsumption runs), then a growth burst well past
            // the 2x+16 gate and a second solve (subsumption runs AGAIN on the
            // grown database, including learnt clauses)
            AddRandomClauses(10);
            var first = solver.Solve();
            Assert.Equal(
                SatTestOracles.BruteForceSatisfiable(variableCount, clauses.Take(10).ToList())
                    ? SatResult.Satisfiable
                    : SatResult.Unsatisfiable, first);

            AddRandomClauses(60);
            var second = solver.Solve();
            Assert.Equal(
                SatTestOracles.BruteForceSatisfiable(variableCount, clauses)
                    ? SatResult.Satisfiable
                    : SatResult.Unsatisfiable, second);
        }
    }

    [Fact]
    public void UnsatCore_ResolvingWithOnlyCoreAssumptions_StaysUnsat()
    {
        // Semantic contract of the core: it must itself be sufficient for UNSAT.
        // AnalyzeFinal mutants that leave junk in (or drop literals from) the core
        // break this on random instances
        var rng = new Random(80808);
        var checkedCores = 0;
        for (var trial = 0; trial < 60; trial++)
        {
            var variableCount = rng.Next(3, 7);
            var clauses = new List<int[]>();
            var solver = new SatSolver(variableCount);
            for (var c = 0; c < rng.Next(4, 12); c++)
            {
                var width = rng.Next(1, 3);
                var clause = new HashSet<int>();
                while (clause.Count < width)
                {
                    var v = rng.Next(1, variableCount + 1);
                    clause.Add(rng.Next(2) == 0 ? v : -v);
                }

                clauses.Add(clause.ToArray());
                solver.AddClause(clause.ToArray());
            }

            var assumptions = Enumerable.Range(1, variableCount)
                .Select(v => rng.Next(2) == 0 ? v : -v)
                .ToArray();

            if (solver.Solve(assumptions) != SatResult.Unsatisfiable) continue;
            var core = solver.UnsatCore;
            if (core == null || core.Count == 0) continue; // hard UNSAT — no assumption core

            // Every core literal must be one of the assumptions
            Assert.True(core.All(assumptions.Contains),
                $"Trial {trial}: core contains a non-assumption literal");

            var replay = new SatSolver(variableCount);
            foreach (var clause in clauses) replay.AddClause(clause);
            Assert.Equal(SatResult.Unsatisfiable, replay.Solve(core.ToArray()));
            checkedCores++;
        }

        Assert.True(checkedCores >= 5, $"Only {checkedCores} cores exercised — generator too weak");
    }

    [Fact]
    public void ConflictBudget_ZeroBudget_ReturnsUnknownNotWrongVerdict()
    {
        // A hard instance with no conflict budget must answer Unknown, never guess
        var rng = new Random(90909);
        var solver = new SatSolver(30);
        for (var c = 0; c < 128; c++)
        {
            var clause = new HashSet<int>();
            while (clause.Count < 3)
            {
                var v = rng.Next(1, 31);
                clause.Add(rng.Next(2) == 0 ? v : -v);
            }

            solver.AddClause(clause.ToArray());
        }

        Assert.Equal(SatResult.Unknown, solver.Solve(maxConflicts: 0));
    }
}
