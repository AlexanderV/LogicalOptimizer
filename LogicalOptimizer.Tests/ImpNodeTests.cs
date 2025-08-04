using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for ImpNode (Implication Node) - testing to improve CRAP Score by increasing coverage from 60%
/// </summary>
public class ImpNodeTests
{
    [Fact]
    public void ImpNode_Constructor_ShouldSetLeftAndRightOperands()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var impNode = new ImpNode(left, right);

        // Assert
        Assert.Equal(left, impNode.Left);
        Assert.Equal(right, impNode.Right);
    }

    [Fact]
    public void ImpNode_Operator_ShouldReturnImplicationSymbol()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var impNode = new ImpNode(left, right);

        // Act
        var operatorSymbol = impNode.Operator;

        // Assert
        Assert.Equal("→", operatorSymbol);
    }

    [Fact]
    public void ImpNode_ToString_ShouldReturnCorrectFormat()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var impNode = new ImpNode(left, right);

        // Act
        var result = impNode.ToString();

        // Assert
        Assert.Equal("a → b", result);
    }

    [Fact]
    public void ImpNode_ToString_WithComplexOperands_ShouldFormatCorrectly()
    {
        // Arrange
        var left = new AndNode(new VariableNode("a"), new VariableNode("b"));
        var right = new OrNode(new VariableNode("c"), new VariableNode("d"));
        var impNode = new ImpNode(left, right);

        // Act
        var result = impNode.ToString();

        // Assert
        Assert.Equal("a & b → c | d", result);
    }

    [Fact]
    public void ImpNode_ToString_WithNotOperands_ShouldFormatCorrectly()
    {
        // Arrange
        var left = new NotNode(new VariableNode("a"));
        var right = new VariableNode("b");
        var impNode = new ImpNode(left, right);

        // Act
        var result = impNode.ToString();

        // Assert
        Assert.Equal("!a → b", result);
    }

    [Fact]
    public void ImpNode_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var original = new ImpNode(left, right);

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.IsType<ImpNode>(cloned);
        var clonedImp = (ImpNode)cloned;
        
        Assert.NotSame(original, cloned);
        Assert.NotSame(original.Left, clonedImp.Left);
        Assert.NotSame(original.Right, clonedImp.Right);
        
        Assert.Equal(original.ToString(), cloned.ToString());
    }

    [Fact]
    public void ImpNode_Clone_WithComplexOperands_ShouldCloneCorrectly()
    {
        // Arrange
        var left = new AndNode(new VariableNode("a"), new NotNode(new VariableNode("b")));
        var right = new OrNode(new VariableNode("c"), new VariableNode("d"));
        var original = new ImpNode(left, right);

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.IsType<ImpNode>(cloned);
        var clonedImp = (ImpNode)cloned;
        
        Assert.NotSame(original, cloned);
        Assert.NotSame(original.Left, clonedImp.Left);
        Assert.NotSame(original.Right, clonedImp.Right);
        
        Assert.Equal(original.ToString(), cloned.ToString());
        Assert.Equal("a & !b → c | d", cloned.ToString());
    }

    [Fact]
    public void ImpNode_InheritanceFromBinaryNode_ShouldWorkCorrectly()
    {
        // Arrange
        var left = new VariableNode("p");
        var right = new VariableNode("q");
        var impNode = new ImpNode(left, right);

        // Act & Assert
        Assert.IsAssignableFrom<BinaryNode>(impNode);
        Assert.IsAssignableFrom<AstNode>(impNode);
    }

    [Fact]
    public void ImpNode_WithNestedImplications_ShouldFormatCorrectly()
    {
        // Arrange
        var innerImp = new ImpNode(new VariableNode("a"), new VariableNode("b"));
        var outerImp = new ImpNode(innerImp, new VariableNode("c"));

        // Act
        var result = outerImp.ToString();

        // Assert
        Assert.Equal("a → b → c", result);
    }

    [Fact]
    public void ImpNode_WithVariousNodeTypes_ShouldHandleAllCases()
    {
        // Arrange
        var variable = new VariableNode("x");
        var notNode = new NotNode(new VariableNode("y"));
        var andNode = new AndNode(new VariableNode("a"), new VariableNode("b"));
        var orNode = new OrNode(new VariableNode("c"), new VariableNode("d"));

        // Act
        var imp1 = new ImpNode(variable, notNode);
        var imp2 = new ImpNode(andNode, orNode);
        var imp3 = new ImpNode(imp1, imp2);

        // Assert
        Assert.Equal("x → !y", imp1.ToString());
        Assert.Equal("a & b → c | d", imp2.ToString());
        Assert.Equal("x → !y → a & b → c | d", imp3.ToString());
    }

    [Fact]
    public void ImpNode_Clone_ShouldPreserveOperatorType()
    {
        // Arrange
        var left = new VariableNode("test1");
        var right = new VariableNode("test2");
        var original = new ImpNode(left, right);

        // Act
        var cloned = (ImpNode)original.Clone();

        // Assert
        Assert.Equal("→", cloned.Operator);
        Assert.Equal(original.Operator, cloned.Operator);
    }
}
