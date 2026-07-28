using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

public class BinaryDecisionDiagramTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    [Theory]
    [InlineData("a & b | a & !b", "a")]
    [InlineData("!(a & b)", "!a | !b")]
    [InlineData("(a | b) & (a | c)", "a | b & c")]
    [InlineData("a & b | a & c | b & c", "a & b | c & (a | b)")]
    public void AreEquivalent_EquivalentPairs_SameCanonicalNode(string left, string right)
    {
        Assert.True(BinaryDecisionDiagram.AreEquivalent(Parse(left), Parse(right)));
    }

    [Theory]
    [InlineData("a | b", "a & b")]
    [InlineData("a", "!a")]
    public void AreEquivalent_DifferentFunctions_False(string left, string right)
    {
        Assert.False(BinaryDecisionDiagram.AreEquivalent(Parse(left), Parse(right)));
    }

    [Fact]
    public void Build_TautologyAndContradiction_Terminal()
    {
        var tautology = BinaryDecisionDiagram.Build(Parse("a | !a"));
        Assert.True(tautology.IsTautology(tautology.Root));

        var contradiction = BinaryDecisionDiagram.Build(Parse("a & !a & (b | c)"));
        Assert.True(contradiction.IsContradiction(contradiction.Root));
    }

    [Theory]
    [InlineData("a & b | a & c | b & c", 4)] // majority of 3: 4 of 8
    [InlineData("a & !b | !a & b", 2)] // xor: 2 of 4
    [InlineData("a", 1)] // single variable: 1 of 2
    public void CountSatisfyingAssignments_KnownFunctions(string expression, int expected)
    {
        var bdd = BinaryDecisionDiagram.Build(Parse(expression));
        Assert.Equal(new BigInteger(expected), bdd.CountSatisfyingAssignments(bdd.Root));
    }

    [Fact]
    public void CountSatisfyingAssignments_TautologyOverOneVariable()
    {
        // Raw construction: parsing "a | !a" folds to constant 1 at build time and the
        // variable would vanish from the BDD's support (counting would give 2^0 = 1)
        var a = new VariableNode("a");
        var bdd = BinaryDecisionDiagram.Build(new OrNode(a, new NotNode(a)));

        Assert.Equal(new BigInteger(2), bdd.CountSatisfyingAssignments(bdd.Root));
    }

    [Fact]
    public void CountSatisfyingAssignments_MatchesTruthTableEnumeration()
    {
        var random = new Random(90210);
        var variables = new[] { "a", "b", "c", "d", "e" };

        for (var i = 0; i < 100; i++)
        {
            var expression = RandomExpression(random, variables, 4);
            var ast = Parse(expression);
            var ordered = ast.GetVariables().OrderBy(v => v).ToList();

            var expected = 0;
            var assignment = new Dictionary<string, bool>();
            for (var mask = 0; mask < 1 << ordered.Count; mask++)
            {
                for (var j = 0; j < ordered.Count; j++)
                    assignment[ordered[j]] = (mask & (1 << j)) != 0;
                if (TruthTable.Evaluate(ast, assignment)) expected++;
            }

            var bdd = BinaryDecisionDiagram.Build(ast);
            Assert.True(new BigInteger(expected) == bdd.CountSatisfyingAssignments(bdd.Root),
                $"Model count mismatch for '{expression}'");
        }
    }

    [Fact]
    public void Evaluate_AgreesWithTruthTable()
    {
        var ast = Parse("(a | !b) & (c | a & b)");
        var bdd = BinaryDecisionDiagram.Build(ast);
        var ordered = ast.GetVariables().OrderBy(v => v).ToList();
        var assignment = new Dictionary<string, bool>();

        for (var mask = 0; mask < 1 << ordered.Count; mask++)
        {
            for (var j = 0; j < ordered.Count; j++)
                assignment[ordered[j]] = (mask & (1 << j)) != 0;
            Assert.Equal(TruthTable.Evaluate(ast, assignment), bdd.Evaluate(bdd.Root, assignment));
        }
    }

    [Fact]
    public void AreEquivalent_ThirtyVariables_CanonicalAndFast()
    {
        // Two syntactically distant forms of the same 30-variable function
        var names = Enumerable.Range(1, 30).Select(i => $"v{i:D2}").ToList();
        var conjunction = string.Join(" & ", names);
        var negatedDisjunction = "!(" + string.Join(" | ", names.Select(n => $"!{n}")) + ")";

        Assert.True(BinaryDecisionDiagram.AreEquivalent(Parse(conjunction), Parse(negatedDisjunction)));
    }

    [Fact]
    public void CountSatisfyingAssignments_ThirtyVariableConjunction_ExactlyOne()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"v{i:D2}");
        var bdd = BinaryDecisionDiagram.Build(Parse(string.Join(" & ", names)));

        Assert.Equal(BigInteger.One, bdd.CountSatisfyingAssignments(bdd.Root));
    }

    [Fact]
    public void NodeBudget_Exceeded_ThrowsOrReturnsNull()
    {
        // Hidden-weighted-bit-style blowup is hard to force cheaply; a tiny budget suffices
        var expression = string.Join(" | ", Enumerable.Range(1, 8).Select(i => $"a{i} & b{i}"));

        Assert.Throws<NodeBudgetExceededException>(() =>
            BinaryDecisionDiagram.Build(Parse(expression), nodeBudget: 8));
        Assert.Null(BinaryDecisionDiagram.AreEquivalent(Parse(expression), Parse(expression + " | a1 & b1"),
            nodeBudget: 8));
    }

    [Fact]
    public void EnumerateSatisfyingAssignments_MatchesTruthTable()
    {
        var random = new Random(4096);
        var variables = new[] { "a", "b", "c", "d" };

        for (var i = 0; i < 40; i++)
        {
            var ast = Parse(RandomExpression(random, variables, 3));
            var bdd = BinaryDecisionDiagram.Build(ast);
            var ordered = ast.GetVariables().OrderBy(v => v).ToList();

            var expected = new HashSet<string>();
            var assignment = new Dictionary<string, bool>();
            for (var mask = 0; mask < 1 << ordered.Count; mask++)
            {
                for (var j = 0; j < ordered.Count; j++)
                    assignment[ordered[j]] = (mask & (1 << j)) != 0;
                if (TruthTable.Evaluate(ast, assignment))
                    expected.Add(string.Join(",", ordered.Select(v => $"{v}={assignment[v]}")));
            }

            var actual = bdd.EnumerateSatisfyingAssignments(bdd.Root)
                .Select(m => string.Join(",", m.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")))
                .ToHashSet();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void SharedManager_RepeatedQueriesAgainstBaseline()
    {
        // The intended usage pattern: one manager, one baseline, many candidate checks
        var baselineAst = Parse("a & b | c & d");
        var manager = new BinaryDecisionDiagram(baselineAst.GetVariables());
        var baseline = manager.FromAst(baselineAst);

        Assert.Equal(baseline, manager.FromAst(Parse("(a | c) & (a | d) & (b | c) & (b | d)")));
        Assert.NotEqual(baseline, manager.FromAst(Parse("a & b | c & !d")));
    }

    [Theory]
    [InlineData("a & b | c & !d")]
    [InlineData("(a | b) & (c | !a)")]
    [InlineData("a & !b | !a & b")]
    [InlineData("a & b & c & d")]
    public void Negate_IsStructural_FunctionAndComplementShareTheSameNode(string expression)
    {
        // Complement edges: !f differs from f only by the edge's complement bit, so both
        // resolve to the SAME regular node. Negate is a pure bit flip — no new nodes.
        var ast = Parse(expression);
        var manager = new BinaryDecisionDiagram(ast.GetVariables());

        var f = manager.FromAst(ast);
        var nodesAfterF = manager.NodeCount;
        var notF = manager.Negate(f);
        var nodesAfterNegate = manager.NodeCount;

        // Structural win: negation allocated zero additional nodes.
        Assert.Equal(nodesAfterF, nodesAfterNegate);
        // f and !f are the same node up to the complement bit.
        Assert.Equal(BinaryDecisionDiagram.Regular(f), BinaryDecisionDiagram.Regular(notF));
        Assert.NotEqual(f, notF);
        Assert.True(BinaryDecisionDiagram.IsComplemented(f) != BinaryDecisionDiagram.IsComplemented(notF));

        // Double negation is identity at the handle level (O(1), no structure change).
        Assert.Equal(f, manager.Negate(notF));
    }

    [Fact]
    public void FromAst_NegationOfFormula_EqualsComplementEdgeOfFormula()
    {
        // Building !f from its own AST must land on exactly Complement(f) — canonicity of
        // the complement bit, not merely of the node.
        var ast = Parse("a & b | !a & c");
        var manager = new BinaryDecisionDiagram(ast.GetVariables());

        var f = manager.FromAst(ast);
        var notF = manager.FromAst(new NotNode(ast));

        Assert.Equal(BinaryDecisionDiagram.Complement(f), notF);
        Assert.Equal(BinaryDecisionDiagram.Regular(f), BinaryDecisionDiagram.Regular(notF));
    }

    [Fact]
    public void Canonicity_EquivalentFormulasProduceIdenticalEdgeIncludingComplementBit()
    {
        // Two syntactically different but equivalent forms must build to the byte-identical
        // edge (same node AND same complement bit); a form and its negation must differ by
        // exactly the complement bit.
        var manager = new BinaryDecisionDiagram(new[] { "a", "b" });

        var nand = manager.FromAst(Parse("!(a & b)"));
        var deMorgan = manager.FromAst(Parse("!a | !b"));
        Assert.Equal(nand, deMorgan);

        var and = manager.FromAst(Parse("a & b"));
        Assert.Equal(and, BinaryDecisionDiagram.Complement(nand));
    }

    [Fact]
    public void ModelCount_FunctionPlusComplement_EqualsTwoToTheN()
    {
        // With complement edges the count decode must be exact: |models(f)| + |models(!f)|
        // must equal 2^n for every function, over the full manager variable set.
        var random = new Random(1234);
        var variables = new[] { "a", "b", "c", "d", "e" };

        for (var i = 0; i < 60; i++)
        {
            var ast = Parse(RandomExpression(random, variables, 4));
            var manager = new BinaryDecisionDiagram(variables);
            var f = manager.FromAst(ast);
            var notF = manager.Negate(f);

            var total = manager.CountSatisfyingAssignments(f) + manager.CountSatisfyingAssignments(notF);
            Assert.Equal(BigInteger.Pow(2, variables.Length), total);
        }
    }

    [Fact]
    public void ComplementEdges_RepresentingFunctionAndItsNegationCostsNoExtraNodes()
    {
        // The ~2x memory win, made explicit: importing a large function AND its negation
        // into one manager uses exactly as many nodes as the function alone, because !f
        // reuses every node of f through the complement bit.
        var names = Enumerable.Range(1, 8).Select(i => $"v{i}").ToList();
        var expression = string.Join(" | ", Enumerable.Range(1, 4).Select(i => $"v{2 * i - 1} & v{2 * i}"));
        var ast = Parse(expression);

        var manager = new BinaryDecisionDiagram(names);
        var f = manager.FromAst(ast);
        var nodesForFAlone = manager.NodeCount;

        // Import the negation as a separate AST — no shared object identity to lean on.
        var notF = manager.FromAst(new NotNode(ast));
        var nodesForBoth = manager.NodeCount;

        Assert.Equal(nodesForFAlone, nodesForBoth);
        Assert.Equal(BinaryDecisionDiagram.Regular(f), BinaryDecisionDiagram.Regular(notF));
        // Sanity: f and !f really are different functions but the same shared structure.
        Assert.NotEqual(f, notF);
    }

    private static string RandomExpression(Random random, string[] variables, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            var name = variables[random.Next(variables.Length)];
            return random.Next(2) == 0 ? name : $"!{name}";
        }

        var left = RandomExpression(random, variables, depth - 1);
        var right = RandomExpression(random, variables, depth - 1);
        return random.Next(2) == 0 ? $"({left} & {right})" : $"({left} | {right})";
    }
}
