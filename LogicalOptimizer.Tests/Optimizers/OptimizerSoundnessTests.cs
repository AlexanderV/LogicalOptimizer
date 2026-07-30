using LogicalOptimizer.Rewrite;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Soundness regression tests: optimization must never change the function.
///     Guards against unsound rewrite rules, e.g. the simultaneous consensus-removal
///     bug that silently dropped minterms of XOR-shaped functions.
///     Tests assert both logical equivalence AND that the pipeline's soundness guard
///     did not fire - rules must be sound on their own, the guard is a last resort.
/// </summary>
public class OptimizerSoundnessTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static string CanonicalDnf(int variableCount, int truthBits, string[] variables)
    {
        var terms = new List<string>();
        for (var minterm = 0; minterm < 1 << variableCount; minterm++)
        {
            if ((truthBits & (1 << minterm)) == 0) continue;

            var literals = new List<string>();
            for (var v = 0; v < variableCount; v++)
                literals.Add((minterm & (1 << v)) != 0 ? variables[v] : "!" + variables[v]);
            terms.Add(string.Join(" & ", literals));
        }

        return terms.Count == 0 ? "0" : string.Join(" | ", terms);
    }

    private static void AssertSoundOptimization(string expression)
    {
        var metrics = new OptimizationMetrics();
        var input = Parse(expression);
        var optimized = new RewriteEngine().Optimize(input, metrics);

        Assert.True(TruthTable.AreEquivalent(input, optimized),
            $"Non-equivalent optimization: '{expression}' -> '{optimized}'");
        Assert.False(metrics.RuleApplicationCount.ContainsKey("SoundnessRollback"),
            $"Soundness guard fired (an unsound rule was applied) for: '{expression}'");
    }

    [Fact]
    public void Optimize_MutualConsensusRegression_KeepsAllMinterms()
    {
        // Used to optimize to 'b & !c | c & !b', silently dropping the minterm a=0,b=0,c=0:
        // two consensus terms justified each other's removal and both were deleted at once
        AssertSoundOptimization("!a & !b & !c | !a & b & !c | a & b & !c | !a & !b & c | a & !b & c");
    }

    [Fact]
    public void Optimize_MutualAbsorptionRegression_KeepsCommutativeDuplicates()
    {
        // 4-variable variant: ConsensusOptimizer used to generate a commutative duplicate
        // ("!c & !a" alongside "!a & !c"); the terms absorbed each other and BOTH were
        // removed, losing minterms
        AssertSoundOptimization(
            "!a & !b & !c & !d | !a & b & !c & !d | a & b & !c & !d | !a & !b & c & !d | a & !b & c & !d");
    }

    [Fact]
    public void Optimize_AllThreeVariableFunctions_PreserveSemantics()
    {
        var variables = new[] { "a", "b", "c" };
        for (var truthBits = 1; truthBits < 255; truthBits++)
            AssertSoundOptimization(CanonicalDnf(3, truthBits, variables));
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    [Trait("Category", "ReleaseEvidence")]
    public void Optimize_AllFourVariableFunctions_PreserveSemantics()
    {
        // doc/CLAIMS.md cites this sweep as evidence for the "verified" claim, so it carries the
        // ReleaseEvidence category: it runs as a gate before publishing and its .trx goes into the
        // release evidence bundle. Exhaustive keeps it out of the per-push suite.
        var variables = new[] { "a", "b", "c", "d" };
        for (var truthBits = 1; truthBits < 65535; truthBits++)
            AssertSoundOptimization(CanonicalDnf(4, truthBits, variables));
    }

    [Fact]
    public void Optimize_RandomExpressions_PreserveSemantics()
    {
        var variables = new[] { "a", "b", "c", "d", "e", "f" };
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            AssertSoundOptimization(RandomExpression(random, variables, 4));
        }
    }

    private static string RandomExpression(Random random, string[] variables, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            var negation = random.Next(3) == 0 ? "!" : "";
            return negation + variables[random.Next(variables.Length)];
        }

        var left = RandomExpression(random, variables, depth - 1);
        var right = RandomExpression(random, variables, depth - 1);
        var op = random.Next(2) == 0 ? "&" : "|";
        var expression = $"{left} {op} {right}";
        return random.Next(3) == 0 ? $"({expression})" : expression;
    }
}
