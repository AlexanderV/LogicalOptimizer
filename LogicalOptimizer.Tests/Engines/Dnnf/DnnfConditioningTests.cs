using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Correctness gate for d-DNNF conditioning and evidence queries (roadmap P1.2). The
///     conditioned/evidence count is validated against the ROBDD oracle (build the formula
///     conjoined with the evidence literals over the SAME input-variable universe, then count)
///     and against brute-force truth-table counting. Conditioning must never mutate the source
///     circuit, and the evidence overloads must equal <c>Condition(evidence).CountModels()</c>.
/// </summary>
public class DnnfConditioningTests
{
    private static AstNode Parse(string expression)
    {
        return RandomExpressions.Parse(expression);
    }

    // The BDD oracle for "models of f consistent with evidence": conjoin f with the evidence
    // literals. Because every evidence variable already occurs in f, the manager's variable set
    // is exactly f's input variables, so CountSatisfyingAssignments counts over that universe.
    private static BigInteger BddCountWithEvidence(AstNode formula, IReadOnlyDictionary<string, bool> evidence)
    {
        AstNode conjoined = formula;
        foreach (var (name, value) in evidence)
        {
            AstNode literal = value ? new VariableNode(name) : new NotNode(new VariableNode(name));
            conjoined = new AndNode(conjoined, literal);
        }

        return BinaryDecisionDiagram.BuildWithBestOrder(conjoined).CountSatisfyingAssignments();
    }

    private static Dictionary<string, bool> RandomEvidence(Random rng, IReadOnlyList<string> variables)
    {
        var evidence = new Dictionary<string, bool>();
        foreach (var variable in variables)
            if (rng.Next(3) == 0) // pin roughly a third of the variables
                evidence[variable] = rng.Next(2) == 0;
        return evidence;
    }

    [Fact]
    public void CountModels_WithEvidence_MatchesBdd_OnRandomFormulas()
    {
        // HARD DIFFERENTIAL GATE vs the BDD oracle across many seeds and evidence sets.
        var rng = new Random(20260729);
        var cases = 0;
        for (var trial = 0; trial < 400; trial++)
        {
            var variableCount = rng.Next(1, 11);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(3, 6), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue; // constant-folded to a constant: no variables to pin

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);

            for (var probe = 0; probe < 3; probe++)
            {
                var evidence = RandomEvidence(rng, variables);
                var byDnnf = circuit.CountModels(evidence);
                var byBdd = BddCountWithEvidence(ast, evidence);
                Assert.True(byDnnf == byBdd,
                    $"Trial {trial}/{probe}: DNNF={byDnnf} BDD={byBdd} for '{expression}' evidence={Show(evidence)}");
                cases++;
            }
        }

        Assert.True(cases >= 400);
    }

    [Fact]
    public void Condition_ThenCount_MatchesBddRestrictThenCount()
    {
        // Condition(a).CountModels() must equal the BDD restricted then counted.
        var rng = new Random(987654);
        for (var trial = 0; trial < 300; trial++)
        {
            var variableCount = rng.Next(1, 10);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(3, 6), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var evidence = RandomEvidence(rng, variables);

            var conditioned = circuit.Condition(evidence);
            var byCondition = conditioned.CountModels();
            var byEvidence = circuit.CountModels(evidence);
            var byBdd = BddCountWithEvidence(ast, evidence);

            Assert.True(byCondition == byBdd,
                $"Trial {trial}: Condition={byCondition} BDD={byBdd} for '{expression}' evidence={Show(evidence)}");
            // The two query paths (new circuit vs single pass) must agree exactly.
            Assert.Equal(byEvidence, byCondition);
            // The conditioned circuit keeps the same variable universe.
            Assert.Equal(circuit.Variables, conditioned.Variables);
        }
    }

    [Fact]
    public void CountModels_WithEvidence_MatchesBruteForce()
    {
        // Independent check: brute-force count of full models consistent with the evidence.
        var rng = new Random(135790);
        for (var trial = 0; trial < 200; trial++)
        {
            var variableCount = rng.Next(1, 11);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(3, 6), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var evidence = RandomEvidence(rng, variables);
            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var byDnnf = circuit.CountModels(evidence);

            var byBrute = BruteForceCountWithEvidence(ast, variables, evidence);
            Assert.True(byDnnf == byBrute,
                $"Trial {trial}: DNNF={byDnnf} brute-force={byBrute} for '{expression}' evidence={Show(evidence)}");
        }
    }

    private static BigInteger BruteForceCountWithEvidence(AstNode ast, IReadOnlyList<string> variables,
        IReadOnlyDictionary<string, bool> evidence)
    {
        var count = BigInteger.Zero;
        var assignment = new Dictionary<string, bool>();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            var consistent = true;
            for (var j = 0; j < variables.Count; j++)
            {
                var value = (m & (1 << j)) != 0;
                assignment[variables[j]] = value;
                if (evidence.TryGetValue(variables[j], out var fixedValue) && fixedValue != value) consistent = false;
            }

            if (consistent && TruthTable.Evaluate(ast, assignment)) count++;
        }

        return count;
    }

    [Fact]
    public void Condition_DoesNotMutateOriginal()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b | c"));
        var originalCount = circuit.CountModels();
        var originalNodeCount = circuit.NodeCount;
        var originalVariables = circuit.Variables.ToList();

        var conditioned = circuit.Condition(new Dictionary<string, bool> { ["a"] = true });

        // The source is untouched.
        Assert.Equal(originalCount, circuit.CountModels());
        Assert.Equal(originalNodeCount, circuit.NodeCount);
        Assert.Equal(originalVariables, circuit.Variables);

        // a | b | c has 7 models; 4 of them have a=true.
        Assert.Equal(new BigInteger(7), originalCount);
        Assert.Equal(new BigInteger(4), conditioned.CountModels());
    }

    [Fact]
    public void EmptyEvidence_EqualsPlainCount()
    {
        var rng = new Random(2468);
        for (var trial = 0; trial < 50; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(1, 8), rng.Next(2, 5), allowConstants: true);
            var circuit = KnowledgeCompilation.CompileToDnnf(Parse(expression));
            var empty = new Dictionary<string, bool>();

            Assert.Equal(circuit.CountModels(), circuit.CountModels(empty));
            Assert.Equal(circuit.CountModels(), circuit.Condition(empty).CountModels());
        }
    }

    [Fact]
    public void FullEvidence_YieldsZeroOrOne()
    {
        var rng = new Random(9753);
        for (var trial = 0; trial < 100; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(1, 9), rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var assignment = variables.ToDictionary(v => v, _ => rng.Next(2) == 0);

            var count = circuit.CountModels(assignment);
            Assert.True(count == BigInteger.Zero || count == BigInteger.One, $"full evidence count was {count}");

            // It is exactly one iff the assignment satisfies the formula.
            var expected = TruthTable.Evaluate(ast, assignment) ? BigInteger.One : BigInteger.Zero;
            Assert.Equal(expected, count);
            Assert.Equal(expected, circuit.Condition(assignment).CountModels());
        }
    }

    [Fact]
    public void WeightedModelCount_WithEvidence_MatchesExhaustiveEnumeration()
    {
        // Validate the weighted evidence path against exhaustive weighted enumeration on small
        // circuits. Weights are exact small integers so the products/sums are representable; the
        // tolerance below only guards against IEEE-754 accumulation noise.
        const double tolerance = 1e-9;
        var rng = new Random(11223344);
        for (var trial = 0; trial < 120; trial++)
        {
            var variableCount = rng.Next(1, 7);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var weights = variables.ToDictionary(
                v => v,
                _ => ((double)rng.Next(1, 5), (double)rng.Next(1, 5)));
            var evidence = RandomEvidence(rng, variables);

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var byCircuit = circuit.WeightedModelCount(weights, evidence);
            var byEnumeration = ExhaustiveWeighted(ast, variables, weights, evidence);

            Assert.True(Math.Abs(byCircuit - byEnumeration) <= tolerance + tolerance * Math.Abs(byEnumeration),
                $"Trial {trial}: circuit={byCircuit} enumeration={byEnumeration} for '{expression}'");
        }
    }

    private static double ExhaustiveWeighted(AstNode ast, IReadOnlyList<string> variables,
        IReadOnlyDictionary<string, (double positive, double negative)> weights,
        IReadOnlyDictionary<string, bool> evidence)
    {
        var total = 0.0;
        var assignment = new Dictionary<string, bool>();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            var consistent = true;
            for (var j = 0; j < variables.Count; j++)
            {
                var value = (m & (1 << j)) != 0;
                assignment[variables[j]] = value;
                if (evidence.TryGetValue(variables[j], out var fixedValue) && fixedValue != value) consistent = false;
            }

            if (!consistent || !TruthTable.Evaluate(ast, assignment)) continue;

            var product = 1.0;
            foreach (var variable in variables)
            {
                var (positive, negative) = weights[variable];
                product *= assignment[variable] ? positive : negative;
            }

            total += product;
        }

        return total;
    }

    [Fact]
    public void WeightedModelCount_WithEmptyEvidence_EqualsUnconditioned()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var weights = new Dictionary<string, (double, double)> { ["a"] = (2, 3), ["b"] = (5, 7) };
        var empty = new Dictionary<string, bool>();

        Assert.Equal(circuit.WeightedModelCount(weights), circuit.WeightedModelCount(weights, empty), 9);
    }

    [Fact]
    public void WeightedModelCount_WithEvidence_HandComputedCase()
    {
        // a | b, weights a=(2,3) b=(5,7). Condition on a=true: models {TT, TF}.
        // WMC = pa*pb + pa*nb = 2*5 + 2*7 = 24.
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var weights = new Dictionary<string, (double, double)> { ["a"] = (2, 3), ["b"] = (5, 7) };
        var evidence = new Dictionary<string, bool> { ["a"] = true };
        Assert.Equal(24.0, circuit.WeightedModelCount(weights, evidence), 9);
    }

    [Fact]
    public void UnknownVariable_Throws()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var bogus = new Dictionary<string, bool> { ["z"] = true };

        Assert.Throws<ArgumentException>(() => circuit.CountModels(bogus));
        Assert.Throws<ArgumentException>(() => circuit.Condition(bogus));
        Assert.Throws<ArgumentException>(() =>
            circuit.WeightedModelCount(new Dictionary<string, (double, double)>(), bogus));
    }

    [Fact]
    public void RepeatedQueries_ReuseTheSameCompiledCircuit()
    {
        // Compile once; issue many conditioning/evidence queries against the very same instance.
        // No API recompiles the formula, and the source circuit is immutable, so its identity and
        // model count are invariant across the whole batch.
        var ast = Parse("(a | b) & (c | !d) & (a | !c)");
        var circuit = KnowledgeCompilation.CompileToDnnf(ast);
        var baselineCount = circuit.CountModels();
        var baselineNodeCount = circuit.NodeCount;

        var rng = new Random(4242);
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        for (var i = 0; i < 500; i++)
        {
            var evidence = RandomEvidence(rng, variables);
            var byEvidence = circuit.CountModels(evidence);
            var conditioned = circuit.Condition(evidence);
            Assert.Equal(byEvidence, conditioned.CountModels());

            // Condition() must return a NEW circuit, never mutate/alias the shared source
            // (Assert.Same(circuit, circuit) here was a no-op — it can never fail).
            Assert.NotSame(circuit, conditioned);
            Assert.Equal(baselineCount, circuit.CountModels());
            Assert.Equal(baselineNodeCount, circuit.NodeCount);
        }
    }

    [Fact]
    public void Condition_HonorsCancellation_OnLargeCircuit()
    {
        // A wide parity function compiles to a large decision DAG; a pre-cancelled token must
        // interrupt the conditioning rewrite.
        var vars = Enumerable.Range(0, 16).Select(i => (AstNode)new VariableNode($"x{i}")).ToList();
        AstNode parity = vars[0];
        for (var i = 1; i < vars.Count; i++) parity = new XorNode(parity, vars[i]);

        var circuit = KnowledgeCompilation.CompileToDnnf(parity);
        Assert.True(circuit.NodeCount > 100, $"expected a large circuit, got {circuit.NodeCount} nodes");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            circuit.Condition(new Dictionary<string, bool> { ["x0"] = true }, cts.Token));
    }

    private static string Show(IReadOnlyDictionary<string, bool> evidence)
    {
        return "{" + string.Join(",", evidence.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) + "}";
    }
}
