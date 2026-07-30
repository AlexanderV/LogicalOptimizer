using System.IO;
using System.Text.Json;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Verifies the machine-readable <c>--format=json</c> contract by parsing the emitted
///     document (a consumer's view), not just its text. The full schema shape is additionally
///     pinned by <c>SnapshotTests.CliOutput_Json</c>.
/// </summary>
public class JsonReportWriterTests
{
    private static JsonElement WriteAndParse(string expression)
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression);
        var writer = new StringWriter();
        JsonReportWriter.Write(writer, result);
        return JsonDocument.Parse(writer.ToString()).RootElement;
    }

    [Fact]
    public void Write_EmitsStableSchemaAndProvenFields()
    {
        var root = WriteAndParse("a & b | a & c");

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("a & b | a & c", root.GetProperty("input").GetString());
        Assert.Equal("a & (b | c)", root.GetProperty("optimized").GetString());
        Assert.True(root.GetProperty("equivalent").GetBoolean());
        Assert.Equal("MinimalProven", root.GetProperty("minimality").GetString());

        var cost = root.GetProperty("cost");
        Assert.Equal(4, cost.GetProperty("originalLiterals").GetInt32());
        Assert.Equal(3, cost.GetProperty("optimizedLiterals").GetInt32());

        Assert.Equal("Computed", root.GetProperty("cnf").GetProperty("status").GetString());
        Assert.Equal("a & (b | c)", root.GetProperty("cnf").GetProperty("expression").GetString());
        Assert.Equal("a & b | a & c", root.GetProperty("dnf").GetProperty("expression").GetString());

        var variables = root.GetProperty("variables").EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, variables);
    }

    [Fact]
    public void Write_OmitsAdvanced_WhenNoPattern_ButEmitsIt_ForXor()
    {
        // No XOR/IMP/EQV pattern here — the "advanced" field must be absent, not an echo.
        Assert.False(WriteAndParse("a & b | a & c").TryGetProperty("advanced", out _));

        Assert.Equal("a XOR b", WriteAndParse("a & !b | !a & b").GetProperty("advanced").GetString());
    }

    [Fact]
    public void WriteError_EmitsErrorObject_WithoutResultFields()
    {
        var writer = new StringWriter();
        JsonReportWriter.WriteError(writer, "a & & b", "Unexpected token & at position 4");
        var root = JsonDocument.Parse(writer.ToString()).RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("a & & b", root.GetProperty("input").GetString());
        Assert.Equal("processing_error", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("error").GetProperty("message").GetString()));
        Assert.False(root.TryGetProperty("optimized", out _));
    }
}
