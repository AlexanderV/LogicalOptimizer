using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Correctness gate for d-DNNF marginal probabilities and model sampling (roadmap P1.3),
///     built on the weighted model counting and evidence queries of P1.2.
///     <para>
///         Marginals are validated against an independent exhaustive (weighted) enumeration:
///         <c>MarginalProbability(v, w)</c> must equal <c>WMC(w, v = true) / WMC(w)</c>, and for
///         uniform weights the plain fraction of models with <c>v = true</c>.
///     </para>
///     <para>
///         Sampling is validated three ways: every drawn model must satisfy the formula and assign
///         exactly the circuit's variables; the empirical frequencies must track the (weighted)
///         model probabilities within a wide, deterministic band; and a seeded sequence must be
///         bit-for-bit reproducible. All randomness is seeded, so every assertion is deterministic —
///         the "statistical" bands are only there to give the fixed-seed draw generous head-room, not
///         to trade correctness for luck.
///     </para>
/// </summary>
public class DnnfSamplingTests
{
    private static AstNode Parse(string expression)
    {
        return RandomExpressions.Parse(expression);
    }

    private static string Key(IReadOnlyDictionary<string, bool> model)
    {
        return string.Join(",", model.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
    }

    // TruthTable.Evaluate takes a concrete dictionary; sampled models are IReadOnlyDictionary.
    private static bool Satisfies(AstNode ast, IReadOnlyDictionary<string, bool> model)
    {
        return TruthTable.Evaluate(ast, new Dictionary<string, bool>(model));
    }

    // All satisfying assignments of the formula over the given variables, by brute force.
    private static List<Dictionary<string, bool>> AllModels(AstNode ast, IReadOnlyList<string> variables)
    {
        var models = new List<Dictionary<string, bool>>();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            var assignment = new Dictionary<string, bool>(variables.Count);
            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (m & (1 << j)) != 0;
            if (TruthTable.Evaluate(ast, assignment)) models.Add(assignment);
        }

        return models;
    }

    // #models consistent with the (optional) partial evidence, by brute force.
    private static int BruteForceCount(AstNode ast, IReadOnlyList<string> variables,
        IReadOnlyDictionary<string, bool>? evidence = null)
    {
        var count = 0;
        foreach (var model in AllModels(ast, variables))
        {
            if (evidence is not null && evidence.Any(e => model[e.Key] != e.Value)) continue;
            count++;
        }

        return count;
    }

    // Weighted count over models consistent with the (optional) evidence: sum of per-model weight
    // products. Independent of the circuit — the reference the marginal is checked against.
    private static double ExhaustiveWeighted(AstNode ast, IReadOnlyList<string> variables,
        IReadOnlyDictionary<string, (double positive, double negative)> weights,
        IReadOnlyDictionary<string, bool>? evidence = null)
    {
        var total = 0.0;
        foreach (var model in AllModels(ast, variables))
        {
            if (evidence is not null && evidence.Any(e => model[e.Key] != e.Value)) continue;
            var product = 1.0;
            foreach (var variable in variables)
            {
                var (positive, negative) = weights[variable];
                product *= model[variable] ? positive : negative;
            }

            total += product;
        }

        return total;
    }

    [Fact]
    public void MarginalProbability_Uniform_EqualsBruteForceFraction()
    {
        // Uniform weights (an empty map defaults every variable to (1, 1)): the marginal is exactly
        // the fraction of the formula's models in which the variable is true.
        var rng = new Random(31415);
        var uniform = new Dictionary<string, (double positive, double negative)>();
        var cases = 0;
        for (var trial = 0; trial < 300; trial++)
        {
            var variableCount = rng.Next(1, 11);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(3, 6), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var total = BruteForceCount(ast, variables);
            if (total == 0) continue; // UNSAT: marginal is undefined (checked separately)

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            foreach (var variable in variables)
            {
                var withTrue = BruteForceCount(ast, variables, new Dictionary<string, bool> { [variable] = true });
                var expected = (double)withTrue / total;
                var actual = circuit.MarginalProbability(variable, uniform);
                Assert.True(Math.Abs(actual - expected) <= 1e-9,
                    $"Trial {trial} var {variable}: marginal={actual} expected={expected} for '{expression}'");
                cases++;
            }
        }

        Assert.True(cases >= 300);
    }

    [Fact]
    public void MarginalProbability_Weighted_EqualsIndependentRatio()
    {
        // Weighted marginal must equal WMC(w, v=true) / WMC(w) computed by exhaustive enumeration.
        var rng = new Random(2718281);
        var cases = 0;
        for (var trial = 0; trial < 250; trial++)
        {
            var variableCount = rng.Next(1, 8);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var weights = variables.ToDictionary(
                v => v,
                _ => ((double)rng.Next(1, 6), (double)rng.Next(1, 6)));

            var total = ExhaustiveWeighted(ast, variables, weights);
            if (total <= 0.0) continue; // UNSAT

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            foreach (var variable in variables)
            {
                var withTrue = ExhaustiveWeighted(ast, variables, weights,
                    new Dictionary<string, bool> { [variable] = true });
                var expected = withTrue / total;
                var actual = circuit.MarginalProbability(variable, weights);
                Assert.True(Math.Abs(actual - expected) <= 1e-9 + 1e-9 * Math.Abs(expected),
                    $"Trial {trial} var {variable}: marginal={actual} expected={expected} for '{expression}'");
                cases++;
            }
        }

        Assert.True(cases >= 250);
    }

    [Fact]
    public void MarginalProbability_HandComputedCase()
    {
        // a | b, weights a=(2,3) b=(5,7). Models {TT:10, TF:14, FT:15}, total 39.
        // Marginal(a) = (TT + TF) / 39 = (10 + 14) / 39; Marginal(b) = (TT + FT) / 39 = (10 + 15) / 39.
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var weights = new Dictionary<string, (double positive, double negative)>
            { ["a"] = (2, 3), ["b"] = (5, 7) };
        Assert.Equal(24.0 / 39.0, circuit.MarginalProbability("a", weights), 9);
        Assert.Equal(25.0 / 39.0, circuit.MarginalProbability("b", weights), 9);
    }

    [Fact]
    public void SampleModel_EverySampleSatisfiesAndAssignsExactlyTheVariables()
    {
        // Across random satisfiable formulas, every drawn model must satisfy the formula and assign
        // precisely the circuit's Variables (free variables included).
        var rng = new Random(52719);
        var checks = 0;
        for (var trial = 0; trial < 200; trial++)
        {
            var variableCount = rng.Next(1, 8);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            if (!circuit.IsSatisfiable) continue;

            var expectedKeys = circuit.Variables.OrderBy(v => v).ToList();
            foreach (var model in circuit.SampleModels(30, seed: trial))
            {
                Assert.Equal(expectedKeys, model.Keys.OrderBy(v => v).ToList());
                Assert.True(Satisfies(ast, model),
                    $"Trial {trial}: sampled {Key(model)} does not satisfy '{expression}'");
                checks++;
            }
        }

        Assert.True(checks > 0);
    }

    [Fact]
    public void UnweightedSampling_IsApproximatelyUniform()
    {
        // Draw a large, FIXED-seed batch and assert each model's empirical frequency sits within a
        // wide absolute band of 1/modelCount. With 40,000 draws the binomial std-dev for a model of
        // probability ~0.14 is ~0.0017, so the 0.03 band is >15 sigma — the seed makes the run
        // deterministic, and the band only guarantees no realistic draw could breach it.
        const int samples = 40000;
        const double tolerance = 0.03;
        foreach (var expression in new[] { "a | b | c", "(a | b) & (!b | c)", "a | b | c | d" })
        {
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var models = AllModels(ast, variables);
            var expected = 1.0 / models.Count;

            var counts = new Dictionary<string, int>();
            foreach (var model in circuit.SampleModels(samples, seed: 12345))
            {
                Assert.True(Satisfies(ast, model), $"sampled non-model of '{expression}'");
                counts[Key(model)] = counts.GetValueOrDefault(Key(model)) + 1;
            }

            // No non-model was ever produced, and every model was hit with ~uniform frequency.
            Assert.Equal(models.Count, counts.Count);
            foreach (var model in models)
            {
                var frequency = counts.GetValueOrDefault(Key(model)) / (double)samples;
                Assert.True(Math.Abs(frequency - expected) <= tolerance,
                    $"'{expression}' model {Key(model)}: freq={frequency:F4} expected={expected:F4}");
            }
        }
    }

    [Fact]
    public void WeightedSampling_TracksNormalizedWeights()
    {
        // Empirical frequencies must track each model's normalized weighted probability. Same
        // deterministic-seed / wide-band reasoning as the uniform case.
        const int samples = 40000;
        const double tolerance = 0.03;
        var ast = Parse("a | b | c");
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        var weights = new Dictionary<string, (double positive, double negative)>
            { ["a"] = (3, 1), ["b"] = (2, 1), ["c"] = (1, 2) };

        var circuit = KnowledgeCompilation.CompileToDnnf(ast);
        var models = AllModels(ast, variables);
        var total = ExhaustiveWeighted(ast, variables, weights);

        double Weight(IReadOnlyDictionary<string, bool> model)
        {
            var product = 1.0;
            foreach (var variable in variables)
            {
                var (positive, negative) = weights[variable];
                product *= model[variable] ? positive : negative;
            }

            return product;
        }

        var counts = new Dictionary<string, int>();
        foreach (var model in circuit.SampleModels(samples, seed: 777, weights))
        {
            Assert.True(Satisfies(ast, model), "weighted sample is not a model");
            counts[Key(model)] = counts.GetValueOrDefault(Key(model)) + 1;
        }

        foreach (var model in models)
        {
            var expected = Weight(model) / total;
            var frequency = counts.GetValueOrDefault(Key(model)) / (double)samples;
            Assert.True(Math.Abs(frequency - expected) <= tolerance,
                $"model {Key(model)}: freq={frequency:F4} expected={expected:F4}");
        }
    }

    [Fact]
    public void SampleModels_IsDeterministic_ForSeed()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b | c | d"));

        var first = circuit.SampleModels(50, seed: 424242).Select(Key).ToList();
        var again = circuit.SampleModels(50, seed: 424242).Select(Key).ToList();
        Assert.Equal(first, again); // same seed => identical sequence

        var other = circuit.SampleModels(50, seed: 999983).Select(Key).ToList();
        Assert.NotEqual(first, other); // different seed => (overwhelmingly likely) different sequence
    }

    [Fact]
    public void SampleModel_ConsumesTheSuppliedRandom_Deterministically()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b | c"));
        var a = circuit.SampleModel(new Random(7));
        var b = circuit.SampleModel(new Random(7));
        Assert.Equal(Key(a), Key(b));
    }

    [Fact]
    public void Sampling_FromConditionedCircuit_RespectsEvidence()
    {
        // Sampling has no evidence overload, but sampling a conditioned circuit must only ever
        // produce models consistent with the evidence — never an inconsistent one.
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b | c"));
        var conditioned = circuit.Condition(new Dictionary<string, bool> { ["a"] = true });
        foreach (var model in conditioned.SampleModels(200, seed: 3))
        {
            Assert.True(model["a"], "conditioned sample violated the pinned evidence");
            Assert.True(Satisfies(Parse("a | b | c"), model));
        }
    }

    [Fact]
    public void Sampling_FromImpossibleEvidence_NeverFabricatesAModel()
    {
        // Conditioning "a" on a=false yields a zero-model circuit; sampling it must throw rather
        // than invent an inconsistent assignment.
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a"));
        var impossible = circuit.Condition(new Dictionary<string, bool> { ["a"] = false });
        Assert.False(impossible.IsSatisfiable);
        Assert.Equal(BigInteger.Zero, impossible.CountModels());

        Assert.Throws<InvalidOperationException>(() => impossible.SampleModel(new Random(1)));
        Assert.Throws<InvalidOperationException>(() => impossible.SampleModels(5, seed: 1));
    }

    [Fact]
    public void UnsatisfiableCircuit_Sampling_Throws()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a & !a"));
        Assert.False(circuit.IsSatisfiable);

        Assert.Throws<InvalidOperationException>(() => circuit.SampleModel(new Random(1)));
        // SampleModels validates eagerly, so the throw happens on the call, not on enumeration.
        Assert.Throws<InvalidOperationException>(() => circuit.SampleModels(3, seed: 1));
        // (The undefined-marginal case for a zero total weight is covered by ZeroTotalWeight_Sampling_Throws;
        // "a & !a" constant-folds away its variable, so it has no variable to take a marginal of.)
    }

    [Fact]
    public void ZeroTotalWeight_Sampling_Throws()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var zero = new Dictionary<string, (double positive, double negative)>
            { ["a"] = (0, 0), ["b"] = (0, 0) };

        Assert.Throws<InvalidOperationException>(() => circuit.SampleModel(new Random(1), zero));
        Assert.Throws<InvalidOperationException>(() => circuit.SampleModels(3, seed: 1, zero));
        Assert.Throws<InvalidOperationException>(() => circuit.MarginalProbability("a", zero));
    }

    [Fact]
    public void UnknownVariable_Marginal_Throws()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        Assert.Throws<ArgumentException>(() =>
            circuit.MarginalProbability("z", new Dictionary<string, (double, double)>()));
    }

    [Fact]
    public void NegativeOrNaNWeights_Throw()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var negative = new Dictionary<string, (double positive, double negative)> { ["a"] = (-1, 1) };
        var nan = new Dictionary<string, (double positive, double negative)> { ["b"] = (double.NaN, 1) };
        var infinity = new Dictionary<string, (double positive, double negative)>
            { ["a"] = (double.PositiveInfinity, 1) };

        Assert.Throws<ArgumentException>(() => circuit.MarginalProbability("a", negative));
        Assert.Throws<ArgumentException>(() => circuit.SampleModel(new Random(1), nan));
        Assert.Throws<ArgumentException>(() => circuit.SampleModels(1, seed: 1, infinity));
    }

    [Fact]
    public void NegativeCount_Throws()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        Assert.Throws<ArgumentOutOfRangeException>(() => circuit.SampleModels(-1, seed: 1));
    }

    [Fact]
    public void ZeroCount_YieldsEmptySequence()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        Assert.Empty(circuit.SampleModels(0, seed: 1));
    }
}
