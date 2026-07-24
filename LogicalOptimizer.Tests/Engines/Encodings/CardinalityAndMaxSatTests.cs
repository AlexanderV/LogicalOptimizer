using Xunit;

namespace LogicalOptimizer.Tests;

public class CardinalityEncoderTests
{
    /// <summary>
    ///     Exhaustive semantics check: for every assignment of the original variables, the
    ///     encoded CNF (with assumptions pinning that assignment) must be satisfiable
    ///     exactly when the predicate holds.
    /// </summary>
    private static void AssertEncodesPredicate(int n, Action<CnfBuilder, List<int>> encode,
        Func<int, bool> predicateOfTrueCount)
    {
        var builder = new CnfBuilder(n);
        var literals = Enumerable.Range(1, n).ToList();
        encode(builder, literals);
        var solver = builder.ToSolver();

        for (var mask = 0; mask < 1 << n; mask++)
        {
            var assumptions = Enumerable.Range(1, n)
                .Select(v => (mask & (1 << (v - 1))) != 0 ? v : -v)
                .ToArray();
            var expected = predicateOfTrueCount(System.Numerics.BitOperations.PopCount((uint)mask));
            var verdict = solver.Solve(assumptions);

            Assert.True((verdict == SatResult.Satisfiable) == expected,
                $"n={n}, mask={mask}: expected {expected}, got {verdict}");
        }
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    public void AtMostK_ExhaustivelyCorrect(int n, int k)
    {
        AssertEncodesPredicate(n, (b, lits) => CardinalityEncoder.AtMostK(b, lits, k), count => count <= k);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(5, 5)]
    public void AtLeastK_ExhaustivelyCorrect(int n, int k)
    {
        AssertEncodesPredicate(n, (b, lits) => CardinalityEncoder.AtLeastK(b, lits, k), count => count >= k);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void ExactlyK_ExhaustivelyCorrect(int n, int k)
    {
        AssertEncodesPredicate(n, (b, lits) => CardinalityEncoder.ExactlyK(b, lits, k), count => count == k);
    }

    [Fact]
    public void AtMostK_NegatedLiterals_CountFalseVariables()
    {
        // At most 1 of {!x1,!x2,!x3}: at least two variables must be true
        AssertEncodesPredicate(3,
            (b, lits) => CardinalityEncoder.AtMostK(b, lits.Select(l => -l).ToList(), 1),
            trueCount => 3 - trueCount <= 1);
    }
}

public class PseudoBooleanEncoderTests
{
    [Fact]
    public void AtMost_RandomWeightedInstances_ExhaustivelyCorrect()
    {
        var random = new Random(1717);
        for (var instance = 0; instance < 40; instance++)
        {
            var n = random.Next(2, 7);
            var weights = Enumerable.Range(0, n).Select(_ => (long)random.Next(1, 8)).ToList();
            var bound = random.Next(0, (int)weights.Sum() + 2);

            var builder = new CnfBuilder(n);
            var literals = Enumerable.Range(1, n).ToList();
            PseudoBooleanEncoder.AtMost(builder, literals, weights, bound);
            var solver = builder.ToSolver();

            for (var mask = 0; mask < 1 << n; mask++)
            {
                var sum = 0L;
                var assumptions = new int[n];
                for (var v = 1; v <= n; v++)
                {
                    var isTrue = (mask & (1 << (v - 1))) != 0;
                    assumptions[v - 1] = isTrue ? v : -v;
                    if (isTrue) sum += weights[v - 1];
                }

                var verdict = solver.Solve(assumptions);
                Assert.True((verdict == SatResult.Satisfiable) == (sum <= bound),
                    $"Instance {instance}, mask {mask}: sum={sum}, bound={bound}, verdict={verdict}");
            }
        }
    }

    [Fact]
    public void AtLeast_MirrorsAtMost()
    {
        var builder = new CnfBuilder(3);
        PseudoBooleanEncoder.AtLeast(builder, new List<int> { 1, 2, 3 }, new List<long> { 3, 2, 2 }, 4);
        var solver = builder.ToSolver();

        // Feasible: {1,2}=5, {1,3}=5, {1,2,3}=7, {2,3}=4; infeasible: singletons and empty
        Assert.Equal(SatResult.Satisfiable, solver.Solve(new[] { 2, 3, -1 }));
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { 1, -2, -3 }));
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve(new[] { -1, -2, 3 }));
    }

    [Fact]
    public void AtMost_NonPositiveWeight_Throws()
    {
        var builder = new CnfBuilder(2);
        Assert.Throws<ArgumentException>(() =>
            PseudoBooleanEncoder.AtMost(builder, new List<int> { 1, 2 }, new List<long> { 1, 0 }, 1));
    }
}

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
