using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The opt-in AIG rewriting facade capability (<see cref="OptimizationOptions.EnableAigRewriting" />).
///     Two guarantees: enabling it never changes the meaning of the result and never makes it
///     costlier than the default path, and leaving it off (the default) reproduces the existing
///     output byte-for-byte.
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
    public void WithFlagOff_OutputIsIdenticalToTheDefaultPath()
    {
        var optimizer = new BooleanExpressionOptimizer();
        foreach (var expression in Corpus)
        {
            var byDefault = optimizer.OptimizeExpression(expression, OptimizationOptions.Default);
            var explicitlyOff =
                optimizer.OptimizeExpression(expression, new OptimizationOptions { EnableAigRewriting = false });

            Assert.Equal(byDefault.Optimized, explicitlyOff.Optimized);
            Assert.Equal(byDefault.CNF, explicitlyOff.CNF);
            Assert.Equal(byDefault.DNF, explicitlyOff.DNF);
            Assert.Equal(byDefault.Advanced, explicitlyOff.Advanced);
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
