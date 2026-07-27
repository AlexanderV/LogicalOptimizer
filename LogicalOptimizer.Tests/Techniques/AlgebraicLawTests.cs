using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Algebraic testing: every axiom of boolean algebra is checked as a LAW over random
///     subexpressions F, G, H — both sides must optimize to semantically equal results,
///     and where the axiom collapses one side (absorption, complement, identity) the
///     optimizer must actually reach the collapsed form.
/// </summary>
public class AlgebraicLawTests
{
    private static readonly OptimizationOptions ResultOnly = new()
    { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

    private static string Optimize(string expression)
    {
        return new BooleanExpressionOptimizer().OptimizeExpression(expression, ResultOnly).Optimized;
    }

    private static void AssertLaw(string lhs, string rhs, string law)
    {
        Assert.True(TruthTable.AreEquivalent(lhs, rhs),
            $"{law}: sides are not equivalent BEFORE optimization: '{lhs}' vs '{rhs}'");
        Assert.True(TruthTable.AreEquivalent(Optimize(lhs), Optimize(rhs)),
            $"{law}: optimizer broke the law for '{lhs}' vs '{rhs}'");
    }

    private static IEnumerable<(string F, string G, string H)> RandomTriples(int seed, int count)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
            yield return (
                "(" + RandomExpressions.Generate(rng, 4, 2) + ")",
                "(" + RandomExpressions.Generate(rng, 4, 2) + ")",
                "(" + RandomExpressions.Generate(rng, 4, 2) + ")");
    }

    [Fact]
    public void Commutativity()
    {
        foreach (var (f, g, _) in RandomTriples(1, 20))
        {
            AssertLaw($"{f} & {g}", $"{g} & {f}", "AND-commutativity");
            AssertLaw($"{f} | {g}", $"{g} | {f}", "OR-commutativity");
        }
    }

    [Fact]
    public void Commutativity_ProducesIdenticalCanonicalForm()
    {
        // Stronger than semantic equality: the canonical output text must be identical,
        // otherwise downstream caching/deduplication by string breaks
        foreach (var (f, g, _) in RandomTriples(2, 20))
        {
            Assert.Equal(Optimize($"{f} & {g}"), Optimize($"{g} & {f}"));
            Assert.Equal(Optimize($"{f} | {g}"), Optimize($"{g} | {f}"));
        }
    }

    [Fact]
    public void Associativity()
    {
        foreach (var (f, g, h) in RandomTriples(3, 15))
        {
            AssertLaw($"({f} & {g}) & {h}", $"{f} & ({g} & {h})", "AND-associativity");
            AssertLaw($"({f} | {g}) | {h}", $"{f} | ({g} | {h})", "OR-associativity");
        }
    }

    [Fact]
    public void Distributivity()
    {
        foreach (var (f, g, h) in RandomTriples(4, 15))
        {
            AssertLaw($"{f} & ({g} | {h})", $"{f} & {g} | {f} & {h}", "AND-over-OR");
            AssertLaw($"{f} | {g} & {h}", $"({f} | {g}) & ({f} | {h})", "OR-over-AND");
        }
    }

    [Fact]
    public void DeMorgan()
    {
        foreach (var (f, g, _) in RandomTriples(5, 20))
        {
            AssertLaw($"!({f} & {g})", $"!{f} | !{g}", "De Morgan AND");
            AssertLaw($"!({f} | {g})", $"!{f} & !{g}", "De Morgan OR");
        }
    }

    [Fact]
    public void Absorption_CollapsesToSingleOperand()
    {
        foreach (var (f, g, _) in RandomTriples(6, 20))
        {
            // Canonical-string identity, not mere equivalence: the optimizer must actually
            // ABSORB the redundant conjunct and reach byte-identical output to Optimize(f).
            // Equivalence would hold even if AbsorptionRule never fired (f | f&g ≡ f always).
            Assert.Equal(Optimize(f), Optimize($"{f} | {f} & {g}"));
            Assert.Equal(Optimize(f), Optimize($"{f} & ({f} | {g})"));
        }
    }

    [Fact]
    public void Idempotence_CollapsesToSingleOperand()
    {
        foreach (var (f, _, _) in RandomTriples(7, 20))
        {
            // Canonical-string identity: the optimizer must collapse the duplicate operand
            // to byte-identical output. Equivalence alone (f & f ≡ f) holds unconditionally
            // and would not detect a broken idempotent-dedup.
            Assert.Equal(Optimize(f), Optimize($"{f} & {f}"));
            Assert.Equal(Optimize(f), Optimize($"{f} | {f}"));
        }
    }

    [Fact]
    public void ComplementAndIdentity()
    {
        // Variable-level complement laws must collapse to constants EXACTLY
        Assert.Equal("1", Optimize("a | !a"));
        Assert.Equal("0", Optimize("a & !a"));
        Assert.Equal("a", Optimize("a & 1"));
        Assert.Equal("a", Optimize("a | 0"));
        Assert.Equal("0", Optimize("a & 0"));
        Assert.Equal("1", Optimize("a | 1"));
        Assert.Equal("a", Optimize("!!a"));

        // Expression-level: the optimizer must EMIT the constant, not merely stay
        // equivalent to it (equivalence to 0/1 is guaranteed by soundness regardless of
        // whether the complement law fired).
        foreach (var (f, _, _) in RandomTriples(8, 15))
        {
            Assert.Equal("0", Optimize($"{f} & !({f})"));
            Assert.Equal("1", Optimize($"{f} | !({f})"));
        }
    }

    [Fact]
    public void Consensus_RedundantTermIsAbsorbed()
    {
        // (x & y) | (!x & z) | (y & z): the consensus term y & z is redundant
        var optimized = Optimize("x & y | !x & z | y & z");
        Assert.True(TruthTable.AreEquivalent("x & y | !x & z", optimized));
        Assert.Equal(4, AstMetrics.CountLiterals(RandomExpressions.Parse(optimized)));
    }

    [Fact]
    public void QuantifierLaws_HoldAsCanonicalNodeIdentities()
    {
        // ROBDDs are canonical, so algebraic quantifier laws are REFERENCE identities:
        // ∃x.(f∨g) = ∃x.f ∨ ∃x.g;  ∀x.(f∧g) = ∀x.f ∧ ∀x.g;  ∀x.f = ¬∃x.¬f
        var rng = new Random(11011);
        for (var trial = 0; trial < 25; trial++)
        {
            var fText = RandomExpressions.Generate(rng, 4, 3);
            var gText = RandomExpressions.Generate(rng, 4, 3);
            var fAst = RandomExpressions.Parse(fText);
            var gAst = RandomExpressions.Parse(gText);
            var variables = fAst.GetVariables().Union(gAst.GetVariables()).OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;
            var x = variables[rng.Next(variables.Count)];

            var bdd = new BinaryDecisionDiagram(variables);
            var f = bdd.FromAst(fAst);
            var g = bdd.FromAst(gAst);

            Assert.Equal(
                bdd.Exists(bdd.Ite(f, BinaryDecisionDiagram.TrueNode, g), x),
                bdd.Ite(bdd.Exists(f, x), BinaryDecisionDiagram.TrueNode, bdd.Exists(g, x)));

            Assert.Equal(
                bdd.ForAll(bdd.Ite(f, g, BinaryDecisionDiagram.FalseNode), x),
                bdd.Ite(bdd.ForAll(f, x), bdd.ForAll(g, x), BinaryDecisionDiagram.FalseNode));

            Assert.Equal(bdd.ForAll(f, x), bdd.Negate(bdd.Exists(bdd.Negate(f), x)));
        }
    }

    [Fact]
    public void CompositionLaws_IdentityAndConstants()
    {
        // f[x := x] = f;  f[x := 1] = f|x=1;  f[x := 0] = f|x=0 — as canonical nodes
        var rng = new Random(12012);
        for (var trial = 0; trial < 25; trial++)
        {
            var ast = RandomExpressions.Parse(RandomExpressions.Generate(rng, 4, 3));
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;
            var x = variables[rng.Next(variables.Count)];

            var bdd = BinaryDecisionDiagram.Build(ast);
            var xNode = bdd.FromAst(new VariableNode(x));

            Assert.Equal(bdd.Root, bdd.Compose(bdd.Root, x, xNode));
            Assert.Equal(bdd.Restrict(bdd.Root, x, true),
                bdd.Compose(bdd.Root, x, BinaryDecisionDiagram.TrueNode));
            Assert.Equal(bdd.Restrict(bdd.Root, x, false),
                bdd.Compose(bdd.Root, x, BinaryDecisionDiagram.FalseNode));
        }
    }

    [Fact]
    public void FormulaFactory_AssociativityIsConstructionInvariant()
    {
        // Flattening makes association REFERENCE-invariant at construction time
        var f = new FormulaFactory();
        var a = f.Variable("a");
        var b = f.Variable("b");
        var c = f.Variable("c");
        var d = f.Variable("d");

        Assert.Same(f.And(f.And(a, b), f.And(c, d)), f.And(a, f.And(b, f.And(c, d))));
        Assert.Same(f.Or(f.Or(a, b), f.Or(c, d)), f.Or(a, f.Or(b, f.Or(c, d))));
        Assert.Same(f.And(a, b, c, d), f.And(f.And(a, b), c, d));
    }

    [Fact]
    public void ShannonExpansion_IsAnIdentity()
    {
        // F ≡ x & F|x=1 | !x & F|x=0 — checked through the optimizer for random F
        var rng = new Random(9);
        for (var trial = 0; trial < 20; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 4, 3);
            var ast = RandomExpressions.Parse(expression);
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) continue;

            var x = variables[rng.Next(variables.Count)];
            var positive = RandomExpressions.Substitute(ast, x, true);
            var negative = RandomExpressions.Substitute(ast, x, false);

            AssertLaw(expression, $"{x} & ({positive}) | !{x} & ({negative})",
                $"Shannon expansion over {x}");
        }
    }
}
