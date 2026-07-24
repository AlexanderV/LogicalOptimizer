using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Metamorphic testing: no output oracle is needed — instead each test applies a
///     semantics-preserving transformation to the INPUT and asserts the documented
///     relation between the two OUTPUTS. Seeds are fixed so every failure reproduces.
/// </summary>
public class MetamorphicTests
{
    private static readonly OptimizationOptions ResultOnly = new()
    { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

    private static string Optimize(string expression)
    {
        return new BooleanExpressionOptimizer().OptimizeExpression(expression, ResultOnly).Optimized;
    }

    [Fact]
    public void Renaming_CommutesWithOptimization()
    {
        // MR: optimize(rename(F)) ≡ rename(optimize(F)) — variable identity carries no meaning
        var rng = new Random(101);
        for (var trial = 0; trial < 40; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 4);
            var ast = RandomExpressions.Parse(expression);
            var map = ast.GetVariables().ToDictionary(v => v, v => "renamed_" + v);

            var renamedFirst = Optimize(RandomExpressions.Rename(ast, map).ToString());
            var optimizedFirst = RandomExpressions.Rename(
                RandomExpressions.Parse(Optimize(expression)), map);

            Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse(renamedFirst), optimizedFirst),
                $"Trial {trial}: renaming does not commute for '{expression}'");
        }
    }

    [Fact]
    public void Negation_CommutesWithOptimization()
    {
        // MR: optimize(!F) ≡ !optimize(F)
        var rng = new Random(202);
        for (var trial = 0; trial < 40; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 4);

            var negatedFirst = Optimize($"!({expression})");
            var optimizedFirst = $"!({Optimize(expression)})";

            Assert.True(TruthTable.AreEquivalent(negatedFirst, optimizedFirst),
                $"Trial {trial}: negation does not commute for '{expression}'");
        }
    }

    [Fact]
    public void ConstantSubstitution_CommutesWithOptimization()
    {
        // MR (cofactor): optimize(F)|x=c ≡ optimize(F|x=c)
        var rng = new Random(303);
        for (var trial = 0; trial < 40; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 4);
            var ast = RandomExpressions.Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var pinned = variables[rng.Next(variables.Count)];
            var value = rng.Next(2) == 0;

            var substitutedFirst = Optimize(RandomExpressions.Substitute(ast, pinned, value).ToString());
            var optimizedFirst = RandomExpressions.Substitute(
                RandomExpressions.Parse(Optimize(expression)), pinned, value);

            Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse(substitutedFirst), optimizedFirst),
                $"Trial {trial}: substituting {pinned}={value} does not commute for '{expression}'");
        }
    }

    [Fact]
    public void DisjunctionComposition_PreservesSemantics()
    {
        // MR: optimize(F | G) ≡ optimize(F) | optimize(G)
        var rng = new Random(404);
        for (var trial = 0; trial < 30; trial++)
        {
            var left = RandomExpressions.Generate(rng, 4, 3);
            var right = RandomExpressions.Generate(rng, 4, 3);

            var togetherFirst = Optimize($"({left}) | ({right})");
            var separately = $"({Optimize(left)}) | ({Optimize(right)})";

            Assert.True(TruthTable.AreEquivalent(togetherFirst, separately),
                $"Trial {trial}: disjunction composition broken for '{left}' | '{right}'");
        }
    }

    [Fact]
    public void TermPermutation_PreservesSemanticsAndExactCost()
    {
        // MR: shuffling top-level OR terms must not change semantics, and inside the
        // exact zone the two-level cover cost (literal count) must be identical
        var rng = new Random(505);
        var optimizer = new BooleanExpressionOptimizer();
        for (var trial = 0; trial < 25; trial++)
        {
            var terms = Enumerable.Range(0, rng.Next(3, 7))
                .Select(_ => "(" + RandomExpressions.Generate(rng, 5, 2) + ")")
                .ToList();
            var original = string.Join(" | ", terms);
            var shuffled = string.Join(" | ", terms.OrderBy(_ => rng.Next()));

            var first = optimizer.OptimizeExpression(original, ResultOnly);
            var second = optimizer.OptimizeExpression(shuffled, ResultOnly);

            Assert.True(TruthTable.AreEquivalent(first.Optimized, second.Optimized),
                $"Trial {trial}: permutation changed semantics of '{original}'");

            if (first.MinimizationStatus == MinimizationStatus.MinimalProven &&
                second.MinimizationStatus == MinimizationStatus.MinimalProven)
            {
                var costFirst = AstMetrics.CountLiterals(RandomExpressions.Parse(first.Optimized));
                var costSecond = AstMetrics.CountLiterals(RandomExpressions.Parse(second.Optimized));
                Assert.True(costFirst == costSecond,
                    $"Trial {trial}: proven-minimal costs differ ({costFirst} vs {costSecond}) for permuted '{original}'");
            }
        }
    }

    [Fact]
    public void Reoptimization_IsIdempotentInCost()
    {
        // MR: optimize(optimize(F)) must not find further improvement worth reporting
        var rng = new Random(606);
        for (var trial = 0; trial < 40; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 6, 4, allowConstants: true);

            var once = Optimize(expression);
            var twice = Optimize(once);

            Assert.True(TruthTable.AreEquivalent(once, twice),
                $"Trial {trial}: re-optimization changed semantics of '{expression}'");
            Assert.True(
                AstMetrics.CountLiterals(RandomExpressions.Parse(twice)) <=
                AstMetrics.CountLiterals(RandomExpressions.Parse(once)),
                $"Trial {trial}: re-optimization made '{once}' worse: '{twice}'");
        }
    }

    [Fact]
    public void DualityPrinciple_HoldsThroughOptimizer()
    {
        // MR (De Morgan duality): optimize(dual(F)) ≡ dual(optimize(F)) where dual swaps
        // &/| and negates every literal — a global structural relation, not a local rule
        var rng = new Random(707);
        for (var trial = 0; trial < 30; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, 3);
            var ast = RandomExpressions.Parse(expression);

            var dualFirst = Optimize(Dual(ast).ToString());
            var optimizedFirst = Dual(RandomExpressions.Parse(Optimize(expression)));

            Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse(dualFirst), optimizedFirst),
                $"Trial {trial}: duality broken for '{expression}'");
        }
    }

    [Fact]
    public void Espresso_TermPermutation_PreservesSemanticsAndCost()
    {
        // MR: the cube-list minimizer must not care about input term order
        var rng = new Random(808);
        for (var trial = 0; trial < 25; trial++)
        {
            var terms = Enumerable.Range(0, rng.Next(3, 8))
                .Select(_ => $"{(rng.Next(2) == 0 ? "!" : "")}v{rng.Next(5)} & v{rng.Next(5)}")
                .ToList();
            var original = string.Join(" | ", terms);
            var shuffled = string.Join(" | ", terms.OrderBy(_ => rng.Next()));

            var a = Transformations.MinimizeDnfHeuristic(RandomExpressions.Parse(original));
            var b = Transformations.MinimizeDnfHeuristic(RandomExpressions.Parse(shuffled));

            Assert.True(TruthTable.AreEquivalent(a, b),
                $"Trial {trial}: permutation changed espresso semantics for '{original}'");
            Assert.True(AstMetrics.CountLiterals(a) == AstMetrics.CountLiterals(b),
                $"Trial {trial}: permutation changed espresso cost for '{original}'");
        }
    }

    [Fact]
    public void Espresso_Renaming_CommutesWithMinimization()
    {
        var rng = new Random(818);
        for (var trial = 0; trial < 20; trial++)
        {
            var sop = string.Join(" | ", Enumerable.Range(0, rng.Next(2, 7))
                .Select(_ => $"v{rng.Next(4)} & {(rng.Next(2) == 0 ? "!" : "")}v{rng.Next(4)}"));
            var ast = RandomExpressions.Parse(sop);
            if (ast.GetVariables().Count == 0) continue;
            var map = ast.GetVariables().ToDictionary(v => v, v => "r_" + v);

            var renamedFirst = Transformations.MinimizeDnfHeuristic(RandomExpressions.Rename(ast, map));
            var minimizedFirst = RandomExpressions.Rename(Transformations.MinimizeDnfHeuristic(ast), map);

            Assert.True(TruthTable.AreEquivalent(renamedFirst, minimizedFirst),
                $"Trial {trial}: renaming does not commute with espresso for '{sop}'");
        }
    }

    [Fact]
    public void BddQuantifierOrder_ExistsCommutes()
    {
        // MR: ∃x.∃y.f and ∃y.∃x.f are the SAME canonical node
        var rng = new Random(828);
        for (var trial = 0; trial < 25; trial++)
        {
            var ast = RandomExpressions.Parse(RandomExpressions.Generate(rng, 4, 3));
            var bdd = BinaryDecisionDiagram.Build(ast);
            var variables = bdd.Variables;
            if (variables.Count < 2) continue;
            var x = variables[0];
            var y = variables[1];

            Assert.Equal(
                bdd.Exists(bdd.Exists(bdd.Root, x), y),
                bdd.Exists(bdd.Exists(bdd.Root, y), x));
        }
    }

    [Fact]
    public void AigStructure_IsInvariantUnderIdempotentDuplication()
    {
        // MR: F and F | F (and F & F) hash to identical graphs
        var rng = new Random(838);
        for (var trial = 0; trial < 25; trial++)
        {
            var ast = RandomExpressions.Parse(RandomExpressions.Generate(rng, 4, 3));
            var plain = AndInverterGraph.FromAst(ast);
            var duplicated = AndInverterGraph.FromAst(new OrNode(ast, ast));
            Assert.Equal(plain.AndNodeCount, duplicated.AndNodeCount);
        }
    }

    /// <summary>dual(F) = !F with every literal re-negated: swap &amp;/|, 0/1, and negate variables.</summary>
    private static AstNode Dual(AstNode node)
    {
        return node switch
        {
            VariableNode v => new NotNode(v),
            ConstantNode c => new ConstantNode(!c.Value),
            NotNode { Operand: VariableNode inner } => inner,
            NotNode n => new NotNode(Dual(n.Operand)),
            AndNode a => new OrNode(Dual(a.Left), Dual(a.Right)),
            OrNode o => new AndNode(Dual(o.Left), Dual(o.Right)),
            _ => throw new InvalidOperationException($"Unexpected node type {node.GetType().Name}")
        };
    }
}
