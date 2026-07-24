using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Regression tests for the Phase-2 rule-completeness upgrades. All expressions use
///     MORE than MAX_EXACT_MINIMIZATION_VARIABLES variables (13+), so results come from
///     the rewrite pipeline itself, not from the exact Quine–McCluskey backend.
///     Assertions are equivalence + literal budget (not exact strings) so that
///     cost-neutral reorderings do not break them.
/// </summary>
public class RuleCompletenessTests
{
    private const string Filler12 =
        "v1 & v2 & v3 & v4 & v5 & v6 & v7 & v8 & v9 & v10 & v11 & v12";

    private const string FillerClauses12 =
        "(v1 | v2) & (v3 | v4) & (v5 | v6) & (v7 | v8) & (v9 | v10) & (v11 | v12)";

    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static void AssertOptimizedWithinBudget(string expression, int maxLiterals)
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression);
        var optimizedAst = Parse(result.Optimized);

        Assert.True(EquivalenceChecker.Check(Parse(expression), optimizedAst).AreEquivalent,
            $"Non-equivalent optimization: '{expression}' -> '{result.Optimized}'");

        var literals = AstMetrics.CountLiterals(optimizedAst);
        Assert.True(literals <= maxLiterals,
            $"Expected at most {maxLiterals} literals, got {literals}: '{result.Optimized}'");
    }

    [Fact]
    public void Absorption_NegatedAbsorber_BeyondQmGate()
    {
        // !a | a & b → !a | b (mirror polarity of extended absorption)
        AssertOptimizedWithinBudget($"!a | a & b | {Filler12}", 14);
    }

    [Fact]
    public void Absorption_CompoundAbsorber_BeyondQmGate()
    {
        // a & b | !(a & b) & c → a & b | c (De Morgan-spread complement is cancelled)
        AssertOptimizedWithinBudget($"a & b | !(a & b) & c | {Filler12}", 15);
    }

    [Fact]
    public void ClauseConsensus_RedundantResolventClause_BeyondQmGate()
    {
        // (b | c) is the resolvent of (a | b) and (!a | c) and must be dropped
        AssertOptimizedWithinBudget($"(a | b) & (!a | c) & (b | c) & {FillerClauses12}", 16);
    }

    [Fact]
    public void Factoring_Subgroup_BeyondQmGate()
    {
        // a & b | a & c | <unrelated> → a & (b | c) | <unrelated>
        AssertOptimizedWithinBudget($"a & b | a & c | {Filler12}", 15);
    }

    [Fact]
    public void ExpandReduce_CrossesGrowFirstValley_BeyondQmGate()
    {
        // (a | b) & c | a & !c → a | b & c requires temporary distribution
        AssertOptimizedWithinBudget($"(a | b) & c | a & !c | {Filler12}", 15);
    }

    [Fact]
    public void Factoring_AllPairsReverse_BeyondQmGate()
    {
        // Non-adjacent clauses sharing a literal must still merge: 5 literals in the
        // (a|d)(b|c)(b|d) part instead of 6
        AssertOptimizedWithinBudget($"(a | d) & (b | c) & (b | d) & {FillerClauses12}", 17);
    }
}
