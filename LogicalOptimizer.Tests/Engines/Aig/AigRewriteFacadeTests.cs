using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The AIG rewriting facade capability (<see cref="OptimizationOptions.EnableAigRewriting" />),
///     on by default since v3.0. Two guarantees: it never changes the meaning of the result and
///     never makes it costlier than the flag-off (pre-3.0) path, and the default path is exactly
///     the flag-on path (turning the flag off restores the pre-3.0 output byte-for-byte).
/// </summary>
public class AigRewriteFacadeTests
{
    private static readonly string[] Corpus =
    {
        "a&b",
        "a|b",
        "(a&b)|(a&c)",
        "(a|b)&(a|c)",
        "(a&b&c)|(a&b&d)|(a&c&d)|(b&c&d)",
        "(a|(((c&b&a)|!d|(d&c))&((b|a)&(a|c|b)&c))|c)",
        "((!(c&d)|!(a&d))&!(c&!a))",
        "!((a&b)|(c&d))",
        "(a&!b)|(!a&b)|(c&d)",
        "((a&b)|c)&((a&b)|d)&(e|f)",
        "(a&b&c&d)|(!a&!b&!c&!d)",
        "a&(b|c)&(d|!e)&(a|f)"
    };

    private static (int Literals, int Nodes) Cost(string optimized)
    {
        var ast = new FormulaFactory().Parse(optimized);
        return (AstMetrics.CountLiterals(ast), AstMetrics.CountNodes(ast));
    }

    [Fact]
    public void WithFlagOn_ResultStaysEquivalentAndNoCostlierThanDefault()
    {
        var optimizer = new BooleanExpressionOptimizer();
        foreach (var expression in Corpus)
        {
            var off = optimizer.OptimizeExpression(expression, new OptimizationOptions { EnableAigRewriting = false });
            var on = optimizer.OptimizeExpression(expression, new OptimizationOptions { EnableAigRewriting = true });

            // (i) meaning is preserved against the original input.
            Assert.True(TruthTable.AreEquivalent(expression, on.Optimized),
                $"AIG result not equivalent to input for '{expression}': {on.Optimized}");
            Assert.Equal(true, EquivalenceChecker.Check(expression, on.Optimized).AreEquivalent);

            // (ii) never regresses cost versus the flag-off result.
            Assert.True(Cost(on.Optimized).CompareTo(Cost(off.Optimized)) <= 0,
                $"AIG made '{expression}' costlier: on='{on.Optimized}' off='{off.Optimized}'");
        }
    }

    [Fact]
    public void DefaultOptions_ProduceEquivalentResults()
    {
        // Rock-solid safety net for the v3.0 default flip: with the default (now AIG-on)
        // options, every optimized result must stay logically equivalent to its input,
        // proven independently of the facade's internal equivalence gate.
        var optimizer = new BooleanExpressionOptimizer();
        foreach (var expression in Corpus)
        {
            var result = optimizer.OptimizeExpression(expression, OptimizationOptions.Default);
            Assert.True(TruthTable.AreEquivalent(expression, result.Optimized),
                $"Default-options result not equivalent to input for '{expression}': {result.Optimized}");
            Assert.Equal(true, EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent);
        }
    }

    [Fact]
    public void DefaultPath_IsTheFlagOnPath()
    {
        // Since v3.0 the flag is on by default, so OptimizationOptions.Default must be
        // byte-for-byte identical to an explicit EnableAigRewriting = true.
        var optimizer = new BooleanExpressionOptimizer();
        foreach (var expression in Corpus)
        {
            var byDefault = optimizer.OptimizeExpression(expression, OptimizationOptions.Default);
            var explicitlyOn =
                optimizer.OptimizeExpression(expression, new OptimizationOptions { EnableAigRewriting = true });

            Assert.Equal(byDefault.Optimized, explicitlyOn.Optimized);
            Assert.Equal(byDefault.CNF, explicitlyOn.CNF);
            Assert.Equal(byDefault.DNF, explicitlyOn.DNF);
            Assert.Equal(byDefault.Advanced, explicitlyOn.Advanced);
        }
    }

    [Fact]
    public void WithFlagOff_RestoresThePre30OutputAndStaysEquivalent()
    {
        // The opt-out path (EnableAigRewriting = false) must never adopt an AIG candidate,
        // so its result is no cheaper than — and still equivalent to — the default (on) path.
        var optimizer = new BooleanExpressionOptimizer();
        foreach (var expression in Corpus)
        {
            var off = optimizer.OptimizeExpression(expression, new OptimizationOptions { EnableAigRewriting = false });

            Assert.True(TruthTable.AreEquivalent(expression, off.Optimized),
                $"Flag-off result not equivalent to input for '{expression}': {off.Optimized}");

            var on = optimizer.OptimizeExpression(expression, OptimizationOptions.Default);
            Assert.True(Cost(on.Optimized).CompareTo(Cost(off.Optimized)) <= 0,
                $"Default (on) path costlier than flag-off for '{expression}': on='{on.Optimized}' off='{off.Optimized}'");
        }
    }

    [Fact]
    public void FlagOn_HonorsCancellation()
    {
        var optimizer = new BooleanExpressionOptimizer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            optimizer.OptimizeExpression("(a|(((c&b&a)|!d|(d&c))&((b|a)&(a|c|b)&c))|c)",
                new OptimizationOptions { EnableAigRewriting = true, CancellationToken = cts.Token }));
    }
}
