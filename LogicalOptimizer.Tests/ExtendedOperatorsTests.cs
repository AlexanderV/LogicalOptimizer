using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for ExtendedOperators (XorNode, NandNode, NorNode) - testing to improve CRAP Score by increasing coverage
/// </summary>
public class ExtendedOperatorsTests
{
    #region XorNode Tests

    [Fact]
    public void XorNode_Constructor_ShouldSetLeftAndRightOperands()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var xorNode = new XorNode(left, right);

        // Assert
        Assert.Equal(left, xorNode.Left);
        Assert.Equal(right, xorNode.Right);
        Assert.False(xorNode.ForceParentheses);
    }

    [Fact]
    public void XorNode_ConstructorWithForceParens_ShouldSetForceParentheses()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var xorNode = new XorNode(left, right, true);

        // Assert
        Assert.Equal(left, xorNode.Left);
        Assert.Equal(right, xorNode.Right);
        Assert.True(xorNode.ForceParentheses);
    }

    [Fact]
    public void XorNode_Operator_ShouldReturnXOR()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var xorNode = new XorNode(left, right);

        // Act
        var operatorSymbol = xorNode.Operator;

        // Assert
        Assert.Equal("XOR", operatorSymbol);
    }

    [Fact]
    public void XorNode_ToString_ShouldReturnCorrectFormat()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var xorNode = new XorNode(left, right);

        // Act
        var result = xorNode.ToString();

        // Assert
        Assert.Equal("a XOR b", result);
    }

    [Fact]
    public void XorNode_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var original = new XorNode(left, right, true);

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.IsType<XorNode>(cloned);
        var clonedXor = (XorNode)cloned;
        
        Assert.NotSame(original, cloned);
        Assert.NotSame(original.Left, clonedXor.Left);
        Assert.NotSame(original.Right, clonedXor.Right);
        Assert.Equal(original.ForceParentheses, clonedXor.ForceParentheses);
        Assert.Equal(original.ToString(), cloned.ToString());
    }

    [Fact]
    public void XorNode_ForceParenthesesProperty_ShouldBeSettable()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var xorNode = new XorNode(left, right);

        // Act
        xorNode.ForceParentheses = true;

        // Assert
        Assert.True(xorNode.ForceParentheses);
    }

    #endregion

    #region NandNode Tests

    [Fact]
    public void NandNode_Constructor_ShouldSetLeftAndRightOperands()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var nandNode = new NandNode(left, right);

        // Assert
        Assert.Equal(left, nandNode.Left);
        Assert.Equal(right, nandNode.Right);
        Assert.False(nandNode.ForceParentheses);
    }

    [Fact]
    public void NandNode_ConstructorWithForceParens_ShouldSetForceParentheses()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var nandNode = new NandNode(left, right, true);

        // Assert
        Assert.Equal(left, nandNode.Left);
        Assert.Equal(right, nandNode.Right);
        Assert.True(nandNode.ForceParentheses);
    }

    [Fact]
    public void NandNode_Operator_ShouldReturnNandSymbol()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var nandNode = new NandNode(left, right);

        // Act
        var operatorSymbol = nandNode.Operator;

        // Assert
        Assert.Equal("~&", operatorSymbol);
    }

    [Fact]
    public void NandNode_ToString_WithoutForceParentheses_ShouldReturnCorrectFormat()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var nandNode = new NandNode(left, right);

        // Act
        var result = nandNode.ToString();

        // Assert
        Assert.Equal("a ~& b", result);
    }

    [Fact]
    public void NandNode_ToString_WithForceParentheses_ShouldIncludeParentheses()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var nandNode = new NandNode(left, right, true);

        // Act
        var result = nandNode.ToString();

        // Assert
        Assert.Equal("(a ~& b)", result);
    }

    [Fact]
    public void NandNode_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var original = new NandNode(left, right, true);

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.IsType<NandNode>(cloned);
        var clonedNand = (NandNode)cloned;
        
        Assert.NotSame(original, cloned);
        Assert.NotSame(original.Left, clonedNand.Left);
        Assert.NotSame(original.Right, clonedNand.Right);
        Assert.Equal(original.ForceParentheses, clonedNand.ForceParentheses);
        Assert.Equal(original.ToString(), cloned.ToString());
    }

    #endregion

    #region NorNode Tests

    [Fact]
    public void NorNode_Constructor_ShouldSetLeftAndRightOperands()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var norNode = new NorNode(left, right);

        // Assert
        Assert.Equal(left, norNode.Left);
        Assert.Equal(right, norNode.Right);
        Assert.False(norNode.ForceParentheses);
    }

    [Fact]
    public void NorNode_ConstructorWithForceParens_ShouldSetForceParentheses()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");

        // Act
        var norNode = new NorNode(left, right, true);

        // Assert
        Assert.Equal(left, norNode.Left);
        Assert.Equal(right, norNode.Right);
        Assert.True(norNode.ForceParentheses);
    }

    [Fact]
    public void NorNode_Operator_ShouldReturnNorSymbol()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var norNode = new NorNode(left, right);

        // Act
        var operatorSymbol = norNode.Operator;

        // Assert
        Assert.Equal("~|", operatorSymbol);
    }

    [Fact]
    public void NorNode_ToString_WithoutForceParentheses_ShouldReturnCorrectFormat()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var norNode = new NorNode(left, right);

        // Act
        var result = norNode.ToString();

        // Assert
        Assert.Equal("a ~| b", result);
    }

    [Fact]
    public void NorNode_ToString_WithForceParentheses_ShouldIncludeParentheses()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var norNode = new NorNode(left, right, true);

        // Act
        var result = norNode.ToString();

        // Assert
        Assert.Equal("(a ~| b)", result);
    }

    [Fact]
    public void NorNode_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var original = new NorNode(left, right, true);

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.IsType<NorNode>(cloned);
        var clonedNor = (NorNode)cloned;
        
        Assert.NotSame(original, cloned);
        Assert.NotSame(original.Left, clonedNor.Left);
        Assert.NotSame(original.Right, clonedNor.Right);
        Assert.Equal(original.ForceParentheses, clonedNor.ForceParentheses);
        Assert.Equal(original.ToString(), cloned.ToString());
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ExtendedOperators_NestedOperations_ShouldFormatCorrectly()
    {
        // Arrange
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");
        
        var xorNode = new XorNode(a, b);
        var nandNode = new NandNode(xorNode, c, true);

        // Act
        var result = nandNode.ToString();

        // Assert
        Assert.Equal("(a XOR b ~& c)", result);
    }

    [Fact]
    public void ExtendedOperators_WithComplexOperands_ShouldHandleCorrectly()
    {
        // Arrange
        var left = new AndNode(new VariableNode("a"), new VariableNode("b"));
        var right = new OrNode(new VariableNode("c"), new VariableNode("d"));
        
        var xorNode = new XorNode(left, right);
        var nandNode = new NandNode(left, right, true);
        var norNode = new NorNode(left, right, true);

        // Act
        var xorResult = xorNode.ToString();
        var nandResult = nandNode.ToString();
        var norResult = norNode.ToString();

        // Assert
        Assert.Equal("a & b XOR c | d", xorResult);
        Assert.Equal("(a & b ~& c | d)", nandResult);
        Assert.Equal("(a & b ~| c | d)", norResult);
    }

    [Fact]
    public void ExtendedOperators_ForceParenthesesProperty_ShouldBeModifiable()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new VariableNode("y");
        
        var nandNode = new NandNode(left, right, false);
        var norNode = new NorNode(left, right, false);

        // Act
        nandNode.ForceParentheses = true;
        norNode.ForceParentheses = true;

        // Assert
        Assert.True(nandNode.ForceParentheses);
        Assert.True(norNode.ForceParentheses);
        Assert.Equal("(x ~& y)", nandNode.ToString());
        Assert.Equal("(x ~| y)", norNode.ToString());
    }

    [Fact]
    public void ExtendedOperators_InheritanceFromBinaryNode_ShouldWorkCorrectly()
    {
        // Arrange
        var left = new VariableNode("p");
        var right = new VariableNode("q");

        // Act
        var xorNode = new XorNode(left, right);
        var nandNode = new NandNode(left, right);
        var norNode = new NorNode(left, right);

        // Assert
        Assert.IsAssignableFrom<BinaryNode>(xorNode);
        Assert.IsAssignableFrom<BinaryNode>(nandNode);
        Assert.IsAssignableFrom<BinaryNode>(norNode);
        Assert.IsAssignableFrom<AstNode>(xorNode);
        Assert.IsAssignableFrom<AstNode>(nandNode);
        Assert.IsAssignableFrom<AstNode>(norNode);
    }

    #region Additional Coverage Tests

    [Fact]
    public void NandNode_ComplexExpression_ShouldFormatCorrectly()
    {
        // Arrange
        var left = new AndNode(new VariableNode("a"), new VariableNode("b"));
        var right = new OrNode(new VariableNode("c"), new VariableNode("d"));
        var nandNode = new NandNode(left, right);

        // Act
        var result = nandNode.ToString();

        // Assert
        Assert.Equal("a & b ~& c | d", result);
    }

    [Fact]
    public void NandNode_WithForceParentheses_ShouldAddParentheses()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var nandNode = new NandNode(left, right, true);

        // Act
        var result = nandNode.ToString();

        // Assert
        Assert.Equal("(a ~& b)", result);
    }

    [Fact]
    public void NandNode_GetVariables_ShouldReturnAllVariables()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new AndNode(new VariableNode("y"), new VariableNode("z"));
        var nandNode = new NandNode(left, right);

        // Act
        var variables = nandNode.GetVariables();

        // Assert
        Assert.Equal(3, variables.Count);
        Assert.Contains("x", variables);
        Assert.Contains("y", variables);
        Assert.Contains("z", variables);
    }

    [Fact]
    public void NorNode_ComplexExpression_ShouldFormatCorrectly()
    {
        // Arrange
        var left = new AndNode(new VariableNode("p"), new VariableNode("q"));
        var right = new OrNode(new VariableNode("r"), new VariableNode("s"));
        var norNode = new NorNode(left, right);

        // Act
        var result = norNode.ToString();

        // Assert
        Assert.Equal("p & q ~| r | s", result);
    }

    [Fact]
    public void NorNode_WithForceParentheses_ShouldAddParentheses()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new VariableNode("y");
        var norNode = new NorNode(left, right, true);

        // Act
        var result = norNode.ToString();

        // Assert
        Assert.Equal("(x ~| y)", result);
    }

    [Fact]
    public void NorNode_GetVariables_ShouldReturnAllVariables()
    {
        // Arrange
        var left = new VariableNode("alpha");
        var right = new OrNode(new VariableNode("beta"), new VariableNode("gamma"));
        var norNode = new NorNode(left, right);

        // Act
        var variables = norNode.GetVariables();

        // Assert
        Assert.Equal(3, variables.Count);
        Assert.Contains("alpha", variables);
        Assert.Contains("beta", variables);
        Assert.Contains("gamma", variables);
    }

    [Fact]
    public void NandNode_Operator_ShouldReturnCorrectOperator()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var nandNode = new NandNode(left, right);

        // Act
        var operatorSymbol = nandNode.Operator;

        // Assert
        Assert.Equal("~&", operatorSymbol);
    }

    [Fact]
    public void NorNode_Operator_ShouldReturnCorrectOperator()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new VariableNode("y");
        var norNode = new NorNode(left, right);

        // Act
        var operatorSymbol = norNode.Operator;

        // Assert
        Assert.Equal("~|", operatorSymbol);
    }

    [Fact]
    public void NandNode_CloneWithForceParentheses_ShouldCreateDeepCopy()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var original = new NandNode(left, right, true);

        // Act
        var clone = (NandNode)original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.NotSame(original.Left, clone.Left);
        Assert.NotSame(original.Right, clone.Right);
        Assert.Equal(original.ForceParentheses, clone.ForceParentheses);
        Assert.Equal(original.ToString(), clone.ToString());
    }

    [Fact]
    public void NorNode_CloneWithForceParentheses_ShouldCreateDeepCopy()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new VariableNode("y");
        var original = new NorNode(left, right, true);

        // Act
        var clone = (NorNode)original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.NotSame(original.Left, clone.Left);
        Assert.NotSame(original.Right, clone.Right);
        Assert.Equal(original.ForceParentheses, clone.ForceParentheses);
        Assert.Equal(original.ToString(), clone.ToString());
    }

    [Fact]
    public void NandNode_ForceParenthesesProperty_ShouldWorkCorrectly()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var nandNode = new NandNode(left, right);

        // Act & Assert
        Assert.False(nandNode.ForceParentheses);
        
        nandNode.ForceParentheses = true;
        Assert.True(nandNode.ForceParentheses);
        Assert.Equal("(a ~& b)", nandNode.ToString());
        
        nandNode.ForceParentheses = false;
        Assert.False(nandNode.ForceParentheses);
        Assert.Equal("a ~& b", nandNode.ToString());
    }

    [Fact]
    public void NorNode_ForceParenthesesProperty_ShouldWorkCorrectly()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new VariableNode("y");
        var norNode = new NorNode(left, right);

        // Act & Assert
        Assert.False(norNode.ForceParentheses);
        
        norNode.ForceParentheses = true;
        Assert.True(norNode.ForceParentheses);
        Assert.Equal("(x ~| y)", norNode.ToString());
        
        norNode.ForceParentheses = false;
        Assert.False(norNode.ForceParentheses);
        Assert.Equal("x ~| y", norNode.ToString());
    }

    #endregion

    #endregion
}
