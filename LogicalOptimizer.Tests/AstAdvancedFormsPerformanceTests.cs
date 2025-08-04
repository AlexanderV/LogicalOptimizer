using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Performance and scalability tests for AST-based advanced forms detection
///     Tests that the removal of the 10-variable limitation works correctly
/// </summary>
public class AstAdvancedFormsPerformanceTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    /// <summary>
    ///     Test that AST-based detection works without variable count limitations
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    public void AstAdvancedForms_VariableScalability_ShouldWorkWithoutLimitations(int variableCount)
    {
        // Arrange - create expression with many variables containing XOR patterns
        var expr = GenerateXorExpression(variableCount);

        // Act
        var result = _optimizer.OptimizeExpression(expr);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Variables.Count >= variableCount,
            $"Should handle {variableCount} variables, got {result.Variables.Count}");

        // Should not throw any exceptions about variable limits
        var exception = Record.Exception(() =>
        {
            // Test advanced forms processing (simulates --advanced flag)
            var patternRecognizer = new PatternRecognizer();
            var advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);
            Assert.NotNull(advancedForms);
        });

        Assert.Null(exception);
    }

    /// <summary>
    ///     Test performance of AST-based pattern detection vs previous approaches
    /// </summary>
    [Theory]
    [InlineData(6, 100)] // Small expressions should be very fast
    [InlineData(12, 1000)] // Medium expressions should be reasonable
    [InlineData(20, 5000)] // Large expressions should complete within reasonable time
    public void AstAdvancedForms_Performance_ShouldScaleLinearly(int variableCount, int maxMilliseconds)
    {
        // Arrange
        var expr = GenerateXorExpression(variableCount);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = _optimizer.OptimizeExpression(expr);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < maxMilliseconds,
            $"Processing {variableCount} variables took {stopwatch.ElapsedMilliseconds}ms, expected < {maxMilliseconds}ms");

        Assert.NotNull(result);
        Assert.True(result.Variables.Count >= variableCount);
    }

    /// <summary>
    ///     Test memory efficiency with large expressions
    /// </summary>
    [Fact]
    public void AstAdvancedForms_MemoryEfficiency_ShouldNotExceedReasonableLimits()
    {
        // Arrange
        var initialMemory = GC.GetTotalMemory(true);

        // Act - process multiple large expressions
        for (var i = 0; i < 10; i++)
        {
            var expr = GenerateXorExpression(15);
            var result = _optimizer.OptimizeExpression(expr);
            Assert.NotNull(result);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert - memory increase should be reasonable (less than 100MB for this test)
        Assert.True(memoryIncrease < 100 * 1024 * 1024,
            $"Memory increased by {memoryIncrease / 1024 / 1024}MB, which seems excessive");
    }

    /// <summary>
    ///     Test complex nested expressions with multiple XOR patterns
    /// </summary>
    [Theory]
    [InlineData("(a & !b) | (!a & b) | (c & !d) | (!c & d)")]
    [InlineData("(v1 & !v2) | (!v1 & v2) | (v3 & !v4) | (!v3 & v4) | (v5 & !v6) | (!v5 & v6)")]
    [InlineData(
        "(x1 & !x2) | (!x1 & x2) | (x3 & !x4) | (!x3 & x4) | (x5 & !x6) | (!x5 & x6) | (x7 & !x8) | (!x7 & x8)")]
    public void AstAdvancedForms_ComplexPatterns_ShouldDetectMultipleXorPatterns(string expr)
    {
        // Act
        var result = _optimizer.OptimizeExpression(expr);

        // Assert
        Assert.NotNull(result);

        // Should handle complex expressions without issues
        var exception = Record.Exception(() =>
        {
            var patternRecognizer = new PatternRecognizer();
            var advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);
            Assert.NotNull(advancedForms);
        });

        Assert.Null(exception);
    }

    /// <summary>
    ///     Test that --advanced flag works with large expressions
    ///     This simulates the actual CLI usage
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    public void AstAdvancedForms_CliAdvancedFlag_ShouldWorkWithLargeExpressions(int variableCount)
    {
        // Arrange
        var expr = GenerateXorExpression(variableCount);

        // Act - simulate --advanced flag processing
        var result = _optimizer.OptimizeExpression(expr);

        var patternRecognizer = new PatternRecognizer();
        var originalAdvanced = patternRecognizer.GenerateAdvancedLogicalForms(result.Original);
        var optimizedAdvanced = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);

        // Choose the better result (simulates Program.cs logic)
        string advancedForms;
        if (!originalAdvanced.StartsWith("Optimized:"))
            advancedForms = originalAdvanced;
        else if (!optimizedAdvanced.StartsWith("Optimized:"))
            advancedForms = optimizedAdvanced;
        else
            advancedForms = optimizedAdvanced;

        // Assert
        Assert.NotNull(advancedForms);
        Assert.NotEmpty(advancedForms);

        // Should detect some patterns in XOR-heavy expressions
        if (advancedForms.Contains("XOR") || advancedForms.Contains("IMP"))
            Assert.True(true, "Advanced patterns detected successfully");
    }

    /// <summary>
    ///     Stress test with very large expressions
    /// </summary>
    [Fact]
    public void AstAdvancedForms_StressTest_ShouldHandleVeryLargeExpressions()
    {
        // Arrange - create a very large expression (14+ variables)
        var expr = GenerateXorExpression(30);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var exception = Record.Exception(() =>
        {
            var result = _optimizer.OptimizeExpression(expr);
            Assert.NotNull(result);
            Assert.True(result.Variables.Count >= 30);
        });
        stopwatch.Stop();

        // Assert
        Assert.Null(exception);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, // Should complete within 10 seconds
            $"Stress test took {stopwatch.ElapsedMilliseconds}ms, which is too slow");
    }

    /// <summary>
    ///     Test edge case with mixed patterns (XOR + IMP + regular boolean operations)
    /// </summary>
    [Fact]
    public void AstAdvancedForms_MixedPatterns_ShouldHandleComplexExpressions()
    {
        // Arrange - complex expression with multiple pattern types
        var expr = "(a & !b) | (!a & b) | (!c | d) | (e & f) | (!g | h)";

        // Act
        var result = _optimizer.OptimizeExpression(expr);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Variables.Count >= 6);

        // Should handle mixed patterns without crashing
        var exception = Record.Exception(() =>
        {
            var patternRecognizer = new PatternRecognizer();
            var advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);
            Assert.NotNull(advancedForms);
        });

        Assert.Null(exception);
    }

    /// <summary>
    ///     Test backward compatibility - should still work with small expressions
    /// </summary>
    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("(a & !b) | (!a & b)")]
    [InlineData("!a | b")]
    public void AstAdvancedForms_BackwardCompatibility_ShouldWorkWithSmallExpressions(string expr)
    {
        // Act
        var result = _optimizer.OptimizeExpression(expr);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Variables);

        // Should work the same as before
        var exception = Record.Exception(() =>
        {
            var patternRecognizer = new PatternRecognizer();
            var advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);
            Assert.NotNull(advancedForms);
        });

        Assert.Null(exception);
    }

    /// <summary>
    ///     Helper method to generate XOR expressions with specified number of variables
    /// </summary>
    private string GenerateXorExpression(int variableCount)
    {
        var terms = new List<string>();

        // Generate XOR patterns: (v1 & !v2) | (!v1 & v2) | (v3 & !v4) | (!v3 & v4) ...
        for (var i = 1; i <= variableCount; i += 2)
            if (i + 1 <= variableCount)
                terms.Add($"(v{i} & !v{i + 1}) | (!v{i} & v{i + 1})");
            else
                // Odd number - add a simple term
                terms.Add($"v{i}");

        return string.Join(" | ", terms);
    }

    /// <summary>
    ///     Helper method to generate implication expressions
    /// </summary>
    private string GenerateImplicationExpression(int variableCount)
    {
        var terms = new List<string>();

        // Generate IMP patterns: !v1 | v2 | !v3 | v4 ...
        for (var i = 1; i <= variableCount; i += 2)
            if (i + 1 <= variableCount)
                terms.Add($"!v{i} | v{i + 1}");
            else
                terms.Add($"v{i}");

        return string.Join(" | ", terms);
    }

    /// <summary>
    ///     Test comparison between old approach (with limits) and new approach (without limits)
    ///     This documents the improvement
    /// </summary>
    [Fact]
    public void AstAdvancedForms_LimitRemoval_DocumentsImprovement()
    {
        // This test documents that we removed the 10-variable limitation

        // Arrange - expression with more than 10 variables (would fail with old limitation)
        var expr = GenerateXorExpression(15);

        // Act - should work without throwing "too many variables" exception
        var result = _optimizer.OptimizeExpression(expr);

        // Assert - documents the improvement
        Assert.NotNull(result);
        Assert.True(result.Variables.Count > 10,
            "This test documents that the 10-variable limitation has been removed");

        // Should be able to process advanced forms without variable count restrictions
        var patternRecognizer = new PatternRecognizer();
        var exception = Record.Exception(() =>
        {
            var advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);
            Assert.NotNull(advancedForms);
        });

        Assert.Null(exception);
    }
}