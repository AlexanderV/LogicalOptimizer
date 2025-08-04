using System.Collections.Generic;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the OptimizationQualityAnalyzer component - quality metrics and analysis
/// </summary>
public class OptimizationQualityAnalyzerTests
{
    [Fact]
    public void AnalyzeOptimization_SimpleOptimization_ShouldReturnValidMetrics()
    {
        // Arrange
        var metrics = new OptimizationMetrics();
        metrics.RuleApplicationCount["Absorption"] = 1;
        metrics.RuleApplicationCount["DoubleNegation"] = 1;
        
        var result = new OptimizationResult
        {
            Original = "a & a",
            Optimized = "a",
            CNF = "a",
            DNF = "a",
            Metrics = metrics
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.True(qualityMetrics.CompressionRatio > 0);
        Assert.True(qualityMetrics.CompressionRatio <= 1.0);
        Assert.True(qualityMetrics.OptimalityScore >= 0 && qualityMetrics.OptimalityScore <= 100);
        Assert.Contains("Absorption", qualityMetrics.AppliedOptimizations);
        Assert.Contains("DoubleNegation", qualityMetrics.AppliedOptimizations);
    }

    [Fact]
    public void AnalyzeOptimization_ComplexExpression_ShouldCalculateCorrectComplexity()
    {
        // Arrange
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
        Assert.True(qualityMetrics.Complexity > 0);
        Assert.True(qualityMetrics.MaxDepth > 0);
        Assert.True(qualityMetrics.OperatorCount > 0);
        Assert.True(qualityMetrics.LiteralCount > 0);
    }

    [Fact]
    public void AnalyzeOptimization_HighlyOptimizedExpression_ShouldIndicateOptimal()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & a & a & a",
            Optimized = "a",
            CNF = "a",
            DNF = "a",
            Metrics = new OptimizationMetrics()
        };

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        Assert.True(qualityMetrics.CompressionRatio < 0.5); // High compression
        Assert.True(qualityMetrics.OptimalityScore >= 85); // Should be considered optimal
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
        Assert.NotEmpty(qualityMetrics.PossibleImprovements); // Should suggest improvements
    }

    [Fact]
    public void AnalyzeOptimization_InvalidExpression_ShouldHandleGracefully()
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

        // Act
        var qualityMetrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

        // Assert
        Assert.NotNull(qualityMetrics);
        // Should not throw exception and return reasonable defaults
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
        Assert.Contains("OPTIMIZATION QUALITY REPORT", report);
        Assert.Contains("Compression", report);
        Assert.Contains("Complexity", report);
    }

    [Fact]
    public void QualityMetrics_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var metrics = new OptimizationQualityAnalyzer.QualityMetrics();

        // Assert
        Assert.NotNull(metrics.AppliedOptimizations);
        Assert.NotNull(metrics.PossibleImprovements);
        Assert.Empty(metrics.AppliedOptimizations);
        Assert.Empty(metrics.PossibleImprovements);
        Assert.Equal(0, metrics.LiteralCount);
        Assert.Equal(0, metrics.OperatorCount);
        Assert.Equal(0, metrics.MaxDepth);
        Assert.False(metrics.IsOptimal);
    }

    [Theory]
    [InlineData("a", "a", 1.0)]           // No optimization: 1 node → 1 node = 1.0
    [InlineData("a & a", "a", 0.33)]      // Good optimization: 3 nodes → 1 node = 0.33 
    [InlineData("a | a | a", "a", 0.2)]   // Excellent optimization: 5 nodes → 1 node = 0.2
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
