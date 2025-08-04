using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the ExpressionGenerator component - random expression generation and demonstration cases
/// </summary>
public class ExpressionGeneratorTests
{
    private static AstNode ParseExpression(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #region GenerateRandomExpression Tests

    [Fact]
    public void GenerateRandomExpression_WithDefaultParameters_ShouldReturnValidExpression()
    {
        // Act
        var expression = ExpressionGenerator.GenerateRandomExpression();

        // Assert
        Assert.NotNull(expression);
        Assert.NotEmpty(expression);
        
        // Should be parseable
        var parseException = Record.Exception(() => ParseExpression(expression));
        Assert.Null(parseException);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void GenerateRandomExpression_WithVariableCount_ShouldReturnValidExpression(int variableCount)
    {
        // Act
        var expression = ExpressionGenerator.GenerateRandomExpression(variableCount);

        // Assert
        Assert.NotNull(expression);
        
        // For variableCount >= 2, should generate non-empty expression
        if (variableCount >= 2)
        {
            Assert.NotEmpty(expression);
            
            // Should be parseable
            var parseException = Record.Exception(() => ParseExpression(expression));
            Assert.Null(parseException);
            
            // Should contain variable patterns (v0, v1, etc.)
            var containsVariablePattern = false;
            for (int i = 0; i < variableCount; i++)
            {
                if (expression.Contains($"v{i}"))
                {
                    containsVariablePattern = true;
                    break;
                }
            }
            Assert.True(containsVariablePattern, "Expression should contain at least one variable");
        }
    }

    [Fact]
    public void GenerateRandomExpression_MultipleInvocations_ShouldGenerateDifferentExpressions()
    {
        // Act
        var expressions = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            expressions.Add(ExpressionGenerator.GenerateRandomExpression(4));
        }

        // Assert
        // Should generate at least some different expressions (not all identical)
        Assert.True(expressions.Count > 1, "Should generate some variation in expressions");
    }

    [Fact]
    public void GenerateRandomExpression_ShouldContainLogicalOperators()
    {
        // Act
        var expression = ExpressionGenerator.GenerateRandomExpression(4);

        // Assert
        Assert.True(expression.Contains("&") || expression.Contains("|"), 
            "Expression should contain logical operators");
    }

    [Fact]
    public void GenerateRandomExpression_ShouldContainParentheses()
    {
        // Act
        var expression = ExpressionGenerator.GenerateRandomExpression(4);

        // Assert
        Assert.Contains("(", expression);
        Assert.Contains(")", expression);
    }

    [Fact]
    public void GenerateRandomExpression_WithZeroVariables_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => ExpressionGenerator.GenerateRandomExpression(0));
        Assert.Null(exception);
    }

    [Fact]
    public void GenerateRandomExpression_WithOneVariable_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => ExpressionGenerator.GenerateRandomExpression(1));
        Assert.Null(exception);
    }

    #endregion

    #region GetDemonstrationExpressions Tests

    [Fact]
    public void GetDemonstrationExpressions_ShouldReturnNonEmptyDictionary()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.NotNull(demonstrations);
        Assert.NotEmpty(demonstrations);
    }

    [Fact]
    public void GetDemonstrationExpressions_AllExpressions_ShouldBeParseable()
    {
        // Arrange
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Act & Assert
        foreach (var kvp in demonstrations)
        {
            var exception = Record.Exception(() => ParseExpression(kvp.Value));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void GetDemonstrationExpressions_ShouldContainExpectedCategories()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.Contains("Simple Factorization", demonstrations.Keys);
        Assert.Contains("De Morgan Law", demonstrations.Keys);
        Assert.Contains("Double Negation", demonstrations.Keys);
        Assert.Contains("Absorption", demonstrations.Keys);
        Assert.Contains("XOR Pattern", demonstrations.Keys);
        Assert.Contains("Implication", demonstrations.Keys);
        Assert.Contains("Tautology", demonstrations.Keys);
        Assert.Contains("Contradiction", demonstrations.Keys);
    }

    [Fact]
    public void GetDemonstrationExpressions_FactorizationExample_ShouldBeCorrect()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.Equal("a & b | a & c", demonstrations["Simple Factorization"]);
    }

    [Fact]
    public void GetDemonstrationExpressions_DeMorganExample_ShouldBeCorrect()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.Equal("!(a & b)", demonstrations["De Morgan Law"]);
    }

    [Fact]
    public void GetDemonstrationExpressions_DoubleNegationExample_ShouldBeCorrect()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.Equal("!!a", demonstrations["Double Negation"]);
    }

    [Fact]
    public void GetDemonstrationExpressions_TautologyExample_ShouldBeCorrect()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.Equal("a | !a", demonstrations["Tautology"]);
    }

    [Fact]
    public void GetDemonstrationExpressions_ContradictionExample_ShouldBeCorrect()
    {
        // Act
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Assert
        Assert.Equal("a & !a", demonstrations["Contradiction"]);
    }

    [Fact]
    public void GetDemonstrationExpressions_AllEntries_ShouldHaveNonEmptyValues()
    {
        // Arrange
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();

        // Act & Assert
        foreach (var kvp in demonstrations)
        {
            Assert.NotNull(kvp.Value);
            Assert.NotEmpty(kvp.Value);
        }
    }

    #endregion

    #region GetBenchmarkExpressions Tests

    [Fact]
    public void GetBenchmarkExpressions_ShouldReturnNonEmptyDictionary()
    {
        // Act
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Assert
        Assert.NotNull(benchmarks);
        Assert.NotEmpty(benchmarks);
    }

    [Fact]
    public void GetBenchmarkExpressions_AllExpressions_ShouldBeParseable()
    {
        // Arrange
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Act & Assert
        foreach (var kvp in benchmarks)
        {
            var exception = Record.Exception(() => ParseExpression(kvp.Value));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void GetBenchmarkExpressions_ShouldContainSizeCategories()
    {
        // Act
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Assert
        Assert.Contains("Small (3 vars)", benchmarks.Keys);
        Assert.Contains("Medium (5 vars)", benchmarks.Keys);
        Assert.Contains("Large (8 vars)", benchmarks.Keys);
    }

    [Fact]
    public void GetBenchmarkExpressions_ShouldContainComplexityCategories()
    {
        // Act
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Assert
        Assert.Contains("Complex Nested", benchmarks.Keys);
        Assert.Contains("Deep Factorization", benchmarks.Keys);
        Assert.Contains("XOR Chain", benchmarks.Keys);
        Assert.Contains("Implication Chain", benchmarks.Keys);
    }

    [Fact]
    public void GetBenchmarkExpressions_SmallExpression_ShouldHaveExpectedPattern()
    {
        // Act
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Assert
        var smallExpr = benchmarks["Small (3 vars)"];
        Assert.Contains("a", smallExpr);
        Assert.Contains("b", smallExpr);
        Assert.Contains("c", smallExpr);
    }

    [Fact]
    public void GetBenchmarkExpressions_XorChain_ShouldContainXorPatterns()
    {
        // Act
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Assert
        var xorExpr = benchmarks["XOR Chain"];
        Assert.Contains("&", xorExpr);
        Assert.Contains("!", xorExpr);
        Assert.Contains("|", xorExpr);
    }

    [Fact]
    public void GetBenchmarkExpressions_AllEntries_ShouldHaveNonEmptyValues()
    {
        // Arrange
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Act & Assert
        foreach (var kvp in benchmarks)
        {
            Assert.NotNull(kvp.Value);
            Assert.NotEmpty(kvp.Value);
        }
    }

    [Fact]
    public void GetBenchmarkExpressions_ComplexityProgression_ShouldIncreaseInLength()
    {
        // Arrange
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Act
        var small = benchmarks["Small (3 vars)"];
        var medium = benchmarks["Medium (5 vars)"];
        var large = benchmarks["Large (8 vars)"];

        // Assert
        Assert.True(small.Length < medium.Length, "Medium should be longer than small");
        Assert.True(medium.Length < large.Length, "Large should be longer than medium");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void AllGeneratedExpressions_ShouldOptimizeWithoutErrors()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();
        var demonstrations = ExpressionGenerator.GetDemonstrationExpressions();
        var benchmarks = ExpressionGenerator.GetBenchmarkExpressions();

        // Act & Assert
        foreach (var expr in demonstrations.Values.Concat(benchmarks.Values))
        {
            var exception = Record.Exception(() => optimizer.OptimizeExpression(expr));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void RandomExpressions_ShouldOptimizeWithoutErrors()
    {
        // Arrange
        var optimizer = new BooleanExpressionOptimizer();

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            var expr = ExpressionGenerator.GenerateRandomExpression(3);
            var exception = Record.Exception(() => optimizer.OptimizeExpression(expr));
            Assert.Null(exception);
        }
    }

    #endregion
}
