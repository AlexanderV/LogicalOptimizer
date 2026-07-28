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
    public void OptimizeExpression_WithMetrics_PopulatesConvergenceTrace()
    {
        // The rewrite engine records one convergence entry per fixpoint iteration plus the
        // initial state, so the trace length is Iterations + 1 and the entries carry node counts.
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & b | a & c | b & c", new OptimizationOptions { IncludeMetrics = true });

        Assert.NotNull(result.Metrics);
        var metrics = result.Metrics!;
        Assert.NotEmpty(metrics.OptimizationSteps);
        Assert.Equal(metrics.Iterations + 1, metrics.OptimizationSteps.Count);
        Assert.StartsWith("iter 0:", metrics.OptimizationSteps[0]);
        Assert.All(metrics.OptimizationSteps, step => Assert.Contains("nodes", step));
    }

    [Fact]
    public void OptimizeExpression_WithMetrics_MeasuresAllocatedBytes()
    {
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & b | a & c | b & c", new OptimizationOptions { IncludeMetrics = true });

        Assert.NotNull(result.Metrics);
        // A real optimization run allocates AST/normal-form artifacts, so the thread-local
        // allocation delta is strictly positive.
        Assert.True(result.Metrics!.AllocatedBytes > 0,
            $"Expected positive allocation measurement, got {result.Metrics.AllocatedBytes}");
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

    [Theory]
    [InlineData(10, 5, true)] // Optimized smaller -> improved
    [InlineData(10, 10, false)] // Equal -> not improved
    [InlineData(5, 10, false)] // Optimized larger -> not improved
    public void IsImproved_ShouldCompareNodeCounts(int originalNodes, int optimizedNodes, bool expected)
    {
        // Arrange
        var metrics = new OptimizationMetrics
        {
            OriginalNodes = originalNodes,
            OptimizedNodes = optimizedNodes
        };

        // Act & Assert
        Assert.Equal(expected, metrics.IsImproved);
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

        // Assert (formatting is culture-stable: production uses CultureInfo.InvariantCulture)
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

    [Theory]
    [InlineData("a", 1, 1, 0)]
    [InlineData("a & b", 3, 2, 1)]
    [InlineData("a | b", 3, 2, 1)]
    [InlineData("!a", 2, 2, 1)]
    [InlineData("a & b & c", 4, 2, 1)] // ONE n-ary AndNode + 3 variables (v2 cost model)
    [InlineData("(a & b) | c", 5, 3, 2)]
    [InlineData("!(a & b)", 4, 3, 2)] // NotNode + AndNode + 2 variables
    [InlineData("a & (b | (c & d))", 7, 4, 3)] // Deep nesting: And -> Or -> And -> variable
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
