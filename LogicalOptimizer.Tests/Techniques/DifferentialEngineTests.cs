using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Differential testing: the same question is answered by INDEPENDENT engines
///     (brute-force truth table, Quine–McCluskey, SAT miter, ROBDD, model enumeration)
///     and every disagreement is a bug in one of them. Seeds are fixed for reproduction.
/// </summary>
public class DifferentialEngineTests
{
    private static (List<string> Variables, HashSet<int> OnSet) RandomFunction(Random rng, int variableCount)
    {
        var variables = Enumerable.Range(0, variableCount).Select(i => $"v{i}").ToList();
        var onSet = new HashSet<int>();
        for (var m = 0; m < 1 << variableCount; m++)
            if (rng.Next(2) == 0)
                onSet.Add(m);
        return (variables, onSet);
    }

    private static AstNode SopOf(IReadOnlyList<string> variables, HashSet<int> onSet)
    {
        if (onSet.Count == 0) return ConstantNode.False;
        var terms = onSet.Select(m => string.Join(" & ", variables.Select((v, j) =>
            (m & (1 << j)) != 0 ? v : "!" + v)));
        return RandomExpressions.Parse(string.Join(" | ", terms));
    }

    [Fact]
    public void QuineMcCluskey_AgreesWithBruteForceOracle()
    {
        var rng = new Random(11);
        for (var trial = 0; trial < 30; trial++)
        {
            var (variables, onSet) = RandomFunction(rng, rng.Next(2, 8));
            var minimal = TruthTableMinimizer.MinimalSop(variables, onSet);

            // Oracle: re-evaluate the minimized expression on every minterm
            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                Assert.True(onSet.Contains(m) == TruthTable.Evaluate(minimal, assignment),
                    $"Trial {trial}: QM output differs from the ON-set at minterm {m}");
            }
        }
    }

    [Fact]
    public void ThreeEquivalenceEngines_AgreeOnEquivalentPairs()
    {
        var rng = new Random(22);
        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions
        { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

        for (var trial = 0; trial < 25; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 4);
            var optimized = optimizer.OptimizeExpression(expression, options).Optimized;
            var left = RandomExpressions.Parse(expression);
            var right = RandomExpressions.Parse(optimized);

            var byTable = TruthTable.AreEquivalent(left, right);
            var byBdd = BinaryDecisionDiagram.AreEquivalent(left, right);
            var bySat = EquivalenceChecker.Check(left, right).AreEquivalent;

            Assert.True(byTable, $"Trial {trial}: truth table says non-equivalent for '{expression}'");
            Assert.True(byBdd == true, $"Trial {trial}: BDD disagrees with truth table for '{expression}'");
            Assert.True(bySat == true, $"Trial {trial}: SAT miter disagrees with truth table for '{expression}'");
        }
    }

    [Fact]
    public void ThreeEquivalenceEngines_AgreeOnPerturbedPairs()
    {
        // Flip exactly one minterm: ALL engines must detect non-equivalence
        var rng = new Random(33);
        for (var trial = 0; trial < 25; trial++)
        {
            var (variables, onSet) = RandomFunction(rng, rng.Next(2, 7));
            var flipped = new HashSet<int>(onSet);
            var target = rng.Next(1 << variables.Count);
            if (!flipped.Remove(target)) flipped.Add(target);

            var left = SopOf(variables, onSet);
            var right = SopOf(variables, flipped);

            Assert.False(TruthTable.AreEquivalent(left, right),
                $"Trial {trial}: truth table missed a flipped minterm");
            Assert.True(BinaryDecisionDiagram.AreEquivalent(left, right) == false,
                $"Trial {trial}: BDD missed a flipped minterm");
            Assert.True(EquivalenceChecker.Check(left, right).AreEquivalent == false,
                $"Trial {trial}: SAT miter missed a flipped minterm");
        }
    }

    [Fact]
    public void ModelCounting_BddVersusBruteForceVersusEnumeration()
    {
        var rng = new Random(44);
        for (var trial = 0; trial < 25; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 4, allowConstants: true);
            var ast = RandomExpressions.Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var bruteForce = RandomExpressions.CountModels(ast, variables);

            var bdd = BinaryDecisionDiagram.Build(ast);
            var root = bdd.FromAst(ast);
            var byBdd = bdd.CountSatisfyingAssignments(root);

            // SAT enumeration counts models over the formula's OWN variables; scale up
            // to the full variable list if constants removed some of them
            var ownVariables = ast.GetVariables().Count;
            var bySatRaw = FormulaAnalysis.EnumerateModels(ast).Count();
            var bySat = new BigInteger(bySatRaw) * BigInteger.Pow(2, variables.Count - ownVariables);

            Assert.Equal(new BigInteger(bruteForce), byBdd);
            Assert.Equal(new BigInteger(bruteForce), bySat);
        }
    }

    [Fact]
    public void Backbone_SatEngineVersusBruteForce()
    {
        var rng = new Random(55);
        for (var trial = 0; trial < 25; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 4, 3);
            var ast = RandomExpressions.Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            // Brute-force backbone: value each variable takes in ALL models
            var models = new List<Dictionary<string, bool>>();
            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                if (TruthTable.Evaluate(ast, assignment))
                    models.Add(new Dictionary<string, bool>(assignment));
            }

            var result = FormulaAnalysis.ComputeBackbone(ast);

            if (models.Count == 0)
            {
                Assert.False(result.IsSatisfiable);
                continue;
            }

            Assert.True(result.IsSatisfiable);
            var expected = variables
                .Where(v => models.All(model => model[v] == models[0][v]))
                .ToDictionary(v => v, v => models[0][v]);

            Assert.Equal(expected.Count, result.ForcedVariables!.Count);
            foreach (var (variable, value) in expected)
                Assert.True(result.ForcedVariables.TryGetValue(variable, out var got) && got == value,
                    $"Trial {trial}: backbone mismatch on {variable} for '{expression}'");
        }
    }

    [Fact]
    public void SatCoverMinimizer_AgreesWithQuineMcCluskey()
    {
        // Independent minimizers: SAT-based prime cover vs QM must produce equivalent
        // covers; the SAT cover must never beat the PROVEN minimal QM cost
        var rng = new Random(66);
        for (var trial = 0; trial < 15; trial++)
        {
            var (variables, onSet) = RandomFunction(rng, rng.Next(3, 7));
            if (onSet.Count == 0) continue;

            var (qm, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);
            var sat = SatTwoLevelMinimizer.TryMinimize(SopOf(variables, onSet), 256, 20_000);
            Assert.NotNull(sat);

            Assert.True(TruthTable.AreEquivalent(qm, sat!),
                $"Trial {trial}: SAT cover is not equivalent to QM cover");

            if (proven)
                Assert.True(AstMetrics.CountLiterals(sat!) >= AstMetrics.CountLiterals(qm),
                    $"Trial {trial}: SAT cover beats a PROVEN minimal cover — QM proof is wrong");
        }
    }

    [Fact]
    public void FourRepresentations_AgreeOnRandomExtendedFormulas()
    {
        // AIG, BDD, FormulaFactory import and the truth-table oracle over the FULL
        // operator set (Xor/Eqv/Imp/Nand/Nor included) — built directly as ASTs since
        // the text grammar has no syntax for them
        var rng = new Random(88);
        for (var trial = 0; trial < 30; trial++)
        {
            var ast = RandomExtendedAst(rng, 4, 3);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var aig = AndInverterGraph.FromAst(ast);
            var bdd = BinaryDecisionDiagram.Build(ast);
            var imported = new FormulaFactory().Import(ast);

            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                var oracle = TruthTable.Evaluate(ast, assignment);
                Assert.True(aig.Evaluate(aig.Root, assignment) == oracle, $"Trial {trial}: AIG at {m}");
                Assert.True(bdd.Evaluate(bdd.Root, assignment) == oracle, $"Trial {trial}: BDD at {m}");
                Assert.True(TruthTable.Evaluate(imported, assignment) == oracle,
                    $"Trial {trial}: factory at {m}");
            }
        }
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
    public void EspressoAndSubcircuitRewrites_AgreeWithExactMinimumOnCost()
    {
        // Three independent size reducers on the same function: espresso and the
        // subcircuit pass must never beat a PROVEN exact minimum
        var rng = new Random(99);
        for (var trial = 0; trial < 20; trial++)
        {
            var (variables, onSet) = RandomFunction(rng, rng.Next(2, 6));
            if (onSet.Count == 0) continue;
            var canonical = SopOf(variables, onSet);

            var (optimum, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);
            if (!proven) continue;
            var optimumCost = AstMetrics.CountLiterals(optimum);

            var espresso = Transformations.MinimizeDnfHeuristic(canonical);
            Assert.True(TruthTable.AreEquivalent(canonical, espresso));
            Assert.True(AstMetrics.CountLiterals(espresso) >= optimumCost,
                $"Trial {trial}: espresso beat a proven minimum");

            var subcircuit = SubcircuitLibrary.RewriteSubcircuits(canonical);
            Assert.True(TruthTable.AreEquivalent(canonical, subcircuit));
            // The library may legitimately BEAT the minimal SOP by choosing the
            // minimal POS form, so ≤ (not ==) against the SOP optimum
            if (variables.Count <= 3)
                Assert.True(AstMetrics.CountLiterals(subcircuit) <= optimumCost,
                    $"Trial {trial}: ≤3-var subcircuit rewrite missed the exact minimum");
        }
    }

    [Fact]
    public void TseitinTransformation_IsEquisatisfiableWithBruteForce()
    {
        var rng = new Random(77);
        for (var trial = 0; trial < 30; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 4, allowConstants: true);
            var ast = RandomExpressions.Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var satisfiableByOracle = RandomExpressions.CountModels(ast, variables) > 0
                                      || (variables.Count == 0 && TruthTable.Evaluate(ast, new Dictionary<string, bool>()));

            var solver = SatSolver.FromCnf(TseitinConverter.Convert(ast));
            var bySolver = solver.Solve();

            Assert.Equal(satisfiableByOracle ? SatResult.Satisfiable : SatResult.Unsatisfiable, bySolver);
        }
    }
}
