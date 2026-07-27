using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     v2 canonical-form invariants. The <see cref="FormulaFactory" /> promises that every
///     tree it hands out is in canonical form: n-ary And/Or nodes are flattened (no nested
///     operand of the same type), deduplicated, free of constants and complement pairs,
///     and sorted by the stable canonical key; double negation is folded; interning makes
///     structurally equal factory-built nodes reference-equal. These tests pin that
///     contract over corpora of random RAW ASTs (built with the raw node constructors,
///     bypassing the factory) and over parsed expressions, plus the n-ary Tseitin
///     encoding advantage over the v1 binary-chain encoding.
/// </summary>
public class CanonicalInvariantTests
{
    // ===== Random raw AST corpus (raw constructors — no factory canonicalization) =====

    private static AstNode RawAst(Random rng, int variableCount, int depth, bool allowConstants)
    {
        if (depth <= 0 || rng.Next(4) == 0)
        {
            if (allowConstants && rng.Next(10) == 0) return new ConstantNode(rng.Next(2) == 0);
            AstNode leaf = new VariableNode($"v{rng.Next(variableCount)}");
            return rng.Next(3) == 0 ? new NotNode(leaf) : leaf;
        }

        var left = RawAst(rng, variableCount, depth - 1, allowConstants);
        var right = RawAst(rng, variableCount, depth - 1, allowConstants);
        AstNode node = rng.Next(2) == 0 ? new AndNode(left, right) : new OrNode(left, right);
        if (rng.Next(4) == 0) node = new NotNode(node);
        return node;
    }

    /// <summary>
    ///     Walks a factory-built tree and asserts every canonical-shape invariant. The
    ///     factory must be the one that built the tree so interning checks can use
    ///     reference equality.
    /// </summary>
    private static void AssertCanonicalShape(AstNode node, FormulaFactory factory)
    {
        switch (node)
        {
            case NaryNode nary:
                Assert.True(nary.Operands.Count >= 2,
                    $"N-ary node with {nary.Operands.Count} operand(s): '{nary}'");

                var seen = new HashSet<AstNode>();
                foreach (var operand in nary.Operands)
                {
                    Assert.True(operand.GetType() != nary.GetType(),
                        $"Nested same-type operand under '{nary}': '{operand}'");
                    Assert.True(operand is not ConstantNode,
                        $"Constant operand survived under '{nary}'");
                    Assert.True(seen.Add(operand),
                        $"Duplicate operand '{operand}' under '{nary}'");
                }

                foreach (var operand in nary.Operands)
                    Assert.False(operand is NotNode not && seen.Contains(not.Operand),
                        $"Complement pair '{(operand as NotNode)?.Operand}'/'{operand}' under '{nary}'");

                // Canonical sortedness + interning: rebuilding the node from its own
                // operand list must be a no-op returning the very same instance
                var rebuilt = nary is AndNode
                    ? factory.And(nary.Operands)
                    : factory.Or(nary.Operands);
                Assert.Same(nary, rebuilt);

                foreach (var operand in nary.Operands) AssertCanonicalShape(operand, factory);
                break;

            case NotNode notNode:
                Assert.True(notNode.Operand is not NotNode,
                    $"Double negation survived: '{notNode}'");
                Assert.True(notNode.Operand is not ConstantNode,
                    $"Negated constant survived: '{notNode}'");
                AssertCanonicalShape(notNode.Operand, factory);
                break;

            case VariableNode:
            case ConstantNode:
                break;

            default:
                Assert.Fail($"Non-canonical node type {node.GetType().Name} in factory output: '{node}'");
                break;
        }
    }

    [Fact]
    public void Import_IsIdempotentAndInternsToReferenceEquality()
    {
        var rng = new Random(20260727);
        for (var i = 0; i < 300; i++)
        {
            var raw = RawAst(rng, 6, 5, allowConstants: true);
            var factory = new FormulaFactory();

            var once = factory.Import(raw);
            var twice = factory.Import(once);

            // Idempotence is not just structural: interning promises the same instance
            Assert.Same(once, twice);
            Assert.Same(once, factory.Import(raw));

            // A fresh factory must agree on the canonical form (structural, not reference)
            Assert.Equal(once, new FormulaFactory().Import(raw));

            Assert.True(TruthTable.AreEquivalent(raw, once),
                $"Import changed semantics of '{raw}' (iteration {i})");
        }
    }

    [Fact]
    public void Import_ProducesCanonicalShape_OnRandomRawAsts()
    {
        var rng = new Random(1157);
        for (var i = 0; i < 300; i++)
        {
            var factory = new FormulaFactory();
            var canonical = factory.Import(RawAst(rng, 6, 5, allowConstants: true));
            AssertCanonicalShape(canonical, factory);
        }
    }

    [Fact]
    public void Parse_ProducesCanonicalShape_AndPrintParseFixpoint()
    {
        var rng = new Random(424242);
        for (var i = 0; i < 300; i++)
        {
            var expression = RandomExpressions.Generate(rng, 6, 5, allowConstants: true);
            var factory = new FormulaFactory();
            var parsed = factory.Parse(expression);

            AssertCanonicalShape(parsed, factory);

            // Canonical forms are print-parse fixpoints: reparse reproduces the tree
            var printed = parsed.ToString();
            var reparsed = factory.Parse(printed);
            Assert.Same(parsed, reparsed);
            Assert.Equal(printed, reparsed.ToString());
        }
    }

    // ===== N-ary Tseitin encoding =====

    /// <summary>Rebuilds n-ary And/Or nodes as v1-style binary right-fold chains.</summary>
    private static AstNode Binarize(AstNode node)
    {
        return node switch
        {
            AndNode and => and.Operands.Select(Binarize).Reverse()
                .Aggregate((right, left) => new AndNode(left, right)),
            OrNode or => or.Operands.Select(Binarize).Reverse()
                .Aggregate((right, left) => new OrNode(left, right)),
            NotNode not => new NotNode(Binarize(not.Operand)),
            _ => node
        };
    }

    [Theory]
    [InlineData("a & b & c & d")]
    [InlineData("a | b | c | d")]
    public void Tseitin_NaryGate_HasPinnedClauseAndAuxCounts(string expression)
    {
        // One aux gate for the whole 4-input connective: 4 implication clauses + 1
        // reverse clause + 1 root unit. The v1 binary chain needed 3 gates and
        // 3 * 3 + 1 = 10 clauses.
        var factory = new FormulaFactory();
        var cnf = TseitinConverter.Convert(factory.Parse(expression), CnfEncodingStyle.Tseitin);

        Assert.Equal(1, cnf.AuxiliaryVariableCount);
        Assert.Equal(6, cnf.Clauses.Count);

        var binaryCnf = TseitinConverter.Convert(Binarize(factory.Parse(expression)), CnfEncodingStyle.Tseitin);
        Assert.Equal(3, binaryCnf.AuxiliaryVariableCount);
        Assert.Equal(10, binaryCnf.Clauses.Count);
    }

    [Fact]
    public void Tseitin_NaryEncoding_NeverExceedsBinaryChainAndStaysEquisatisfiable()
    {
        var rng = new Random(90210);
        for (var i = 0; i < 200; i++)
        {
            var factory = new FormulaFactory();
            var canonical = factory.Import(RawAst(rng, 6, 5, allowConstants: false));
            var binarized = Binarize(canonical);

            foreach (var style in new[] { CnfEncodingStyle.Tseitin, CnfEncodingStyle.PlaistedGreenbaum })
            {
                var nary = TseitinConverter.Convert(canonical, style);
                var chain = TseitinConverter.Convert(binarized, style);
                Assert.True(nary.Clauses.Count <= chain.Clauses.Count,
                    $"{style}: n-ary encoding has {nary.Clauses.Count} clauses, " +
                    $"binary chain only {chain.Clauses.Count} for '{canonical}'");
                Assert.True(nary.AuxiliaryVariableCount <= chain.AuxiliaryVariableCount,
                    $"{style}: n-ary encoding uses {nary.AuxiliaryVariableCount} aux vars, " +
                    $"binary chain only {chain.AuxiliaryVariableCount} for '{canonical}'");
            }

            // Equisatisfiability spot-check against the brute-force oracle
            if (i % 20 == 0)
            {
                var variables = canonical.GetVariables().OrderBy(v => v).ToList();
                var satisfiable = canonical is ConstantNode constant
                    ? constant.Value
                    : RandomExpressions.CountModels(canonical, variables) > 0;
                var verdict = SatSolver.FromCnf(TseitinConverter.Convert(canonical)).Solve();
                Assert.True((verdict == SatResult.Satisfiable) == satisfiable,
                    $"N-ary Tseitin broke equisatisfiability for '{canonical}'");
            }
        }
    }
}
