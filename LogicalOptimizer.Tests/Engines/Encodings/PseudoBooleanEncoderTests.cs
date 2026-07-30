using Xunit;

namespace LogicalOptimizer.Tests;


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
