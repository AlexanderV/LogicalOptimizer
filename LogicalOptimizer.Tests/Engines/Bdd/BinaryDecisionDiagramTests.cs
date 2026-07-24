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
    [InlineData("a | !a", 2)] // tautology over one variable
    public void CountSatisfyingAssignments_KnownFunctions(string expression, int expected)
    {
        var bdd = BinaryDecisionDiagram.Build(Parse(expression));
        Assert.Equal(new BigInteger(expected), bdd.CountSatisfyingAssignments(bdd.Root));
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

        Assert.Throws<InvalidOperationException>(() =>
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
