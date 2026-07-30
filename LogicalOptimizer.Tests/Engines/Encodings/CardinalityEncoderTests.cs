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
