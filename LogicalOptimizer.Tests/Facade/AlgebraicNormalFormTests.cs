using CsCheck;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for <see cref="Transformations.ToAlgebraicNormalForm" /> (ANF / Zhegalkin /
///     Reed–Muller polynomial). The ANF text grammar cannot be parsed back (there is no
///     <c>XOR</c> token in the lexer), so equivalence is asserted against the brute-force
///     truth-table oracle, which evaluates the full operator set including XOR.
/// </summary>
public class AlgebraicNormalFormTests
{
    private static AstNode Var(string name)
    {
        return new VariableNode(name);
    }

    // ASTs over the full operator set, restricted to a handful of variables so the
    // brute-force oracle stays within the truth-table cap.
    private static readonly Gen<AstNode> Leaf = Gen.OneOf(
        Gen.Int[0, 3].Select(i => (AstNode)new VariableNode($"v{i}")),
        Gen.Bool.Select(b => (AstNode)new ConstantNode(b)));

    private static Gen<AstNode> AstGen(int depth)
    {
        if (depth <= 0) return Leaf;
        var inner = AstGen(depth - 1);
        return Gen.OneOf(
            Leaf,
            Gen.Select(inner, inner, (l, r) => (AstNode)new AndNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new OrNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new XorNode(l, r)),
            inner.Select(x => (AstNode)new NotNode(x)));
    }

    private static readonly Gen<AstNode> AnyAst = AstGen(4);

    [Fact]
    public void Anf_IsSemanticallyEquivalentToOriginal_OverAllAssignments()
    {
        // Differential vs brute force: Evaluate(ANF) == Evaluate(original) everywhere.
        AnyAst.Sample(ast =>
        {
            var anf = Transformations.ToAlgebraicNormalForm(ast);
            Assert.True(TruthTable.AreEquivalent(ast, anf),
                $"ANF changed the truth function of '{ast}' -> '{AstFormatter.Format(anf)}'");
        }, iter: 500);
    }

    [Fact]
    public void Anf_IsIdempotent_And_MobiusIsAnInvolution()
    {
        // ANF is unique per truth function, so the ANF of an ANF is the SAME ANF
        // (structurally), and it stays equivalent to the original formula. Feeding the
        // canonical coefficients back through the Möbius transform is its own inverse.
        AnyAst.Sample(ast =>
        {
            var anf = Transformations.ToAlgebraicNormalForm(ast);
            var anfOfAnf = Transformations.ToAlgebraicNormalForm(anf);

            Assert.Equal(AstFormatter.Format(anf), AstFormatter.Format(anfOfAnf));
            Assert.True(TruthTable.AreEquivalent(ast, anfOfAnf),
                $"Round trip changed the truth function of '{ast}'");
        }, iter: 300);
    }

    [Fact]
    public void Anf_XorOfSelf_IsZero()
    {
        var anf = Transformations.ToAlgebraicNormalForm(new XorNode(Var("a"), Var("a")));
        Assert.Equal("0", AstFormatter.Format(anf));
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void Anf_Constants_MapToConstants(bool value, string expected)
    {
        var anf = Transformations.ToAlgebraicNormalForm(new ConstantNode(value));
        Assert.Equal(expected, AstFormatter.Format(anf));
    }

    [Fact]
    public void Anf_Conjunction_IsTheSingleMonomial()
    {
        // ANF of (a & b) is exactly the monomial a & b.
        var anf = Transformations.ToAlgebraicNormalForm(new AndNode(Var("a"), Var("b")));
        Assert.Equal("a & b", AstFormatter.Format(anf));
    }

    [Fact]
    public void Anf_Negation_IsOnePlusVariable()
    {
        // ANF of !a is 1 XOR a.
        var anf = Transformations.ToAlgebraicNormalForm(new NotNode(Var("a")));
        Assert.Equal("1 XOR a", AstFormatter.Format(anf));
    }

    [Fact]
    public void Anf_Xor_PassesThrough()
    {
        // ANF of (a XOR b) is a XOR b.
        var anf = Transformations.ToAlgebraicNormalForm(new XorNode(Var("a"), Var("b")));
        Assert.Equal("a XOR b", AstFormatter.Format(anf));
    }

    [Fact]
    public void Anf_Disjunction_ExpandsToXorOfMonomials()
    {
        // ANF of (a | b) is a XOR b XOR (a & b); assert both structure and semantics.
        var or = new OrNode(Var("a"), Var("b"));
        var anf = Transformations.ToAlgebraicNormalForm(or);

        Assert.Equal("(a XOR b) XOR (a & b)", AstFormatter.Format(anf));
        Assert.True(TruthTable.AreEquivalent(or, anf));
    }

    [Fact]
    public void Anf_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            Transformations.ToAlgebraicNormalForm(new AndNode(Var("a"), Var("b")), cts.Token));
    }

    [Fact]
    public void Anf_RejectsNullFormula()
    {
        Assert.Throws<ArgumentNullException>(() => Transformations.ToAlgebraicNormalForm(null!));
    }
}
