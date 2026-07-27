using System.Collections.Generic;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the OptimizationQualityAnalyzer component - quality metrics and analysis
/// </summary>
public class OptimizationQualityAnalyzerTests
{
    [Fact]
    public void AnalyzeOptimization_ComplexExpression_ShouldCalculateCorrectComplexity()
    {
        // Arrange - Or(And(a,b), And(c,d), And(e,f)): ONE n-ary OR + 3 AND operators,
        // 6 literals, variables at depth 2 (v2 cost model: n-ary node is one operator)
        var result = new OptimizationResult
        {
            Original = "(a & b) | (c & d) | (e & f)",
            Optimized = "(a & b) | (c & d) | (e & f)",
            CNF = "(a & b) | (c & d) | (e & f)",
            DNF = "(a & b) | (c & d) | (e & f)",
            Metrics = new OptimizationMetrics()
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.Equal(6, qualityMetrics.LiteralCount);
        Assert.Equal(4, qualityMetrics.OperatorCount);
        Assert.Equal(2, qualityMetrics.MaxDepth);
        Assert.Equal(1.0, qualityMetrics.CompressionRatio, 2);
        // Complexity = operators * 1.0 + literals * 0.5 + depth * 0.8 = 4 + 3 + 1.6
        Assert.Equal(8.6, qualityMetrics.Complexity, 1);
    }

    [Fact]
    public void AnalyzeOptimization_HighlyOptimizedExpression_ShouldIndicateOptimal()
    {
        // Arrange - the original must not fold at parse time (the factory dedups exact
        // duplicates), so absorption-shaped redundancy provides the compression
        var result = new OptimizationResult
        {
            Original = "a & b | a & !b | a",
            Optimized = "a",
            CNF = "a",
            DNF = "a",
            Metrics = new OptimizationMetrics()
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        // Deterministic analyzer: original parses to a 9-node tree (OR of a&b, a&!b, a),
        // optimized "a" is 1 node → ratio exactly 1/9.
        Assert.Equal(1.0 / 9, qualityMetrics.CompressionRatio, 3);
        // Score = base 50 + 30 (ratio<0.5) + 15 (depth 3→0) − 10 (no rules) = 85.
        Assert.Equal(85, qualityMetrics.OptimalityScore);
        Assert.True(qualityMetrics.IsOptimal);
    }

    [Fact]
    public void AnalyzeOptimization_NoOptimization_ShouldShowCompressionRatioOfOne()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            CNF = "a & b",
            DNF = "a & b",
            Metrics = new OptimizationMetrics()
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.Equal(1.0, qualityMetrics.CompressionRatio, 2); // No compression
        // Ratio > 0.9 ⇒ the analyzer emits this exact low-compression advice
        Assert.Contains("Low compression ratio - check for additional transformations",
            qualityMetrics.PossibleImprovements);
    }

    [Fact]
    public void AnalyzeOptimization_InvalidExpression_ShouldThrow()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "invalid @#$ expression",
            Optimized = "invalid @#$ expression",
            CNF = "invalid @#$ expression",
            DNF = "invalid @#$ expression",
            Metrics = new OptimizationMetrics()
        };

        // Act & Assert: garbage input must not silently produce a quality report
        Assert.Throws<ArgumentException>(() => OptimizationQualityAnalyzer.AnalyzeOptimization(result));
    }

    [Fact]
    public void GenerateQualityReport_ValidResult_ShouldReturnFormattedReport()
    {
        // Arrange
        var metrics = new OptimizationMetrics();
        metrics.RuleApplicationCount["Absorption"] = 2;

        var result = new OptimizationResult
        {
            Original = "a & a | b & b",
            Optimized = "a | b",
            CNF = "a | b",
            DNF = "a | b",
            Metrics = metrics
        };

        // Act
        var report = OptimizationQualityAnalyzer.GenerateQualityReport(result);

        // Assert
        Assert.NotNull(report);
        Assert.NotEmpty(report);
        // "a & a | b & b" folds to "a | b" (ratio 1.0) ⇒ the report must carry the
        // specific low-compression improvement line, not just section headers.
        Assert.Contains("Low compression ratio - check for additional transformations", report);
        Assert.Contains("Compression", report);
        Assert.Contains("Complexity", report);
    }

    [Theory]
    [InlineData("a", "a", 1.0)] // No optimization: 1 node → 1 node = 1.0
    [InlineData("a | a & b", "a", 0.2)] // Good optimization: 5 nodes → 1 node = 0.2
    [InlineData("a & b | a & !b | a & c | a", "a", 0.1)] // Excellent: 12 nodes → 1 node
    public void AnalyzeOptimization_CompressionRatio_ShouldBeCalculatedCorrectly(string original, string optimized, double expectedRatio)
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = original,
            Optimized = optimized,
            CNF = optimized,
            DNF = optimized,
            Metrics = new OptimizationMetrics()
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.Equal(expectedRatio, qualityMetrics.CompressionRatio, 1); // Allow 1 decimal place tolerance
    }

    [Fact]
    public void AnalyzeOptimization_WithManyOptimizations_ShouldListAllAppliedRules()
    {
        // Arrange
        var metrics = new OptimizationMetrics();
        metrics.RuleApplicationCount["Absorption"] = 3;
        metrics.RuleApplicationCount["DoubleNegation"] = 2;
        metrics.RuleApplicationCount["DeMorgan"] = 1;
        metrics.RuleApplicationCount["Identity"] = 4;

        var result = new OptimizationResult
        {
            Original = "!!a & (a | !a) & b",
            Optimized = "a & b",
            CNF = "a & b",
            DNF = "a & b",
            Metrics = metrics
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.Equal(4, qualityMetrics.AppliedOptimizations.Count);
        Assert.Contains("Absorption", qualityMetrics.AppliedOptimizations);
        Assert.Contains("DoubleNegation", qualityMetrics.AppliedOptimizations);
        Assert.Contains("DeMorgan", qualityMetrics.AppliedOptimizations);
        Assert.Contains("Identity", qualityMetrics.AppliedOptimizations);
    }

    [Fact]
    public void AnalyzeOptimization_NullMetrics_ShouldHandleGracefully()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            CNF = "a & b",
            DNF = "a & b",
            Metrics = null
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.Empty(qualityMetrics.AppliedOptimizations);
        // Should not throw exception
    }
}
