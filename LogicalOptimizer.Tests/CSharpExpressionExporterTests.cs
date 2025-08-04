using System;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Comprehensive tests for CSharpExpressionExporter to improve coverage from 54.9%
/// </summary>
public class CSharpExpressionExporterTests
{
    [Fact]
    public void ToExpression_VariableNode_ShouldReturnVariableName()
    {
        // Arrange
        var variable = new VariableNode("testVar");

        // Act
        var result = CSharpExpressionExporter.ToExpression(variable);

        // Assert
        Assert.Equal("testVar", result);
    }

    [Fact]
    public void ToExpression_ConstantOne_ShouldReturnTrue()
    {
        // Arrange
        var constantOne = new VariableNode("1");

        // Act
        var result = CSharpExpressionExporter.ToExpression(constantOne);

        // Assert
        Assert.Equal("true", result);
    }

    [Fact]
    public void ToExpression_ConstantZero_ShouldReturnFalse()
    {
        // Arrange
        var constantZero = new VariableNode("0");

        // Act
        var result = CSharpExpressionExporter.ToExpression(constantZero);

        // Assert
        Assert.Equal("false", result);
    }

    [Fact]
    public void ToExpression_NotNode_ShouldReturnNegatedExpression()
    {
        // Arrange
        var variable = new VariableNode("a");
        var notNode = new NotNode(variable);

        // Act
        var result = CSharpExpressionExporter.ToExpression(notNode);

        // Assert
        Assert.Equal("!(a)", result);
    }

    [Fact]
    public void ToExpression_AndNode_ShouldReturnAndExpression()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var andNode = new AndNode(left, right);

        // Act
        var result = CSharpExpressionExporter.ToExpression(andNode);

        // Assert
        Assert.Equal("(a && b)", result);
    }

    [Fact]
    public void ToExpression_OrNode_ShouldReturnOrExpression()
    {
        // Arrange
        var left = new VariableNode("x");
        var right = new VariableNode("y");
        var orNode = new OrNode(left, right);

        // Act
        var result = CSharpExpressionExporter.ToExpression(orNode);

        // Assert
        Assert.Equal("(x || y)", result);
    }

    [Fact]
    public void ToExpression_XorNode_ShouldReturnXorExpression()
    {
        // Arrange
        var left = new VariableNode("p");
        var right = new VariableNode("q");
        var xorNode = new XorNode(left, right);

        // Act
        var result = CSharpExpressionExporter.ToExpression(xorNode);

        // Assert
        Assert.Equal("(p ^ q)", result);
    }

    [Fact]
    public void ToExpression_ImpNode_ShouldReturnImplicationExpression()
    {
        // Arrange
        var left = new VariableNode("a");
        var right = new VariableNode("b");
        var impNode = new ImpNode(left, right);

        // Act
        var result = CSharpExpressionExporter.ToExpression(impNode);

        // Assert
        Assert.Equal("(!a || b)", result);
    }

    [Fact]
    public void ToExpression_ComplexNestedExpression_ShouldReturnCorrectExpression()
    {
        // Arrange: (a && b) || !c
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");
        var andNode = new AndNode(a, b);
        var notNode = new NotNode(c);
        var orNode = new OrNode(andNode, notNode);

        // Act
        var result = CSharpExpressionExporter.ToExpression(orNode);

        // Assert
        Assert.Equal("((a && b) || !(c))", result);
    }

    [Fact]
    public void ToExpression_NestedNotExpression_ShouldReturnCorrectExpression()
    {
        // Arrange: !(!a && b)
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var notA = new NotNode(a);
        var andNode = new AndNode(notA, b);
        var notOuter = new NotNode(andNode);

        // Act
        var result = CSharpExpressionExporter.ToExpression(notOuter);

        // Assert
        Assert.Equal("!((!(a) && b))", result);
    }

    [Fact]
    public void GenerateMethod_SimpleExpression_ShouldGenerateCorrectMethod()
    {
        // Arrange
        var andNode = new AndNode(new VariableNode("a"), new VariableNode("b"));

        // Act
        var result = CSharpExpressionExporter.GenerateMethod(andNode);

        // Assert
        Assert.Contains("public static bool EvaluateExpression(bool a, bool b)", result);
        Assert.Contains("return (a && b);", result);
        Assert.Contains("{", result);
        Assert.Contains("}", result);
    }

    [Fact]
    public void GenerateMethod_CustomMethodName_ShouldUseCustomName()
    {
        // Arrange
        var variable = new VariableNode("test");

        // Act
        var result = CSharpExpressionExporter.GenerateMethod(variable, "CustomMethod");

        // Assert
        Assert.Contains("public static bool CustomMethod(bool test)", result);
        Assert.Contains("return test;", result);
    }

    [Fact]
    public void GenerateMethod_MultipleVariables_ShouldOrderParametersAlphabetically()
    {
        // Arrange: Create expression with variables in non-alphabetical order
        var z = new VariableNode("z");
        var a = new VariableNode("a");
        var m = new VariableNode("m");
        var expression = new AndNode(new AndNode(z, a), m);

        // Act
        var result = CSharpExpressionExporter.GenerateMethod(expression);

        // Assert
        Assert.Contains("bool a, bool m, bool z", result); // Should be alphabetically ordered
        Assert.Contains("return ((z && a) && m);", result);
    }

    [Fact]
    public void GenerateMethod_SingleVariable_ShouldGenerateCorrectMethod()
    {
        // Arrange
        var variable = new VariableNode("singleVar");

        // Act
        var result = CSharpExpressionExporter.GenerateMethod(variable, "TestMethod");

        // Assert
        Assert.Contains("public static bool TestMethod(bool singleVar)", result);
        Assert.Contains("return singleVar;", result);
    }

    [Fact]
    public void GenerateClass_SimpleExpression_ShouldGenerateCorrectClass()
    {
        // Arrange
        var orNode = new OrNode(new VariableNode("x"), new VariableNode("y"));

        // Act
        var result = CSharpExpressionExporter.GenerateClass(orNode);

        // Assert
        Assert.Contains("using System;", result);
        Assert.Contains("public static class BooleanEvaluator", result);
        Assert.Contains("public static bool Evaluate(bool x, bool y)", result);
        Assert.Contains("return (x || y);", result);
    }

    [Fact]
    public void GenerateClass_CustomClassAndMethodName_ShouldUseCustomNames()
    {
        // Arrange
        var variable = new VariableNode("flag");

        // Act
        var result = CSharpExpressionExporter.GenerateClass(variable, "MyCustomClass", "MyCustomMethod");

        // Assert
        Assert.Contains("public static class MyCustomClass", result);
        Assert.Contains("public static bool MyCustomMethod(bool flag)", result);
        Assert.Contains("return flag;", result);
    }

    [Fact]
    public void GenerateClass_ComplexExpression_ShouldGenerateCorrectClass()
    {
        // Arrange: !(a && b) || c
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");
        var andNode = new AndNode(a, b);
        var notNode = new NotNode(andNode);
        var orNode = new OrNode(notNode, c);

        // Act
        var result = CSharpExpressionExporter.GenerateClass(orNode, "ComplexEvaluator", "EvaluateComplex");

        // Assert
        Assert.Contains("public static class ComplexEvaluator", result);
        Assert.Contains("public static bool EvaluateComplex(bool a, bool b, bool c)", result);
        Assert.Contains("EvaluateComplex", result);
        Assert.Contains("return", result);
        Assert.Contains("||", result);
        Assert.Contains("c", result);
        Assert.Contains("using System;", result);
    }

    [Fact]
    public void GenerateLambda_SimpleExpression_ShouldGenerateCorrectLambda()
    {
        // Arrange
        var andNode = new AndNode(new VariableNode("p"), new VariableNode("q"));

        // Act
        var result = CSharpExpressionExporter.GenerateLambda(andNode);

        // Assert
        Assert.Equal("(p, q) => (p && q)", result);
    }

    [Fact]
    public void GenerateLambda_SingleVariable_ShouldGenerateCorrectLambda()
    {
        // Arrange
        var variable = new VariableNode("input");

        // Act
        var result = CSharpExpressionExporter.GenerateLambda(variable);

        // Assert
        Assert.Equal("(input) => input", result);
    }

    [Fact]
    public void GenerateLambda_ComplexExpression_ShouldGenerateCorrectLambda()
    {
        // Arrange: (a || b) && !c
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");
        var orNode = new OrNode(a, b);
        var notNode = new NotNode(c);
        var andNode = new AndNode(orNode, notNode);

        // Act
        var result = CSharpExpressionExporter.GenerateLambda(andNode);

        // Assert
        Assert.Equal("(a, b, c) => ((a || b) && !(c))", result);
    }

    [Fact]
    public void GenerateLambda_VariablesOrderedAlphabetically_ShouldMaintainOrder()
    {
        // Arrange: Variables in non-alphabetical order in expression
        var z = new VariableNode("z");
        var a = new VariableNode("a");
        var expression = new XorNode(z, a);

        // Act
        var result = CSharpExpressionExporter.GenerateLambda(expression);

        // Assert
        Assert.StartsWith("(a, z) =>", result); // Parameters should be alphabetically ordered
        Assert.Contains("(z ^ a)", result); // But expression maintains original structure
    }

    [Fact]
    public void GenerateMethod_Constants_ShouldHandleCorrectly()
    {
        // Arrange: Expression with constants
        var constantTrue = new VariableNode("1");
        var variable = new VariableNode("a");
        var andNode = new AndNode(constantTrue, variable);

        // Act
        var result = CSharpExpressionExporter.GenerateMethod(andNode);

        // Assert
        Assert.Contains("bool a", result); // Only variable parameters, not constants
        Assert.Contains("return (true && a);", result);
    }

    [Fact]
    public void ToExpression_MixedConstants_ShouldConvertCorrectly()
    {
        // Arrange: Mix of constants and variables
        var constantZero = new VariableNode("0");
        var constantOne = new VariableNode("1");
        var variable = new VariableNode("x");
        var orNode = new OrNode(new AndNode(constantZero, variable), constantOne);

        // Act
        var result = CSharpExpressionExporter.ToExpression(orNode);

        // Assert
        Assert.Equal("((false && x) || true)", result);
    }

    [Fact]
    public void GenerateClass_NoVariables_ShouldHandleGracefully()
    {
        // Arrange: Expression with only constants
        var constantTrue = new VariableNode("1");

        // Act
        var result = CSharpExpressionExporter.GenerateClass(constantTrue);

        // Assert
        Assert.Contains("public static bool Evaluate()", result); // No parameters
        Assert.Contains("return true;", result);
        Assert.Contains("public static class BooleanEvaluator", result);
    }
}
