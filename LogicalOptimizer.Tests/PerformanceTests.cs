using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

public class PerformanceTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Fact]
    public void Performance_LargeExpression_ShouldProcessInReasonableTime()
    {
        // Arrange
        var largeExpression = "a & b | c & d | e & f | g & h | i & j | k & l | m & n | o & p";
        var sw = new Stopwatch();

        // Act
        sw.Start();
        var result = _optimizer.OptimizeExpression(largeExpression);
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Large expression should be processed in less than 1 second, but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Performance_DeeplyNestedExpression_ShouldProcessInReasonableTime()
    {
        // Arrange
        var deepExpression = "((((((a & b) | c) & d) | e) & f) | g)";
        var sw = new Stopwatch();

        // Act
        sw.Start();
        var result = _optimizer.OptimizeExpression(deepExpression);
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Deeply nested expression should be processed in less than 0.5 seconds, but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Performance_MassiveProcessing_ShouldProcessInReasonableTime()
    {
        // Arrange
        var sw = new Stopwatch();

        // Act
        sw.Start();
        for (var i = 0; i < 100; i++) _optimizer.OptimizeExpression($"a{i} & b{i} | c{i}");
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"100 expressions should be processed in less than 1 second, but took {sw.ElapsedMilliseconds}ms");
    }
}
