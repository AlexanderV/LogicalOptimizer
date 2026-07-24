using LogicalOptimizer;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for EQV pattern recognition in PatternRecognizer
/// </summary>
public class EqvPatternRecognizerTests
{
    [Fact]
    public void TryReplaceWithEqv_ReversedPattern_ReturnsEqvNode()
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var notA = new NotNode(a);
        var notB = new NotNode(b);

        // Create reversed pattern: (!a & !b) | (a & b)
        var leftAnd = new AndNode(notA, notB);
        var rightAnd = new AndNode(a, b);
        var orNode = new OrNode(leftAnd, rightAnd);

        // Act
        var result = recognizer.TryReplaceWithEqv(orNode);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<EqvNode>(result);
        var eqvNode = (EqvNode)result;
        Assert.Equal("a", ((VariableNode)eqvNode.Left).Name);
        Assert.Equal("b", ((VariableNode)eqvNode.Right).Name);
    }

    [Fact]
    public void TryReplaceWithEqv_InvalidPattern_ReturnsNull()
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");

        // Create invalid pattern: (a & b) | (a & c)
        var leftAnd = new AndNode(a, b);
        var rightAnd = new AndNode(a, c);
        var orNode = new OrNode(leftAnd, rightAnd);

        // Act
        var result = recognizer.TryReplaceWithEqv(orNode);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void IsEqvPattern_ValidPattern_ReturnsTrue()
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var notA = new NotNode(a);
        var notB = new NotNode(b);

        var leftAnd = new AndNode(a, b);
        var rightAnd = new AndNode(notA, notB);

        // Act
        var result = recognizer.IsEqvPattern(leftAnd, rightAnd, out var x, out var y);

        // Assert
        Assert.True(result);
        Assert.NotNull(x);
        Assert.NotNull(y);
        Assert.Equal("a", ((VariableNode)x).Name);
        Assert.Equal("b", ((VariableNode)y).Name);
    }

    [Fact]
    public void IsEqvPattern_ReversedPattern_ReturnsTrue()
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var notA = new NotNode(a);
        var notB = new NotNode(b);

        var leftAnd = new AndNode(notA, notB);
        var rightAnd = new AndNode(a, b);

        // Act
        var result = recognizer.IsEqvPattern(leftAnd, rightAnd, out var x, out var y);

        // Assert
        Assert.True(result);
        Assert.NotNull(x);
        Assert.NotNull(y);
        Assert.Equal("a", ((VariableNode)x).Name);
        Assert.Equal("b", ((VariableNode)y).Name);
    }

    [Fact]
    public void IsEqvPattern_InvalidPattern_ReturnsFalse()
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");

        var leftAnd = new AndNode(a, b);
        var rightAnd = new AndNode(a, c);

        // Act
        var result = recognizer.IsEqvPattern(leftAnd, rightAnd, out var x, out var y);

        // Assert
        Assert.False(result);
        Assert.Null(x);
        Assert.Null(y);
    }

    [Fact]
    public void ExtractEqvAndParts_NonAndNode_ReturnsFalse()
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var orNode = new OrNode(a, b);

        // Act
        var result = recognizer.ExtractEqvAndParts(orNode, out var left, out var right);

        // Assert
        Assert.False(result);
        Assert.Null(left);
        Assert.Null(right);
    }

    [Theory]
    [InlineData("a", "b")]
    [InlineData("p", "q")]
    [InlineData("x", "y")]
    [InlineData("var1", "var2")]
    public void EqvPatternRecognition_TheoryTest(string var1Name, string var2Name)
    {
        // Arrange
        var recognizer = new PatternRecognizer();
        var var1 = new VariableNode(var1Name);
        var var2 = new VariableNode(var2Name);
        var notVar1 = new NotNode(var1);
        var notVar2 = new NotNode(var2);

        // Create EQV pattern: (var1 & var2) | (!var1 & !var2)
        var positiveCase = new AndNode(var1, var2);
        var negativeCase = new AndNode(notVar1, notVar2);
        var eqvPattern = new OrNode(positiveCase, negativeCase);

        // Act
        var result = recognizer.TryReplaceWithEqv(eqvPattern);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<EqvNode>(result);
        var eqvNode = (EqvNode)result;
        Assert.Equal(var1Name, ((VariableNode)eqvNode.Left).Name);
        Assert.Equal(var2Name, ((VariableNode)eqvNode.Right).Name);
    }

    [Fact]
    public void EqvPattern_WithCommutativeAnd_RecognizedCorrectly()
    {
        // Test that pattern recognition works with commutative AND operations

        // Arrange
        var recognizer = new PatternRecognizer();
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var notA = new NotNode(a);
        var notB = new NotNode(b);

        // Create pattern with both ANDs commuted: (b & a) | (!b & !a)
        var leftAnd = new AndNode(b, a);  // Commuted
        var rightAnd = new AndNode(notB, notA);  // Commuted
        var eqvPattern = new OrNode(leftAnd, rightAnd);

        // Act
        var result = recognizer.TryReplaceWithEqv(eqvPattern);

        // Assert - should still detect EQV pattern despite commutation
        Assert.NotNull(result);
        Assert.IsType<EqvNode>(result);
    }
}
