using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Correctness gate for the portfolio of cardinality / pseudo-Boolean encodings (P2.1).
///     Every selectable encoding must be EXACTLY semantically equivalent to the constraint it
///     encodes — verified assignment-by-assignment on small n — the existing parameterless
///     methods must keep their byte-identical output, and <c>Auto</c> must never be larger than
///     the stable default on the fixed calibration corpus.
/// </summary>
public class EncodingPortfolioTests
{
    private readonly ITestOutputHelper _output;

    public EncodingPortfolioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly CardinalityEncoding[] CardinalityEncodings =
    {
        CardinalityEncoding.Pairwise, CardinalityEncoding.SequentialCounter,
        CardinalityEncoding.Product, CardinalityEncoding.Totalizer, CardinalityEncoding.Auto
    };

    private static readonly PseudoBooleanEncoding[] PseudoBooleanEncodings =
    {
        PseudoBooleanEncoding.DynamicProgramming, PseudoBooleanEncoding.BinaryMerge,
        PseudoBooleanEncoding.GeneralizedTotalizer, PseudoBooleanEncoding.Auto
    };

    // --- exhaustive semantic equivalence: cardinality ----------------------

    /// <summary>
    ///     For every encoding, every 1 ≤ n ≤ 6, every 0 ≤ k ≤ n+1 and every one of the 2^n
    ///     assignments, the encoded CNF (with the assignment pinned by assumptions) is satisfiable
    ///     IFF the assignment satisfies the constraint. An encoding that disagrees on a single
    ///     assignment is a hard bug.
    /// </summary>
    [Fact]
    public void CardinalityEncodings_ExhaustivelyEquivalent_SmallN()
    {
        var checks = RunCardinalityEquivalence(1, 6);
        Assert.True(checks > 0);
        _output.WriteLine($"Cardinality exhaustive (n=1..6): {checks} assignment checks");
    }

    /// <summary>The heavier n = 7..9 tail of the same proof; excluded from the fast gate.</summary>
    [Fact]
    [Trait("Category", "Exhaustive")]
    public void CardinalityEncodings_ExhaustivelyEquivalent_LargeN()
    {
        var checks = RunCardinalityEquivalence(7, 9);
        _output.WriteLine($"Cardinality exhaustive (n=7..9): {checks} assignment checks");
    }

    private long RunCardinalityEquivalence(int minN, int maxN)
    {
        long checks = 0;
        for (var n = minN; n <= maxN; n++)
        for (var k = 0; k <= n + 1; k++)
        foreach (var encoding in CardinalityEncodings)
        {
            checks += AssertCardinality(n, k, encoding, "AM", c => c <= k);
            checks += AssertCardinality(n, k, encoding, "AL", c => c >= k);
            checks += AssertCardinality(n, k, encoding, "EX", c => c == k);
        }

        return checks;
    }

    private static long AssertCardinality(int n, int k, CardinalityEncoding encoding, string kind,
        Func<int, bool> predicate)
    {
        var builder = new CnfBuilder(n);
        var literals = Enumerable.Range(1, n).ToList();
        switch (kind)
        {
            case "AM":
                CardinalityEncoder.AtMostK(builder, literals, k, encoding);
                break;
            case "AL":
                CardinalityEncoder.AtLeastK(builder, literals, k, encoding);
                break;
            default:
                CardinalityEncoder.ExactlyK(builder, literals, k, encoding);
                break;
        }

        var solver = builder.ToSolver();
        for (var mask = 0; mask < 1 << n; mask++)
        {
            var assumptions = Enumerable.Range(1, n)
                .Select(v => (mask & (1 << (v - 1))) != 0 ? v : -v)
                .ToArray();
            var expected = predicate(BitOperations.PopCount((uint)mask));
            var verdict = solver.Solve(assumptions);
            Assert.True((verdict == SatResult.Satisfiable) == expected,
                $"{kind} n={n} k={k} encoding={encoding} mask={mask}: expected {expected}, got {verdict}");
        }

        return 1 << n;
    }

    // --- exhaustive semantic equivalence: pseudo-Boolean -------------------

    [Fact]
    public void PseudoBooleanEncodings_ExhaustivelyEquivalent_SmallInstances()
    {
        var checks = RunPseudoBooleanEquivalence(4242, 120);
        Assert.True(checks > 0);
        _output.WriteLine($"Pseudo-Boolean exhaustive (120 instances): {checks} assignment checks");
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void PseudoBooleanEncodings_ExhaustivelyEquivalent_ManyInstances()
    {
        var checks = RunPseudoBooleanEquivalence(9001, 400);
        _output.WriteLine($"Pseudo-Boolean exhaustive (400 instances): {checks} assignment checks");
    }

    private static long RunPseudoBooleanEquivalence(int seed, int instances)
    {
        var random = new Random(seed);
        long checks = 0;
        for (var instance = 0; instance < instances; instance++)
        {
            var n = random.Next(1, 8);
            var weights = Enumerable.Range(0, n).Select(_ => (long)random.Next(1, 10)).ToList();
            var total = weights.Sum();
            var bound = random.Next(-1, (int)total + 2);
            var literals = Enumerable.Range(1, n).ToList();

            foreach (var encoding in PseudoBooleanEncodings)
            {
                var atMost = new CnfBuilder(n);
                PseudoBooleanEncoder.AtMost(atMost, literals, weights, bound, encoding);
                var atMostSolver = atMost.ToSolver();

                var atLeast = new CnfBuilder(n);
                PseudoBooleanEncoder.AtLeast(atLeast, literals, weights, bound, encoding);
                var atLeastSolver = atLeast.ToSolver();

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

                    Assert.True((atMostSolver.Solve(assumptions) == SatResult.Satisfiable) == (sum <= bound),
                        $"AtMost encoding={encoding} n={n} bound={bound} mask={mask} sum={sum}");
                    Assert.True((atLeastSolver.Solve(assumptions) == SatResult.Satisfiable) == (sum >= bound),
                        $"AtLeast encoding={encoding} n={n} bound={bound} mask={mask} sum={sum}");
                    checks += 2;
                }
            }
        }

        return checks;
    }

    // --- default output is unchanged (behavior preservation) ---------------

    /// <summary>
    ///     Characterization of the shipped sequential-counter output: the parameterless
    ///     <see cref="CardinalityEncoder.AtMostK(CnfBuilder,IReadOnlyList{int},int)" /> must
    ///     produce this exact clause set. If this breaks, existing callers' CNF changed.
    /// </summary>
    [Fact]
    public void DefaultAtMostK_ProducesTheSameClauseSet()
    {
        var builder = new CnfBuilder(4);
        CardinalityEncoder.AtMostK(builder, new[] { 1, 2, 3, 4 }, 2);

        var expected = new[]
        {
            new[] { -1, 5 }, new[] { -6 }, new[] { -2, 7 }, new[] { -5, 7 }, new[] { -2, -5, 8 },
            new[] { -6, 8 }, new[] { -2, -6 }, new[] { -3, 9 }, new[] { -7, 9 }, new[] { -3, -7, 10 },
            new[] { -8, 10 }, new[] { -3, -8 }, new[] { -4, -10 }
        };

        Assert.Equal(10, builder.VariableCount);
        Assert.Equal(expected.Length, builder.Clauses.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], builder.Clauses[i]);
    }

    /// <summary>
    ///     The <see cref="CardinalityEncoding.SequentialCounter" /> and
    ///     <see cref="PseudoBooleanEncoding.DynamicProgramming" /> overloads reproduce the exact
    ///     clause set of the parameterless (default) methods on many shapes.
    /// </summary>
    [Fact]
    public void ExplicitDefaultEncodings_MatchTheParameterlessMethods()
    {
        for (var n = 1; n <= 10; n++)
        for (var k = 0; k <= n + 1; k++)
        {
            var reference = new CnfBuilder(n);
            CardinalityEncoder.AtMostK(reference, Enumerable.Range(1, n).ToList(), k);
            var explicitly = new CnfBuilder(n);
            CardinalityEncoder.AtMostK(explicitly, Enumerable.Range(1, n).ToList(), k,
                CardinalityEncoding.SequentialCounter);
            AssertSameCnf(reference, explicitly, $"AtMostK n={n} k={k}");
        }

        var random = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            var n = random.Next(1, 8);
            var weights = Enumerable.Range(0, n).Select(_ => (long)random.Next(1, 12)).ToList();
            var bound = random.Next(-1, (int)weights.Sum() + 2);
            var literals = Enumerable.Range(1, n).ToList();

            var reference = new CnfBuilder(n);
            PseudoBooleanEncoder.AtMost(reference, literals, weights, bound);
            var explicitly = new CnfBuilder(n);
            PseudoBooleanEncoder.AtMost(explicitly, literals, weights, bound,
                PseudoBooleanEncoding.DynamicProgramming);
            AssertSameCnf(reference, explicitly, $"PB AtMost n={n} bound={bound}");
        }
    }

    private static void AssertSameCnf(CnfBuilder a, CnfBuilder b, string context)
    {
        Assert.True(a.VariableCount == b.VariableCount, $"{context}: variable count differs");
        Assert.True(a.Clauses.Count == b.Clauses.Count, $"{context}: clause count differs");
        for (var i = 0; i < a.Clauses.Count; i++)
            Assert.True(a.Clauses[i].SequenceEqual(b.Clauses[i]), $"{context}: clause {i} differs");
    }

    // --- clause / auxiliary-variable counts (size trade-offs) --------------

    /// <summary>
    ///     Characterization of the size each encoding introduces on representative shapes. These
    ///     numbers document the trade-offs the Auto heuristic weighs; a change is a deliberate
    ///     encoding change, not silent drift.
    /// </summary>
    [Theory]
    // at-most-one over 10 literals
    [InlineData("AM", 10, 1, CardinalityEncoding.Pairwise, 45, 0)]
    [InlineData("AM", 10, 1, CardinalityEncoding.SequentialCounter, 26, 9)]
    [InlineData("AM", 10, 1, CardinalityEncoding.Product, 29, 7)]
    [InlineData("AM", 10, 1, CardinalityEncoding.Totalizer, 36, 18)]
    // at-most-two over 4 literals
    [InlineData("AM", 4, 2, CardinalityEncoding.Pairwise, 4, 0)]
    [InlineData("AM", 4, 2, CardinalityEncoding.SequentialCounter, 13, 6)]
    [InlineData("AM", 4, 2, CardinalityEncoding.Totalizer, 14, 7)]
    public void CardinalityEncoding_HasExpectedSize(string kind, int n, int k, CardinalityEncoding encoding,
        int expectedClauses, int expectedAux)
    {
        var builder = new CnfBuilder(n);
        var literals = Enumerable.Range(1, n).ToList();
        var stats = kind == "AM"
            ? CardinalityEncoder.AtMostK(builder, literals, k, encoding)
            : CardinalityEncoder.AtLeastK(builder, literals, k, encoding);

        Assert.Equal(expectedClauses, stats.Clauses);
        Assert.Equal(expectedAux, stats.AuxiliaryVariables);
        Assert.Equal(expectedClauses + expectedAux, stats.Cost);
        Assert.Equal(stats.Clauses, builder.Clauses.Count);
        Assert.Equal(stats.AuxiliaryVariables, builder.VariableCount - n);
    }

    [Theory]
    [InlineData(PseudoBooleanEncoding.DynamicProgramming, 50, 29)]
    [InlineData(PseudoBooleanEncoding.BinaryMerge, 84, 12)]
    [InlineData(PseudoBooleanEncoding.GeneralizedTotalizer, 55, 25)]
    public void PseudoBooleanEncoding_HasExpectedSize(PseudoBooleanEncoding encoding, int expectedClauses,
        int expectedAux)
    {
        var builder = new CnfBuilder(6);
        var stats = PseudoBooleanEncoder.AtMost(builder, new[] { 1, 2, 3, 4, 5, 6 },
            new long[] { 1, 2, 3, 4, 5, 6 }, 8, encoding);

        Assert.Equal(expectedClauses, stats.Clauses);
        Assert.Equal(expectedAux, stats.AuxiliaryVariables);
    }

    // --- Auto never worse than the default on the calibration corpus -------

    /// <summary>
    ///     On every shape of the FIXED calibration corpus (tools/encoding_corpus.txt), Auto's
    ///     total size (clauses + auxiliary variables) is ≤ the stable default's — the roadmap's
    ///     "Auto never worse than the default beyond an agreed threshold" gate. The default is
    ///     always among the encodings Auto measures, so the threshold here is 0.
    /// </summary>
    [Fact]
    public void Auto_IsNeverWorseThanTheDefault_OnCalibrationCorpus()
    {
        var corpus = ReadCorpus();
        Assert.NotEmpty(corpus);

        _output.WriteLine($"{"shape",-34}{"auto",8}{"default",10}{"delta",8}");
        var offenders = new List<string>();
        foreach (var shape in corpus)
        {
            var (auto, def) = MeasureShape(shape);
            _output.WriteLine($"{shape.Label,-34}{auto.Cost,8}{def.Cost,10}{auto.Cost - def.Cost,8}");
            if (auto.Cost > def.Cost)
                offenders.Add($"{shape.Label}: auto={auto.Cost} > default={def.Cost}");
        }

        Assert.True(offenders.Count == 0,
            $"Auto is larger than the default on: {string.Join("; ", offenders)}");
    }

    private static (EncodingStats Auto, EncodingStats Default) MeasureShape(CorpusShape shape)
    {
        var literals = Enumerable.Range(1, shape.N).ToList();
        if (shape.IsCardinality)
        {
            var defBuilder = new CnfBuilder(shape.N);
            var c0 = defBuilder.Clauses.Count;
            var v0 = defBuilder.VariableCount;
            RunCardinality(defBuilder, literals, shape.Op, shape.Bound, null);
            var def = new EncodingStats(defBuilder.Clauses.Count - c0, defBuilder.VariableCount - v0);

            var autoBuilder = new CnfBuilder(shape.N);
            var auto = RunCardinality(autoBuilder, literals, shape.Op, shape.Bound, CardinalityEncoding.Auto);
            return (auto, def);
        }
        else
        {
            var defBuilder = new CnfBuilder(shape.N);
            var c0 = defBuilder.Clauses.Count;
            var v0 = defBuilder.VariableCount;
            RunPseudoBoolean(defBuilder, literals, shape.Weights!, shape.Op, shape.Bound, null);
            var def = new EncodingStats(defBuilder.Clauses.Count - c0, defBuilder.VariableCount - v0);

            var autoBuilder = new CnfBuilder(shape.N);
            var auto = RunPseudoBoolean(autoBuilder, literals, shape.Weights!, shape.Op, shape.Bound,
                PseudoBooleanEncoding.Auto);
            return (auto, def);
        }
    }

    private static EncodingStats RunCardinality(CnfBuilder builder, IReadOnlyList<int> literals, string op,
        long k, CardinalityEncoding? encoding)
    {
        var ik = (int)k;
        if (encoding == null)
        {
            var c0 = builder.Clauses.Count;
            var v0 = builder.VariableCount;
            switch (op)
            {
                case "AM": CardinalityEncoder.AtMostK(builder, literals, ik); break;
                case "AL": CardinalityEncoder.AtLeastK(builder, literals, ik); break;
                default: CardinalityEncoder.ExactlyK(builder, literals, ik); break;
            }

            return new EncodingStats(builder.Clauses.Count - c0, builder.VariableCount - v0);
        }

        return op switch
        {
            "AM" => CardinalityEncoder.AtMostK(builder, literals, ik, encoding.Value),
            "AL" => CardinalityEncoder.AtLeastK(builder, literals, ik, encoding.Value),
            _ => CardinalityEncoder.ExactlyK(builder, literals, ik, encoding.Value)
        };
    }

    private static EncodingStats RunPseudoBoolean(CnfBuilder builder, IReadOnlyList<int> literals,
        IReadOnlyList<long> weights, string op, long bound, PseudoBooleanEncoding? encoding)
    {
        if (encoding == null)
        {
            var c0 = builder.Clauses.Count;
            var v0 = builder.VariableCount;
            if (op == "AM") PseudoBooleanEncoder.AtMost(builder, literals, weights, bound);
            else PseudoBooleanEncoder.AtLeast(builder, literals, weights, bound);
            return new EncodingStats(builder.Clauses.Count - c0, builder.VariableCount - v0);
        }

        return op == "AM"
            ? PseudoBooleanEncoder.AtMost(builder, literals, weights, bound, encoding.Value)
            : PseudoBooleanEncoder.AtLeast(builder, literals, weights, bound, encoding.Value);
    }

    private sealed record CorpusShape(bool IsCardinality, string Op, int N, long Bound, long[]? Weights,
        string Label);

    private static List<CorpusShape> ReadCorpus()
    {
        var path = LocateCorpus();
        var shapes = new List<CorpusShape>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "CARD")
            {
                var n = int.Parse(parts[2]["n=".Length..]);
                var k = int.Parse(parts[3]["k=".Length..]);
                shapes.Add(new CorpusShape(true, parts[1], n, k, null, line));
            }
            else
            {
                var n = int.Parse(parts[2]["n=".Length..]);
                var bound = long.Parse(parts[3]["bound=".Length..]);
                var weights = parts[4]["weights=".Length..].Split(',').Select(long.Parse).ToArray();
                shapes.Add(new CorpusShape(false, parts[1], n, bound, weights, line));
            }
        }

        return shapes;
    }

    private static string LocateCorpus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "encoding_corpus.txt");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate tools/encoding_corpus.txt from the test output directory");
    }
}
