using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Correctness gate for the d-DNNF knowledge compiler. The primary oracle is the ROBDD:
///     both compute exact model counts by independent means, so <c>CompileToDnnf(f).CountModels()</c>
///     must equal <c>BinaryDecisionDiagram.BuildWithBestOrder(f).CountSatisfyingAssignments()</c>
///     for every formula. Brute-force truth-table counting, model enumeration and weighted counts
///     provide further independent checks.
/// </summary>
public class KnowledgeCompilationTests
{
    private static AstNode Parse(string expression)
    {
        return RandomExpressions.Parse(expression);
    }

    private static BigInteger BddCount(AstNode formula)
    {
        return BinaryDecisionDiagram.BuildWithBestOrder(formula).CountSatisfyingAssignments();
    }

    private static AstNode RandomExtendedAst(Random rng, int variableCount, int depth)
    {
        if (depth <= 0 || rng.Next(4) == 0)
        {
            AstNode leaf = new VariableNode($"v{rng.Next(variableCount)}");
            return rng.Next(3) == 0 ? new NotNode(leaf) : leaf;
        }

        var l = RandomExtendedAst(rng, variableCount, depth - 1);
        var r = RandomExtendedAst(rng, variableCount, depth - 1);
        return rng.Next(7) switch
        {
            0 => new AndNode(l, r),
            1 => new OrNode(l, r),
            2 => new XorNode(l, r),
            3 => new EqvNode(l, r),
            4 => new ImpNode(l, r),
            5 => new NandNode(l, r),
            _ => new NorNode(l, r)
        };
    }

    [Fact]
    public void CountModels_MatchesBdd_OnRandomTextFormulas()
    {
        // HARD DIFFERENTIAL GATE vs the BDD oracle. Depth-bounded random expressions keep the
        // Tseitin CNF small even as the variable count grows to 12.
        var rng = new Random(2026);
        var cases = 0;
        for (var trial = 0; trial < 400; trial++)
        {
            var variableCount = rng.Next(1, 13);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(3, 7), allowConstants: true);
            var ast = Parse(expression);

            var byDnnf = KnowledgeCompilation.CompileToDnnf(ast).CountModels();
            var byBdd = BddCount(ast);
            Assert.True(byDnnf == byBdd,
                $"Trial {trial}: DNNF={byDnnf} BDD={byBdd} for '{expression}'");
            cases++;
        }

        Assert.True(cases >= 400);
    }

    [Fact]
    public void CountModels_MatchesBdd_OnRandomExtendedAstFormulas()
    {
        // The full operator set (Xor/Eqv/Imp/Nand/Nor) exercised against the BDD oracle.
        var rng = new Random(4711);
        for (var trial = 0; trial < 250; trial++)
        {
            var ast = RandomExtendedAst(rng, rng.Next(1, 9), rng.Next(2, 5));
            var byDnnf = KnowledgeCompilation.CompileToDnnf(ast).CountModels();
            var byBdd = BddCount(ast);
            Assert.True(byDnnf == byBdd, $"Trial {trial}: DNNF={byDnnf} BDD={byBdd}");
        }
    }

    [Fact]
    public void CountModels_MatchesBruteForce_OnRandomFormulas()
    {
        // Differential vs full truth-table enumeration (<=12 variables).
        var rng = new Random(1234);
        for (var trial = 0; trial < 200; trial++)
        {
            var variableCount = rng.Next(1, 11);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(3, 6), allowConstants: true);
            var ast = Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var byBrute = new BigInteger(RandomExpressions.CountModels(ast, variables));
            var byDnnf = KnowledgeCompilation.CompileToDnnf(ast).CountModels();
            Assert.True(byDnnf == byBrute,
                $"Trial {trial}: DNNF={byDnnf} brute-force={byBrute} for '{expression}'");
        }
    }

    [Fact]
    public void CountModels_MatchesBdd_OnStructuredFamilies()
    {
        // XOR/parity chains and other structured formulas whose counts are known in closed form.
        // n starts at 2 because the n-ary And/Or nodes require at least two operands.
        for (var n = 2; n <= 10; n++)
        {
            var vars = Enumerable.Range(0, n).Select(i => (AstNode)new VariableNode($"x{i}")).ToList();

            AstNode parity = vars[0];
            for (var i = 1; i < n; i++) parity = new XorNode(parity, vars[i]);
            Assert.Equal(BddCount(parity), KnowledgeCompilation.CompileToDnnf(parity).CountModels());
            // Parity over n variables is true on exactly half of the 2^n assignments.
            Assert.Equal(BigInteger.Pow(2, n - 1), KnowledgeCompilation.CompileToDnnf(parity).CountModels());

            var conjunction = new AndNode(vars);
            Assert.Equal(BigInteger.One, KnowledgeCompilation.CompileToDnnf(conjunction).CountModels());

            var disjunction = new OrNode(vars);
            Assert.Equal(BigInteger.Pow(2, n) - 1, KnowledgeCompilation.CompileToDnnf(disjunction).CountModels());
        }
    }

    [Fact]
    public void CountModels_NegationComplementsToTwoToTheN()
    {
        // Property: count(f) + count(!f) == 2^n over f's variables.
        var rng = new Random(7);
        for (var trial = 0; trial < 100; trial++)
        {
            var variableCount = rng.Next(1, 9);
            var ast = RandomExtendedAst(rng, variableCount, rng.Next(2, 5));
            var n = ast.GetVariables().Count;
            if (n == 0) continue;

            var positive = KnowledgeCompilation.CompileToDnnf(ast).CountModels();
            var negated = KnowledgeCompilation.CompileToDnnf(new NotNode(ast)).CountModels();
            Assert.Equal(BigInteger.Pow(2, n), positive + negated);
        }
    }

    [Fact]
    public void ConstantsAndUnsatisfiable_HaveExpectedCountsAndSatisfiability()
    {
        // Constant TRUE: 2^n over its variables (here 0 vars -> 1).
        var constantTrue = KnowledgeCompilation.CompileToDnnf(ConstantNode.True);
        Assert.True(constantTrue.IsSatisfiable);
        Assert.Equal(BigInteger.One, constantTrue.CountModels());
        Assert.Empty(constantTrue.Variables);

        // a | !a is a tautology over {a} -> 2 models. Built explicitly because the parser
        // constant-folds the textual form to TRUE (dropping the variable).
        var a = new VariableNode("a");
        var tautology = KnowledgeCompilation.CompileToDnnf(new OrNode(a, new NotNode(a)));
        Assert.True(tautology.IsSatisfiable);
        Assert.Equal(new BigInteger(2), tautology.CountModels());
        Assert.Equal(new[] { "a" }, tautology.Variables);

        // Constant FALSE and a & !a are unsatisfiable.
        foreach (var unsat in new[] { KnowledgeCompilation.CompileToDnnf(ConstantNode.False),
                     KnowledgeCompilation.CompileToDnnf(Parse("a & !a")) })
        {
            Assert.False(unsat.IsSatisfiable);
            Assert.Equal(BigInteger.Zero, unsat.CountModels());
            Assert.Empty(unsat.EnumerateModels());
        }
    }

    [Fact]
    public void EnumerateModels_MatchesFacadeAndCount()
    {
        var rng = new Random(555);
        for (var trial = 0; trial < 120; trial++)
        {
            var variableCount = rng.Next(1, 8);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var dnnfModels = circuit.EnumerateModels().Select(Canonical).ToHashSet();
            var facadeModels = FormulaAnalysis.EnumerateModels(ast).Select(Canonical).ToHashSet();

            Assert.True(dnnfModels.SetEquals(facadeModels),
                $"Trial {trial}: enumeration set differs for '{expression}'");
            Assert.Equal(circuit.CountModels(), new BigInteger(dnnfModels.Count));
        }
    }

    private static string Canonical(IReadOnlyDictionary<string, bool> model)
    {
        return string.Join(",", model.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    [Fact]
    public void WeightedModelCount_WithUnitWeights_EqualsCountModels()
    {
        var rng = new Random(909);
        for (var trial = 0; trial < 100; trial++)
        {
            var variableCount = rng.Next(1, 8);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);

            var circuit = KnowledgeCompilation.CompileToDnnf(ast);
            var weights = ast.GetVariables().ToDictionary(v => v, _ => (1.0, 1.0));
            var weighted = circuit.WeightedModelCount(weights);
            Assert.Equal((double)circuit.CountModels(), weighted, 6);
        }
    }

    [Fact]
    public void WeightedModelCount_HandComputedCases()
    {
        // a | b : models {TT, TF, FT}. WMC = pa*pb + pa*nb + na*pb.
        var orCircuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b"));
        var orWeights = new Dictionary<string, (double, double)> { ["a"] = (2, 3), ["b"] = (5, 7) };
        // (pa+na)(pb+nb) - na*nb = 5*12 - 21 = 39.
        Assert.Equal(39.0, orCircuit.WeightedModelCount(orWeights), 6);

        // a & b : single model TT -> pa*pb = 10.
        var andCircuit = KnowledgeCompilation.CompileToDnnf(Parse("a & b"));
        Assert.Equal(10.0, andCircuit.WeightedModelCount(orWeights), 6);

        // Single variable a : one model -> pa.
        var single = KnowledgeCompilation.CompileToDnnf(Parse("a"));
        Assert.Equal(2.0, single.WeightedModelCount(new Dictionary<string, (double, double)> { ["a"] = (2, 3) }), 6);

        // Free variable b in "a" contributes (pb+nb): WMC(a) with a=(2,3), b default (1,1)
        // over scope {a,b} is not applicable here (b absent), so verify a free var via a|!a&...
        // Missing-weight variables default to (1,1): WMC(a|b) with only a weighted equals
        // count-style contribution for b.
        var partial = orCircuit.WeightedModelCount(new Dictionary<string, (double, double)> { ["a"] = (1, 1) });
        Assert.Equal((double)orCircuit.CountModels(), partial, 6);
    }

    [Fact]
    public void NodeBudget_Exceeded_Throws()
    {
        // A wide parity function forces many decision nodes; a tiny budget must abort.
        var vars = Enumerable.Range(0, 12).Select(i => (AstNode)new VariableNode($"x{i}")).ToList();
        AstNode parity = vars[0];
        for (var i = 1; i < vars.Count; i++) parity = new XorNode(parity, vars[i]);

        var ex = Assert.Throws<NodeBudgetExceededException>(() =>
            KnowledgeCompilation.CompileToDnnf(parity, nodeBudget: 8));
        Assert.Contains("budget", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreCancelledToken_ThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            KnowledgeCompilation.CompileToDnnf(Parse("a & b | c"), cancellationToken: cts.Token));
    }

    [Fact]
    public void EnumerateModels_HonorsCancellation()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("a | b | c | d"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            circuit.EnumerateModels(cts.Token).ToList());
    }
}
