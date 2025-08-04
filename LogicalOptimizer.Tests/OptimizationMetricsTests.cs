using System;
using System.Collections.Generic;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for OptimizationMetrics and AstMetrics components
/// </summary>
public class OptimizationMetricsTests
{
    private static AstNode ParseExpression(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #region OptimizationMetrics Tests

    [Fact]
    public void OptimizationMetrics_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var metrics = new OptimizationMetrics();

        // Assert
        Assert.Equal(0, metrics.OriginalNodes);
        Assert.Equal(0, metrics.OptimizedNodes);
        Assert.Equal(0, metrics.Iterations);
        Assert.Equal(0, metrics.AppliedRules);
        Assert.Equal(TimeSpan.Zero, metrics.ElapsedTime);
        Assert.NotNull(metrics.RuleApplicationCount);
        Assert.Empty(metrics.RuleApplicationCount);
        Assert.NotNull(metrics.OptimizationSteps);
        Assert.Empty(metrics.OptimizationSteps);
    }

    [Fact]
    public void CompressionRatio_WithValidData_ShouldCalculateCorrectly()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 10,
            OptimizedNodes = 5
        };

        // Act
        var ratio = metrics.CompressionRatio;

        // Assert
        Assert.Equal(0.5, ratio);
    }

    [Fact]
    public void CompressionRatio_WithZeroOriginalNodes_ShouldReturnOne()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 0,
            OptimizedNodes = 5
        };

        // Act
        var ratio = metrics.CompressionRatio;

        // Assert
        Assert.Equal(1.0, ratio);
    }

    [Fact]
    public void IsImproved_WhenOptimizedLess_ShouldReturnTrue()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 10,
            OptimizedNodes = 5
        };

        // Act & Assert
        Assert.True(metrics.IsImproved);
    }

    [Fact]
    public void IsImproved_WhenOptimizedEqual_ShouldReturnFalse()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 10,
            OptimizedNodes = 10
        };

        // Act & Assert
        Assert.False(metrics.IsImproved);
    }

    [Fact]
    public void IsImproved_WhenOptimizedGreater_ShouldReturnFalse()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 5,
            OptimizedNodes = 10
        };

        // Act & Assert
        Assert.False(metrics.IsImproved);
    }

    [Fact]
    public void AddStep_ShouldAddToOptimizationSteps()
    {
        // Arrange
        var metrics = new OptimizationMetrics();
        var step = "Applied DeMorgan's law";

        // Act
        metrics.AddStep(step);

        // Assert
        Assert.Contains(step, metrics.OptimizationSteps);
        Assert.Single(metrics.OptimizationSteps);
    }

    [Fact]
    public void AddStep_MultipleSteps_ShouldAddAll()
    {
        // Arrange
        var metrics = new OptimizationMetrics();
        var steps = new[] { "Step 1", "Step 2", "Step 3" };

        // Act
        foreach (var step in steps)
        {
            metrics.AddStep(step);
        }

        // Assert
        Assert.Equal(3, metrics.OptimizationSteps.Count);
        Assert.Equal(steps, metrics.OptimizationSteps);
    }

    [Fact]
    public void ToString_WithBasicData_ShouldFormatCorrectly()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 10,
            OptimizedNodes = 5,
            Iterations = 3,
            AppliedRules = 7,
            ElapsedTime = TimeSpan.FromMilliseconds(123.45)
        };

        // Act
        var result = metrics.ToString();

        // Assert
        Assert.Contains("=== Optimization Metrics ===", result);
        Assert.Contains("Original nodes: 10", result);
        Assert.Contains("Optimized nodes: 5", result);
        Assert.Contains("Compression ratio: 50.0%", result);
        Assert.Contains("Iterations: 3", result);
        Assert.Contains("Applied rules: 7", result);
        Assert.Contains("123.45ms", result);
    }

    [Fact]
    public void ToString_WithRuleApplications_ShouldIncludeRules()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 10,
            OptimizedNodes = 5,
            RuleApplicationCount = new Dictionary<string, int>
            {
                ["DeMorgan"] = 3,
                ["Constants"] = 5,
                ["Absorption"] = 1
            }
        };

        // Act
        var result = metrics.ToString();

        // Assert
        Assert.Contains("Rule applications:", result);
        Assert.Contains("Constants: 5", result);
        Assert.Contains("DeMorgan: 3", result);
        Assert.Contains("Absorption: 1", result);
    }

    [Fact]
    public void ToString_WithoutRuleApplications_ShouldNotIncludeRulesSection()
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = 10,
            OptimizedNodes = 5
        };

        // Act
        var result = metrics.ToString();

        // Assert
        Assert.DoesNotContain("Rule applications:", result);
    }

    #endregion

    #region AstMetrics Tests

    [Fact]
    public void CountNodes_SingleVariable_ShouldReturnOne()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var count = AstMetrics.CountNodes(ast);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountNodes_SimpleAnd_ShouldReturnThree()
    {
        // Arrange
        var ast = ParseExpression("a & b");

        // Act
        var count = AstMetrics.CountNodes(ast);

        // Assert
        Assert.Equal(3, count); // AndNode + 2 VariableNodes
    }

    [Fact]
    public void CountNodes_SimpleOr_ShouldReturnThree()
    {
        // Arrange
        var ast = ParseExpression("a | b");

        // Act
        var count = AstMetrics.CountNodes(ast);

        // Assert
        Assert.Equal(3, count); // OrNode + 2 VariableNodes
    }

    [Fact]
    public void CountNodes_SimpleNot_ShouldReturnTwo()
    {
        // Arrange
        var ast = ParseExpression("!a");

        // Act
        var count = AstMetrics.CountNodes(ast);

        // Assert
        Assert.Equal(2, count); // NotNode + VariableNode
    }

    [Fact]
    public void CountNodes_ComplexExpression_ShouldReturnCorrectCount()
    {
        // Arrange
        var ast = ParseExpression("(a & b) | c");

        // Act
        var count = AstMetrics.CountNodes(ast);

        // Assert
        Assert.Equal(5, count); // OrNode + AndNode + 3 VariableNodes
    }

    [Fact]
    public void CountNodes_NestedExpression_ShouldReturnCorrectCount()
    {
        // Arrange
        var ast = ParseExpression("!(a & b)");

        // Act
        var count = AstMetrics.CountNodes(ast);

        // Assert
        Assert.Equal(4, count); // NotNode + AndNode + 2 VariableNodes
    }

    [Fact]
    public void GetDepth_SingleVariable_ShouldReturnOne()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var depth = AstMetrics.GetDepth(ast);

        // Assert
        Assert.Equal(1, depth);
    }

    [Fact]
    public void GetDepth_SimpleAnd_ShouldReturnTwo()
    {
        // Arrange
        var ast = ParseExpression("a & b");

        // Act
        var depth = AstMetrics.GetDepth(ast);

        // Assert
        Assert.Equal(2, depth); // AndNode depth + Variable depth
    }

    [Fact]
    public void GetDepth_NestedExpression_ShouldReturnCorrectDepth()
    {
        // Arrange
        var ast = ParseExpression("!(a & b)");

        // Act
        var depth = AstMetrics.GetDepth(ast);

        // Assert
        Assert.Equal(3, depth); // NotNode + AndNode + Variable
    }

    [Fact]
    public void GetDepth_ComplexNesting_ShouldReturnCorrectDepth()
    {
        // Arrange
        var ast = ParseExpression("a & (b | (c & d))");

        // Act
        var depth = AstMetrics.GetDepth(ast);

        // Assert
        Assert.True(depth >= 3); // Should be at least 3 levels deep
    }

    [Fact]
    public void CountOperators_SingleVariable_ShouldReturnZero()
    {
        // Arrange
        var ast = ParseExpression("a");

        // Act
        var count = AstMetrics.CountOperators(ast);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void CountOperators_SimpleAnd_ShouldReturnOne()
    {
        // Arrange
        var ast = ParseExpression("a & b");

        // Act
        var count = AstMetrics.CountOperators(ast);

        // Assert
        Assert.Equal(1, count); // One AND operator
    }

    [Fact]
    public void CountOperators_SimpleNot_ShouldReturnOne()
    {
        // Arrange
        var ast = ParseExpression("!a");

        // Act
        var count = AstMetrics.CountOperators(ast);

        // Assert
        Assert.Equal(1, count); // One NOT operator
    }

    [Fact]
    public void CountOperators_ComplexExpression_ShouldReturnCorrectCount()
    {
        // Arrange
        var ast = ParseExpression("(a & b) | c");

        // Act
        var count = AstMetrics.CountOperators(ast);

        // Assert
        Assert.Equal(2, count); // One AND + One OR
    }

    [Fact]
    public void CountOperators_NestedExpression_ShouldReturnCorrectCount()
    {
        // Arrange
        var ast = ParseExpression("!(a & b)");

        // Act
        var count = AstMetrics.CountOperators(ast);

        // Assert
        Assert.Equal(2, count); // One NOT + One AND
    }

    [Theory]
    [InlineData("a", 1, 1, 0)]
    [InlineData("a & b", 3, 2, 1)]
    [InlineData("a | b", 3, 2, 1)]
    [InlineData("!a", 2, 2, 1)]
    [InlineData("a & b & c", 5, 3, 2)]
    [InlineData("(a & b) | c", 5, 3, 2)]
    public void AstMetrics_VariousExpressions_ShouldReturnExpectedValues(string expression, int expectedNodes, int expectedDepth, int expectedOperators)
    {
        // Arrange
        var ast = ParseExpression(expression);

        // Act
        var nodes = AstMetrics.CountNodes(ast);
        var depth = AstMetrics.GetDepth(ast);
        var operators = AstMetrics.CountOperators(ast);

        // Assert
        Assert.Equal(expectedNodes, nodes);
        Assert.Equal(expectedDepth, depth);
        Assert.Equal(expectedOperators, operators);
    }

    #endregion
}
