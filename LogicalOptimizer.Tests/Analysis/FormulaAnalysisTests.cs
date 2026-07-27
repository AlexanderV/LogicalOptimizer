using Xunit;

namespace LogicalOptimizer.Tests;

public class FormulaAnalysisTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    [Fact]
    public void ComputeBackbone_ForcedAndFreeVariables()
    {
        // a is forced true; b free (b | c must hold when a alone satisfies nothing else)
        var result = FormulaAnalysis.ComputeBackbone(Parse("a & (b | c)"));

        Assert.True(result.IsSatisfiable);
        Assert.Equal(new Dictionary<string, bool> { ["a"] = true }, result.ForcedVariables);
    }

    [Fact]
    public void ComputeBackbone_FullyForced()
    {
        var result = FormulaAnalysis.ComputeBackbone(Parse("(a | b) & !a"));

        Assert.True(result.IsSatisfiable);
        Assert.False(result.ForcedVariables!["a"]);
        Assert.True(result.ForcedVariables["b"]);
    }

    [Fact]
    public void ComputeBackbone_Unsatisfiable()
    {
        Assert.False(FormulaAnalysis.ComputeBackbone(Parse("a & !a")).IsSatisfiable);
    }

    [Fact]
    public void ComputeBackbone_ImplicationChainTwentyVariables_AllForced()
    {
        // v1 & (v1->v2) & ... & (v19->v20): every variable is backbone-true, far beyond
        // the truth-table range
        var parts = new List<string> { "v01" };
        for (var i = 1; i < 20; i++)
            parts.Add($"(!v{i:D2} | v{i + 1:D2})");
        var result = FormulaAnalysis.ComputeBackbone(Parse(string.Join(" & ", parts)));

        Assert.True(result.IsSatisfiable);
        Assert.Equal(20, result.ForcedVariables!.Count);
        Assert.All(result.ForcedVariables, kv => Assert.True(kv.Value));
    }

    [Fact]
    public void ComputeBackbone_AgreesWithExhaustiveEnumeration()
    {
        var random = new Random(2024);
        var variables = new[] { "a", "b", "c", "d" };

        for (var i = 0; i < 60; i++)
        {
            var expression = RandomExpression(random, variables, 3);
            var ast = Parse(expression);
            var ordered = ast.GetVariables().OrderBy(v => v).ToList();

            var expected = ExhaustiveBackbone(ast, ordered);
            var actual = FormulaAnalysis.ComputeBackbone(ast);

            if (expected == null)
            {
                Assert.False(actual.IsSatisfiable);
            }
            else
            {
                Assert.True(actual.IsSatisfiable);
                Assert.Equal(expected, actual.ForcedVariables!.OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value));
            }
        }
    }

    private static Dictionary<string, bool>? ExhaustiveBackbone(AstNode ast, List<string> variables)
    {
        var assignment = new Dictionary<string, bool>();
        Dictionary<string, bool>? candidates = null;

        for (var mask = 0; mask < 1 << variables.Count; mask++)
        {
            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (mask & (1 << j)) != 0;
            if (!TruthTable.Evaluate(ast, assignment)) continue;

            if (candidates == null)
            {
                candidates = new Dictionary<string, bool>(assignment);
            }
            else
            {
                foreach (var name in candidates.Keys.ToList())
                    if (candidates[name] != assignment[name])
                        candidates.Remove(name);
            }
        }

        return candidates == null
            ? null
            : candidates.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    [Fact]
    public void EnumerateModels_ProjectedAndComplete()
    {
        var models = FormulaAnalysis.EnumerateModels(Parse("a | b")).ToList();

        Assert.Equal(3, models.Count);
        Assert.All(models, m => Assert.True(m["a"] || m["b"]));
        Assert.Equal(3, models.Select(m => (m["a"], m["b"])).Distinct().Count());
    }

    [Fact]
    public void EnumerateModels_RespectsCapAndLaziness()
    {
        // 30-variable disjunction has ~2^30 models; taking 5 must be instant
        var expression = string.Join(" | ", Enumerable.Range(1, 30).Select(i => $"v{i:D2}"));
        var models = FormulaAnalysis.EnumerateModels(Parse(expression), maxModels: 5).ToList();

        Assert.Equal(5, models.Count);
        Assert.Equal(5, models.Select(m => string.Join(",", m.OrderBy(kv => kv.Key))).Distinct().Count());
    }

    [Fact]
    public void EnumerateModels_UnsatisfiableFormula_Empty()
    {
        Assert.Empty(FormulaAnalysis.EnumerateModels(Parse("a & !a")));
    }

    [Fact]
    public void SimplifyWithBackbone_PropagatesForcedLiterals()
    {
        // Backbone: a forced false, b forced true; c and d stay free. The contract of
        // SimplifyWithBackbone is: substitute forced variables by constants, fold, then
        // re-attach the forced literals (sorted by name) as a prefix of the AND chain —
        // so the residual core must not mention a or b at all
        var input = Parse("(a | b) & !a & (c | d)");
        var simplified = FormulaAnalysis.SimplifyWithBackbone(input);

        Assert.True(EquivalenceChecker.Check(input, simplified).AreEquivalent);
        Assert.True(AstMetrics.CountLiterals(simplified) <= AstMetrics.CountLiterals(input));

        var forced = new Dictionary<string, bool>();
        var coreOperands = new List<AstNode>();
        foreach (var operand in Assert.IsType<AndNode>(simplified).Operands)
            if (IsLiteral(operand, out var name, out var value) && (name == "a" || name == "b"))
                forced[name] = value;
            else
                coreOperands.Add(operand);

        Assert.Equal(new Dictionary<string, bool> { ["a"] = false, ["b"] = true }, forced);
        var coreVariables = new HashSet<string>(coreOperands.SelectMany(o => o.GetVariables()));
        Assert.DoesNotContain("a", coreVariables);
        Assert.DoesNotContain("b", coreVariables);
        Assert.Equal(new HashSet<string> { "c", "d" }, coreVariables);
    }

    private static bool IsLiteral(AstNode node, out string name, out bool value)
    {
        switch (node)
        {
            case VariableNode variable:
                name = variable.Name;
                value = true;
                return true;
            case NotNode { Operand: VariableNode negated }:
                name = negated.Name;
                value = false;
                return true;
            default:
                name = "";
                value = false;
                return false;
        }
    }

    [Fact]
    public void SimplifyWithBackbone_UnsatisfiableBecomesFalse()
    {
        Assert.Equal("0", FormulaAnalysis.SimplifyWithBackbone(Parse("(a | b) & !a & !b")).ToString());
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
