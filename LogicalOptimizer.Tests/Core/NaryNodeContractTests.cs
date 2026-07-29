using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Contract tests for the v2 node hierarchy: n-ary core nodes (AndNode/OrNode over
/// an immutable Operands list), the derived binary operator nodes (XorNode, NandNode,
/// NorNode, EqvNode, ImpNode) and the AstFormatter rendering rules. The raw
/// constructors are low-level: they store operands as given (no flattening, sorting
/// or folding) — canonical trees come from FormulaFactory.
/// </summary>
public class NaryNodeContractTests
{
    public static TheoryData<string, Func<AstNode, AstNode, BinaryNode>> DerivedNodeFactories => new()
    {
        { "XOR", (left, right) => new XorNode(left, right) },
        { "~&", (left, right) => new NandNode(left, right) },
        { "~|", (left, right) => new NorNode(left, right) },
        { "↔", (left, right) => new EqvNode(left, right) },
        { "→", (left, right) => new ImpNode(left, right) }
    };

    public static TheoryData<string, Func<IReadOnlyList<AstNode>, NaryNode>> NaryFactories => new()
    {
        { "&", operands => new AndNode(operands) },
        { "|", operands => new OrNode(operands) }
    };

    #region N-ary node invariants

    [Theory]
    [MemberData(nameof(NaryFactories))]
    public void NaryCtor_FewerThanTwoOperands_Throws(string symbol,
        Func<IReadOnlyList<AstNode>, NaryNode> factory)
    {
        // symbol identifies the node type in the failure message (and keeps the MemberData
        // column used) — a bare Assert.NotNull(symbol) here would have been vacuous filler.
        var empty = Assert.Throws<ArgumentException>(() => factory(Array.Empty<AstNode>()));
        Assert.False(string.IsNullOrWhiteSpace(empty.Message), $"{symbol}: empty-operand validation needs a diagnostic");
        Assert.Throws<ArgumentException>(() => factory(new AstNode[] { new VariableNode("a") }));
    }

    [Theory]
    [MemberData(nameof(NaryFactories))]
    public void NaryCtor_NullListOrNullOperand_Throws(string symbol,
        Func<IReadOnlyList<AstNode>, NaryNode> factory)
    {
        Assert.Throws<ArgumentNullException>(() => factory(null!));
        var nullOperand = Assert.Throws<ArgumentException>(
            () => factory(new AstNode[] { new VariableNode("a"), null! }));
        Assert.False(string.IsNullOrWhiteSpace(nullOperand.Message), $"{symbol}: null-operand validation needs a diagnostic");
    }

    [Theory]
    [MemberData(nameof(NaryFactories))]
    public void Operands_AreDefensivelyCopied(string symbol,
        Func<IReadOnlyList<AstNode>, NaryNode> factory)
    {
        var source = new AstNode[] { new VariableNode("a"), new VariableNode("b") };
        var node = factory(source);

        // Mutating the source array must not affect the node
        source[0] = new VariableNode("z");

        Assert.Equal("a", ((VariableNode)node.Operands[0]).Name);
        Assert.Equal("b", ((VariableNode)node.Operands[1]).Name);
        Assert.False(node.Operands[0] == source[0], $"{symbol}: node aliased the mutated source slot");
    }

    [Theory]
    [MemberData(nameof(NaryFactories))]
    public void Operands_PreserveGivenOrderAndCount(string symbol,
        Func<IReadOnlyList<AstNode>, NaryNode> factory)
    {
        // Raw constructors store operands as given — canonicalization is the factory's job
        var node = factory(new AstNode[]
            { new VariableNode("c"), new VariableNode("a"), new VariableNode("b") });

        Assert.Equal(3, node.Operands.Count);
        Assert.Equal($"c {symbol} a {symbol} b", node.ToString());
    }

    [Fact]
    public void Equality_IsStructuralAndOrderSensitive()
    {
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");

        Assert.Equal(new AndNode(a, b), new AndNode(new VariableNode("a"), new VariableNode("b")));
        Assert.NotEqual<AstNode>(new AndNode(a, b), new AndNode(b, a));
        Assert.NotEqual<AstNode>(new AndNode(a, b), new OrNode(a, b));
        Assert.NotEqual<AstNode>(new AndNode(a, b), new AndNode(new AstNode[] { a, b, c }));
    }

    [Fact]
    public void HashCode_IsConsistentWithEqualityAndStable()
    {
        var first = new AndNode(new AstNode[]
            { new VariableNode("a"), new VariableNode("b"), new VariableNode("c") });
        var second = new AndNode(new AstNode[]
            { new VariableNode("a"), new VariableNode("b"), new VariableNode("c") });

        // Equal-hash-for-equal-values is the contract that makes hash-based interning work:
        // two INDEPENDENTLY constructed, structurally-equal nodes are Equal and share a hash.
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        // The hash folds node type and operand ORDER in (see NaryNode ctor), so a
        // structurally-different node — same operand set, reversed order — is not Equal and
        // hashes differently. This is what distinguishes real structural hashing from a
        // trivial constant/identity implementation.
        var reordered = new AndNode(new AstNode[]
            { new VariableNode("c"), new VariableNode("b"), new VariableNode("a") });
        Assert.NotEqual<AstNode>(first, reordered);
        Assert.NotEqual(first.GetHashCode(), reordered.GetHashCode());
    }

    [Fact]
    public void GetVariables_CollectsAcrossAllOperands()
    {
        var node = new OrNode(new AstNode[]
        {
            new VariableNode("x"),
            new AndNode(new VariableNode("y"), new VariableNode("z")),
            new NotNode(new VariableNode("x"))
        });

        Assert.Equal(new[] { "x", "y", "z" }, node.GetVariables().OrderBy(v => v));
    }

    #endregion

    #region Immutability: Clone returns the same instance for every node type

    public static TheoryData<AstNode> AllNodeKinds => new()
    {
        new VariableNode("a"),
        ConstantNode.True,
        ConstantNode.False,
        new NotNode(new VariableNode("a")),
        new AndNode(new VariableNode("a"), new VariableNode("b")),
        new OrNode(new VariableNode("a"), new VariableNode("b")),
        new XorNode(new VariableNode("a"), new VariableNode("b")),
        new NandNode(new VariableNode("a"), new VariableNode("b")),
        new NorNode(new VariableNode("a"), new VariableNode("b")),
        new EqvNode(new VariableNode("a"), new VariableNode("b")),
        new ImpNode(new VariableNode("a"), new VariableNode("b"))
    };

    [Theory]
    [MemberData(nameof(AllNodeKinds))]
    public void Clone_ReturnsSameInstance(AstNode node)
    {
        Assert.Same(node, node.Clone());
    }

    #endregion

    #region Derived binary node contracts

    [Theory]
    [MemberData(nameof(DerivedNodeFactories))]
    public void Derived_ToString_SimpleOperands_UsesOperatorSymbol(string symbol,
        Func<AstNode, AstNode, BinaryNode> factory)
    {
        var node = factory(new VariableNode("a"), new VariableNode("b"));

        Assert.Equal(symbol, node.Operator);
        Assert.Equal($"a {symbol} b", node.ToString());
    }

    [Theory]
    [MemberData(nameof(DerivedNodeFactories))]
    public void Derived_ExposesOperandsAndEquality(string symbol,
        Func<AstNode, AstNode, BinaryNode> factory)
    {
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var node = factory(a, b);

        Assert.Equal(node, factory(new VariableNode("a"), new VariableNode("b")));
        Assert.False(node.Equals(factory(b, a)), $"{symbol}: operand order must matter for equality");
        Assert.Equal(node.GetHashCode(), factory(a, b).GetHashCode());
    }

    [Theory]
    [MemberData(nameof(DerivedNodeFactories))]
    public void Derived_GetVariables_ReturnsAllVariablesFromBothOperands(string symbol,
        Func<AstNode, AstNode, BinaryNode> factory)
    {
        var node = factory(
            new VariableNode("x"),
            new AndNode(new VariableNode("y"), new VariableNode("z")));

        Assert.Equal(symbol, node.Operator);
        Assert.Equal(new[] { "x", "y", "z" }, node.GetVariables().OrderBy(v => v));
    }

    #endregion

    #region AstFormatter rendering rules

    [Fact]
    public void Format_NaryChainsRenderFlat()
    {
        var and = new AndNode(new AstNode[]
            { new VariableNode("a"), new VariableNode("b"), new VariableNode("c") });
        var or = new OrNode(new AstNode[]
            { new VariableNode("a"), new VariableNode("b"), new VariableNode("c") });

        Assert.Equal("a & b & c", and.ToString());
        Assert.Equal("a | b | c", or.ToString());
    }

    [Fact]
    public void Format_OrUnderAnd_IsParenthesized_AndUnderOrIsNot()
    {
        var orUnderAnd = new AndNode(
            new OrNode(new VariableNode("a"), new VariableNode("b")),
            new VariableNode("c"));
        var andUnderOr = new OrNode(
            new AndNode(new VariableNode("a"), new VariableNode("b")),
            new VariableNode("c"));

        Assert.Equal("(a | b) & c", orUnderAnd.ToString());
        Assert.Equal("a & b | c", andUnderOr.ToString());
    }

    [Fact]
    public void Format_NotOverCompound_UsesParentheses()
    {
        var variable = new VariableNode("a");

        Assert.Equal("!a", new NotNode(variable).ToString());
        Assert.Equal("!!a", new NotNode(new NotNode(variable)).ToString());
        Assert.Equal("!(a & b)", new NotNode(new AndNode(variable, new VariableNode("b"))).ToString());
        Assert.Equal("!(a | b)", new NotNode(new OrNode(variable, new VariableNode("b"))).ToString());
        Assert.Equal("!(a XOR b)", new NotNode(new XorNode(variable, new VariableNode("b"))).ToString());
    }

    [Theory]
    [MemberData(nameof(DerivedNodeFactories))]
    public void Format_DerivedOperators_ParenthesizeCompoundButNotNotOrAtoms(string symbol,
        Func<AstNode, AstNode, BinaryNode> factory)
    {
        var withNotOperand = factory(new VariableNode("x"), new NotNode(new VariableNode("y")));
        var withAndOrOperands = factory(
            new AndNode(new VariableNode("a"), new VariableNode("b")),
            new OrNode(new VariableNode("c"), new VariableNode("d")));
        var nested = factory(withNotOperand, withAndOrOperands);

        Assert.Equal($"x {symbol} !y", withNotOperand.ToString());
        Assert.Equal($"(a & b) {symbol} (c | d)", withAndOrOperands.ToString());
        Assert.Equal($"(x {symbol} !y) {symbol} ((a & b) {symbol} (c | d))", nested.ToString());
    }

    [Theory]
    [MemberData(nameof(DerivedNodeFactories))]
    public void Format_NestedSameDerivedOperator_ParenthesizesInnerNode(string symbol,
        Func<AstNode, AstNode, BinaryNode> factory)
    {
        var inner = factory(new VariableNode("a"), new VariableNode("b"));
        var outer = factory(inner, new VariableNode("c"));

        Assert.Equal($"(a {symbol} b) {symbol} c", outer.ToString());
    }

    [Fact]
    public void Format_MixedDerivedOperators_ParenthesizesNestedOperator()
    {
        // ((a XOR b) ~& c) mixes two derived operator node types
        var xorNode = new XorNode(new VariableNode("a"), new VariableNode("b"));
        var nandNode = new NandNode(xorNode, new VariableNode("c"));

        Assert.Equal("(a XOR b) ~& c", nandNode.ToString());
    }

    #endregion
}
