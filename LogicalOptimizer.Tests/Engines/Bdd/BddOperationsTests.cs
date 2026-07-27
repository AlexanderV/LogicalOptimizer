using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for BDD quantification, restriction, composition and variable-order
///     selection. Every operation is checked against a brute-force semantic oracle.
/// </summary>
public class BddOperationsTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static Dictionary<string, bool> Assignment(IReadOnlyList<string> variables, int mask)
    {
        var assignment = new Dictionary<string, bool>();
        for (var j = 0; j < variables.Count; j++)
            assignment[variables[j]] = (mask & (1 << j)) != 0;
        return assignment;
    }

    [Fact]
    public void Restrict_MatchesSemanticSubstitution()
    {
        var ast = Parse("a & b | !a & c | b & !c");
        var bdd = BinaryDecisionDiagram.Build(ast);
        var variables = bdd.Variables;

        foreach (var pinned in new[] { "a", "b", "c" })
            foreach (var value in new[] { false, true })
            {
                var restricted = bdd.Restrict(bdd.Root, pinned, value);
                for (var m = 0; m < 1 << variables.Count; m++)
                {
                    var assignment = Assignment(variables, m);
                    var expected = TruthTable.Evaluate(ast,
                        new Dictionary<string, bool>(assignment) { [pinned] = value });
                    Assert.True(bdd.Evaluate(restricted, assignment) == expected,
                        $"Restrict({pinned}={value}) wrong at mask {m}");
                }
            }
    }

    [Fact]
    public void ExistsAndForAll_MatchBruteForceQuantification()
    {
        var rng = new Random(515);
        for (var trial = 0; trial < 40; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 4, 3);
            var ast = Parse(expression);
            var bdd = BinaryDecisionDiagram.Build(ast);
            var variables = bdd.Variables;
            if (variables.Count == 0) continue;

            var x = variables[rng.Next(variables.Count)];
            var exists = bdd.Exists(bdd.Root, x);
            var forAll = bdd.ForAll(bdd.Root, x);

            for (var m = 0; m < 1 << variables.Count; m++)
            {
                var assignment = Assignment(variables, m);
                var low = TruthTable.Evaluate(ast, new Dictionary<string, bool>(assignment) { [x] = false });
                var high = TruthTable.Evaluate(ast, new Dictionary<string, bool>(assignment) { [x] = true });
                Assert.True(bdd.Evaluate(exists, assignment) == (low || high),
                    $"Trial {trial}: Exists({x}) wrong for '{expression}'");
                Assert.True(bdd.Evaluate(forAll, assignment) == (low && high),
                    $"Trial {trial}: ForAll({x}) wrong for '{expression}'");
            }
        }
    }

    [Fact]
    public void Exists_MultipleVariables_ProjectsFunction()
    {
        // ∃b,c. (a & b | c) is satisfiable for any a => projection is constant 1
        var ast = Parse("a & b | c");
        var bdd = BinaryDecisionDiagram.Build(ast);
        Assert.True(bdd.IsTautology(bdd.Exists(bdd.Root, "b", "c")));

        // ∀b,c. (a & b | c) is falsified at b=0,c=0 for every a => constant 0
        Assert.True(bdd.IsContradiction(bdd.ForAll(bdd.Root, "b", "c")));

        // ∀b. (a & b | c) = (c) & (a | c) = c — quantifying one variable keeps the rest
        var forAllB = bdd.ForAll(bdd.Root, "b");
        Assert.Equal(bdd.FromAst(new VariableNode("c")), forAllB);
    }

    [Fact]
    public void Compose_MatchesSemanticSubstitution()
    {
        var f = Parse("x & y | !x & z");
        var g = Parse("y | z");
        var variables = new[] { "x", "y", "z" };
        var bdd = new BinaryDecisionDiagram(variables);
        var composed = bdd.Compose(bdd.FromAst(f), "x", bdd.FromAst(g));

        for (var m = 0; m < 8; m++)
        {
            var assignment = Assignment(variables, m);
            var gValue = TruthTable.Evaluate(g, assignment);
            var expected = TruthTable.Evaluate(f,
                new Dictionary<string, bool>(assignment) { ["x"] = gValue });
            Assert.True(bdd.Evaluate(composed, assignment) == expected,
                $"Compose wrong at mask {m}");
        }
    }

    [Fact]
    public void Restrict_UnknownVariable_Throws()
    {
        var bdd = BinaryDecisionDiagram.Build(Parse("a & b"));
        Assert.Throws<ArgumentException>(() => bdd.Restrict(bdd.Root, "nope", true));
    }

    [Fact]
    public void BuildWithBestOrder_BeatsSortedOrderOnInterleavingSensitiveFunction()
    {
        // The classic order-sensitive shape: pairwise products. Appearance order
        // (a1,b1,a2,b2,...) is linear; sorted order (a1..a6,b1..b6) is exponential
        var expression = string.Join(" | ", Enumerable.Range(1, 6).Select(i => $"a{i} & b{i}"));
        var ast = Parse(expression);

        var best = BinaryDecisionDiagram.BuildWithBestOrder(ast);
        var sorted = BinaryDecisionDiagram.Build(ast);

        Assert.True(best.NodeCount < sorted.NodeCount / 3,
            $"Best order gave {best.NodeCount} nodes vs sorted {sorted.NodeCount} — no real improvement");

        // Semantics must be identical regardless of order
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            var assignment = Assignment(variables, m);
            Assert.True(best.Evaluate(best.Root, assignment) == TruthTable.Evaluate(ast, assignment),
                $"BuildWithBestOrder changed semantics at mask {m}");
        }
    }

    [Fact]
    public void BuildWithSiftedOrder_NeverWorseThanStaticHeuristicsAndStaysSound()
    {
        // A shuffled pairwise-product function: appearance order is scrambled, sorted
        // order interleaves badly — sifting has to reconstruct the pairing
        var rng = new Random(989);
        var pairs = Enumerable.Range(1, 5).Select(i => ($"a{i}", $"b{i}")).ToList();
        var terms = pairs.Select(p => $"{p.Item1} & {p.Item2}").OrderBy(_ => rng.Next()).ToList();
        // Scramble textual appearance by rewriting each pair in random member order
        var expression = string.Join(" | ", terms);
        var ast = Parse(expression);

        var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast);
        var staticBest = BinaryDecisionDiagram.BuildWithBestOrder(ast);

        Assert.True(sifted.NodeCount <= staticBest.NodeCount,
            $"Sifting ({sifted.NodeCount} nodes) lost to static heuristics ({staticBest.NodeCount})");

        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            var assignment = Assignment(variables, m);
            Assert.True(sifted.Evaluate(sifted.Root, assignment) == TruthTable.Evaluate(ast, assignment),
                $"Sifted BDD changed semantics at mask {m}");
        }
    }

    [Fact]
    public void BuildWithSiftedOrder_BeatsStaticHeuristicsOnAdversarialOrder()
    {
        // Pairing (a_i with b_i) hidden from all three static heuristics: a leading
        // all-a's term registers a1..a6 first, so appearance order = a1..a6,b1..b6
        // (pairs split into blocks), sorted order is the same, and reversed appearance
        // is the same blocks backwards. Only per-variable sifting can migrate each
        // b_i next to its a_i. Raw construction: parsing would canonically sort the
        // OR operands (pair terms before the long preamble), interleaving a_i and b_i
        // in appearance order and handing the static heuristics the good order for free.
        var preamble = new AndNode(Enumerable.Range(1, 6)
            .Select(i => (AstNode)new VariableNode($"a{i}")).ToList());
        var pairTerms = Enumerable.Range(1, 6)
            .Select(i => (AstNode)new AndNode(new VariableNode($"a{i}"), new VariableNode($"b{i}")));
        var ast = new OrNode(new AstNode[] { preamble }.Concat(pairTerms).ToList());

        var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast);
        var staticBest = BinaryDecisionDiagram.BuildWithBestOrder(ast);

        Assert.True(sifted.NodeCount < staticBest.NodeCount,
            $"Sifting found nothing: {sifted.NodeCount} vs static {staticBest.NodeCount}");
    }

    [Fact]
    public void BuildWithBestOrder_TinyBudget_ThrowsWhenNoOrderFits()
    {
        var expression = string.Join(" | ", Enumerable.Range(1, 6).Select(i => $"a{i} & b{i}"));
        Assert.Throws<InvalidOperationException>(() =>
            BinaryDecisionDiagram.BuildWithBestOrder(Parse(expression), nodeBudget: 3));
    }
}
