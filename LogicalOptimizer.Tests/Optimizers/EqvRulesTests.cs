using LogicalOptimizer;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for EQV optimization rules.
///     <para>
///         These pin WHICH node each rule returns (and that it declines to fire otherwise). They are
///         deliberately NOT the only evidence: a shape read off the implementation cannot detect a
///         wrong constant or a flipped polarity, so the semantics of every
///         <c>ExtendedOptimizationRules</c> rule — EQV included — is swept against a truth-table
///         oracle by <c>ExtendedOptimizationRulesTests.EveryRule_WhenItFires_PreservesSemantics</c>.
///         Add new rules to that sweep, not just here.
///     </para>
/// </summary>
public class EqvRulesTests
{
    [Fact]
    public void IdentityWithZero_LeftIsZero_ReturnsNegatedRight()
    {
        // Arrange
        var zero = new ConstantNode(false);
        var a = new VariableNode("a");
        var node = new EqvNode(zero, a);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.IdentityWithZero(node);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<NotNode>(result);
        var notNode = (NotNode)result;
        Assert.Same(a, notNode.Operand);
    }

    [Fact]
    public void IdentityWithZero_RightIsZero_ReturnsNegatedLeft()
    {
        // Arrange
        var a = new VariableNode("a");
        var zero = new ConstantNode(false);
        var node = new EqvNode(a, zero);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.IdentityWithZero(node);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<NotNode>(result);
        var notNode = (NotNode)result;
        Assert.Same(a, notNode.Operand);
    }

    [Fact]
    public void IdentityWithZero_NoZero_ReturnsNull()
    {
        // Arrange
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var node = new EqvNode(a, b);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.IdentityWithZero(node);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void IdentityWithZero_BothZero_ReturnsNegatedZero()
    {
        // Arrange - (0 ↔ 0): left zero wins, rule returns negated right operand
        var node = new EqvNode(new ConstantNode(false), new ConstantNode(false));

        // Act
        var result = ExtendedOptimizationRules.EqvRules.IdentityWithZero(node);

        // Assert
        Assert.NotNull(result);
        var notNode = Assert.IsType<NotNode>(result);
        Assert.Equal("0", notNode.Operand.ToString());
    }

    [Fact]
    public void ComplementLaw_LeftIsNegatedRight_ReturnsZero()
    {
        // Arrange
        var a = new VariableNode("a");
        var notA = new NotNode(a);
        var node = new EqvNode(notA, a);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.ComplementLaw(node);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("0", result.ToString());
    }

    [Fact]
    public void ComplementLaw_RightIsNegatedLeft_ReturnsZero()
    {
        // Arrange
        var a = new VariableNode("a");
        var notA = new NotNode(a);
        var node = new EqvNode(a, notA);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.ComplementLaw(node);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("0", result.ToString());
    }

    [Fact]
    public void ComplementLaw_NoComplement_ReturnsNull()
    {
        // Arrange
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var node = new EqvNode(a, b);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.ComplementLaw(node);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ComplementLaw_DoubleNegation_DoesNotApply()
    {
        // Arrange - x ↔ !!x: the rule matches only a single negation level,
        // so double negation must not trigger it
        var x = new VariableNode("x");
        var doubleNot = new NotNode(new NotNode(x));
        var node = new EqvNode(x, doubleNot);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.ComplementLaw(node);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ToBasicOperators_ConvertsEqvToBasicForm()
    {
        // Arrange
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var node = new EqvNode(a, b);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.ToBasicOperators(node);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<OrNode>(result);
        var orNode = (OrNode)result;

        // Should be (a & b) | (!a & !b)
        Assert.Equal(2, orNode.Operands.Count);
        Assert.IsType<AndNode>(orNode.Operands[0]);
        Assert.IsType<AndNode>(orNode.Operands[1]);

        var leftAnd = (AndNode)orNode.Operands[0];
        var rightAnd = (AndNode)orNode.Operands[1];

        // Left side: a & b
        Assert.Same(a, leftAnd.Operands[0]);
        Assert.Same(b, leftAnd.Operands[1]);

        // Right side: !a & !b
        Assert.IsType<NotNode>(rightAnd.Operands[0]);
        Assert.IsType<NotNode>(rightAnd.Operands[1]);
        Assert.Same(a, ((NotNode)rightAnd.Operands[0]).Operand);
        Assert.Same(b, ((NotNode)rightAnd.Operands[1]).Operand);
    }

    [Theory]
    [InlineData("a", "a", true)]  // Same variables should trigger reflexivity
    [InlineData("x", "y", false)] // Different variables should not
    public void ReflexivityLaw_Theory_Test(string leftVar, string rightVar, bool shouldOptimize)
    {
        // Arrange
        var left = new VariableNode(leftVar);
        var right = new VariableNode(rightVar);
        var node = new EqvNode(left, right);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.ReflexivityLaw(node);

        // Assert
        if (shouldOptimize)
        {
            Assert.NotNull(result);
            Assert.Equal("1", result.ToString());
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData("1", "a", "a")]
    [InlineData("a", "1", "a")]
    [InlineData("1", "1", "1")]
    [InlineData("0", "a", null)]  // Should not apply identity with one rule
    [InlineData("a", "b", null)]  // No constant one present
    public void IdentityWithOne_Theory_Test(string leftVar, string rightVar, string? expectedResult)
    {
        // Arrange
        var left = CreateOperand(leftVar);
        var right = CreateOperand(rightVar);
        var node = new EqvNode(left, right);

        // Act
        var result = ExtendedOptimizationRules.EqvRules.IdentityWithOne(node);

        // Assert
        if (expectedResult != null)
        {
            Assert.NotNull(result);
            Assert.Equal(expectedResult, result.ToString());
        }
        else
        {
            Assert.Null(result);
        }
    }

    private static AstNode CreateOperand(string name)
    {
        return name switch
        {
            "1" => ConstantNode.True,
            "0" => ConstantNode.False,
            _ => new VariableNode(name)
        };
    }
}
