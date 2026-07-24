using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     FormulaFactory contract: n-ary flattening, duplicate/constant/complement folding,
///     structural interning (reference equality for equal formulas), and semantic
///     faithfulness of Import against the truth-table oracle.
/// </summary>
public class FormulaFactoryTests
{
    [Fact]
    public void And_FlattensAndDeduplicates()
    {
        var f = new FormulaFactory();
        var a = f.Variable("a");
        var b = f.Variable("b");
        var c = f.Variable("c");

        var nested = f.And(f.And(a, b), f.And(b, c), a);
        Assert.Equal("a & b & c", nested.ToString());
    }

    [Fact]
    public void ConstantFolding_IdentityAndAnnihilator()
    {
        var f = new FormulaFactory();
        var a = f.Variable("a");

        Assert.Same(a, f.And(a, f.True));
        Assert.Same(f.False, f.And(a, f.False));
        Assert.Same(a, f.Or(a, f.False));
        Assert.Same(f.True, f.Or(a, f.True));
        Assert.Same(f.True, f.And());
        Assert.Same(f.False, f.Or());
    }

    [Fact]
    public void ComplementFolding_CollapsesConnective()
    {
        var f = new FormulaFactory();
        var a = f.Variable("a");
        var b = f.Variable("b");

        Assert.Same(f.False, f.And(a, b, f.Not(a)));
        Assert.Same(f.True, f.Or(a, b, f.Not(a)));
    }

    [Fact]
    public void Not_FoldsDoubleNegationAndConstants()
    {
        var f = new FormulaFactory();
        var a = f.Variable("a");

        Assert.Same(a, f.Not(f.Not(a)));
        Assert.Same(f.False, f.Not(f.True));
        Assert.Same(f.True, f.Not(f.False));
    }

    [Fact]
    public void Interning_EqualFormulasAreSameInstance()
    {
        var f = new FormulaFactory();
        var first = f.And(f.Variable("a"), f.Variable("b"));
        var second = f.And(f.Variable("a"), f.Variable("b"));
        Assert.Same(first, second);

        // Shared suffixes of right-folded n-ary chains are shared instances too
        var chain1 = f.And(f.Variable("x"), f.Variable("y"), f.Variable("z"));
        var chain2 = f.And(f.Variable("w"), f.Variable("y"), f.Variable("z"));
        Assert.Same(((AndNode)chain1).Right, ((AndNode)chain2).Right);
    }

    [Fact]
    public void Import_RandomExpressions_PreservesSemantics()
    {
        var rng = new Random(999);
        var f = new FormulaFactory();
        for (var trial = 0; trial < 60; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, 5, rng.Next(1, 5), allowConstants: true));
            var imported = f.Import(ast);
            Assert.True(TruthTable.AreEquivalent(ast, imported),
                $"Trial {trial}: import changed semantics of '{ast}'");
            Assert.True(AstMetrics.CountLiterals(imported) <= AstMetrics.CountLiterals(ast),
                $"Trial {trial}: construction-time folding worsened '{ast}'");
        }
    }

    [Fact]
    public void ExtendedConstructors_MatchDefinitions()
    {
        var f = new FormulaFactory();
        var a = f.Variable("a");
        var b = f.Variable("b");

        Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse("!a | b"), f.Implication(a, b)));
        Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse("a & b | !a & !b"), f.Equivalence(a, b)));
        Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse("a & !b | !a & b"), f.Xor(a, b)));
    }

    [Fact]
    public void Parse_GoesThroughFactoryCanonicalization()
    {
        var f = new FormulaFactory();
        Assert.Same(f.Variable("a"), f.Parse("a & a & (a | b | !b)"));
    }
}
