using System.Runtime.CompilerServices;
using VerifyXunit;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Snapshot/approval testing with Verify: the full rendered output of the
///     user-facing surfaces (CLI formatter, C# exporter, AST visualizer, debug dump)
///     is stored in *.verified.txt files next to this class. A failing test writes a
///     *.received.txt to diff against; approve by replacing the verified file.
/// </summary>
internal static class SnapshotSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Never launch an interactive diff tool from a test run
        DiffEngine.DiffRunner.Disabled = true;
    }
}

public class SnapshotTests
{
    private static string CaptureConsole(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    [Fact]
    public Task CliOutput_Default()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c | b & c");
        var output = CaptureConsole(() => new OutputFormatter().DisplayResult(result,
            new CommandLineProcessor.CommandLineOptions { Expression = "a & b | a & c | b & c" }));
        return Verifier.Verify(output);
    }

    [Fact]
    public Task CliOutput_Verbose()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | !a & c",
            new OptimizationOptions { IncludeMetrics = true });
        var output = CaptureConsole(() => new OutputFormatter().DisplayResult(result,
            new CommandLineProcessor.CommandLineOptions { Expression = "a & b | !a & c", Verbose = true }));

        // Timings vary run to run; pin everything except the number itself
        return Verifier.Verify(output).ScrubLinesWithReplace(line =>
            line.Contains("time", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("ms") || line.Contains("Ms"))
                ? "[scrubbed timing]"
                : line);
    }

    [Fact]
    public Task CSharpExporter_GeneratedCode()
    {
        var ast = RandomExpressions.Parse("a & (b | !c) | !a & d");
        return Verifier.Verify(CSharpExpressionExporter.GenerateMethod(ast, "EvaluateDemo"));
    }

    [Fact]
    public Task AstVisualizer_TreeRendering()
    {
        var ast = RandomExpressions.Parse("!(a & b) | c & (d | !e)");
        return Verifier.Verify(
            AstVisualizer.VisualizeTree(ast) + "\n---\n" + AstVisualizer.GetCompactVisualization(ast));
    }

    [Fact]
    public Task OptimizationResult_FullToString()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | !a & c",
            new OptimizationOptions { IncludeTruthTables = true });
        return Verifier.Verify(result.ToString());
    }

    [Fact]
    public Task DebugDump_TreesAndMetrics()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & !b",
            new OptimizationOptions { IncludeMetrics = true, IncludeDebugInfo = true });

        return Verifier.Verify(result.DebugInfo).ScrubLinesWithReplace(line =>
            line.Contains("ms", StringComparison.OrdinalIgnoreCase) && line.Any(char.IsDigit)
                ? "[scrubbed timing]"
                : line);
    }

    [Fact]
    public Task TruthTable_Rendering()
    {
        var table = TruthTable.Generate(RandomExpressions.Parse("a & b | c"));
        return Verifier.Verify(table.ToString());
    }
}
