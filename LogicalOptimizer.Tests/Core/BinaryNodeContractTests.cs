using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Parameterized contract tests for the advanced binary operator nodes
/// (XorNode, NandNode, NorNode, EqvNode, ImpNode). All of them share the
/// BinaryNode contract: operator symbol and ToString formatting,
/// ForceParentheses behavior, deep cloning, variable collection and
/// parenthesization of complex operands.
/// </summary>
public class BinaryNodeContractTests
{
    public static TheoryData<string, Func<AstNode, AstNode, bool, BinaryNode>> NodeFactories => new()
    {
        { "XOR", (left, right, forceParens) => new XorNode(left, right, forceParens) },
        { "~&", (left, right, forceParens) => new NandNode(left, right, forceParens) },
        { "~|", (left, right, forceParens) => new NorNode(left, right, forceParens) },
        { "↔", (left, right, forceParens) => new EqvNode(left, right, forceParens) },
        { "→", (left, right, forceParens) => new ImpNode(left, right, forceParens) }
    };

    [Theory]
    [MemberData(nameof(NodeFactories))]
    public void ToString_SimpleOperands_UsesOperatorSymbol(string symbol,
        Func<AstNode, AstNode, bool, BinaryNode> factory)
    {
        // Arrange
        var node = factory(new VariableNode("a"), new VariableNode("b"), false);

        // Assert
        Assert.Equal(symbol, node.Operator);
        Assert.Equal($"a {symbol} b", node.ToString());
    }

    [Theory]
    [MemberData(nameof(NodeFactories))]
    public void ForceParentheses_Toggle_ChangesFormattingBothWays(string symbol,
        Func<AstNode, AstNode, bool, BinaryNode> factory)
    {
        // Arrange - constructed with parentheses forced
        var node = factory(new VariableNode("a"), new VariableNode("b"), true);
        Assert.True(node.ForceParentheses);
        Assert.Equal($"(a {symbol} b)", node.ToString());

        // Act & Assert - toggle off
        node.ForceParentheses = false;
        Assert.Equal($"a {symbol} b", node.ToString());

        // Act & Assert - toggle back on
        node.ForceParentheses = true;
        Assert.Equal($"(a {symbol} b)", node.ToString());
    }

    [Theory]
    [MemberData(nameof(NodeFactories))]
    public void Clone_CreatesIndependentDeepCopy(string symbol,
        Func<AstNode, AstNode, bool, BinaryNode> factory)
    {
        // Arrange
        var original = factory(new VariableNode("a"), new VariableNode("b"), true);

        // Act
        var cloned = original.Clone();

        // Assert - same concrete type, independent instances
        Assert.IsType(original.GetType(), cloned);
        var clonedBinary = (BinaryNode)cloned;
        Assert.NotSame(original, cloned);
        Assert.NotSame(original.Left, clonedBinary.Left);
        Assert.NotSame(original.Right, clonedBinary.Right);

        // Assert - operands equal, ForceParentheses preserved
        Assert.Equal(original.Left, clonedBinary.Left);
        Assert.Equal(original.Right, clonedBinary.Right);
        Assert.True(clonedBinary.ForceParentheses);
        Assert.Equal(original.ToString(), cloned.ToString());
        Assert.Equal($"(a {symbol} b)", cloned.ToString());
    }

    [Theory]
    [MemberData(nameof(NodeFactories))]
    public void GetVariables_ReturnsAllVariablesFromBothOperands(string symbol,
        Func<AstNode, AstNode, bool, BinaryNode> factory)
    {
        // Arrange
        var node = factory(
            new VariableNode("x"),
            new AndNode(new VariableNode("y"), new VariableNode("z")),
            false);

        // Act
        var variables = node.GetVariables();

        // Assert
        Assert.Equal(symbol, node.Operator);
        Assert.Equal(new[] { "x", "y", "z" }, variables.OrderBy(v => v));
    }

    [Theory]
    [MemberData(nameof(NodeFactories))]
    public void ToString_WithComplexOperands_ParenthesizesBinaryChildren(string symbol,
        Func<AstNode, AstNode, bool, BinaryNode> factory)
    {
        // Arrange
        var withNotOperand = factory(new VariableNode("x"), new NotNode(new VariableNode("y")), false);
        var withAndOrOperands = factory(
            new AndNode(new VariableNode("a"), new VariableNode("b")),
            new OrNode(new VariableNode("c"), new VariableNode("d")),
            false);
        var nested = factory(withNotOperand, withAndOrOperands, false);

        // Assert
        Assert.Equal($"x {symbol} !y", withNotOperand.ToString());
        Assert.Equal($"(a & b) {symbol} (c | d)", withAndOrOperands.ToString());
        Assert.Equal($"(x {symbol} !y) {symbol} ((a & b) {symbol} (c | d))", nested.ToString());
    }

    [Theory]
    [MemberData(nameof(NodeFactories))]
    public void ToString_NestedSameOperator_ParenthesizesInnerNode(string symbol,
        Func<AstNode, AstNode, bool, BinaryNode> factory)
    {
        // Arrange
        var inner = factory(new VariableNode("a"), new VariableNode("b"), false);
        var outer = factory(inner, new VariableNode("c"), false);

        // Assert
        Assert.Equal($"(a {symbol} b) {symbol} c", outer.ToString());
    }

    [Fact]
    public void ToString_MixedExtendedOperators_ParenthesizesNestedOperator()
    {
        // Arrange - ((a XOR b) ~& c) mixes two extended operator node types
        var xorNode = new XorNode(new VariableNode("a"), new VariableNode("b"));
        var nandNode = new NandNode(xorNode, new VariableNode("c"), true);

        // Assert
        Assert.Equal("((a XOR b) ~& c)", nandNode.ToString());
    }
}
