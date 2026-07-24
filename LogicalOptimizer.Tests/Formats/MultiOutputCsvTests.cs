using Xunit;

namespace LogicalOptimizer.Tests;

public class MultiOutputCsvTests
{
    private const string HalfAdderCsv = "a,b,Sum,Carry\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1";

    [Fact]
    public void ParseCsvToMultiOutputTable_HalfAdder_SplitsOutputs()
    {
        var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(HalfAdderCsv, new[] { "Sum", "Carry" });

        Assert.Equal(new List<string> { "a", "b" }, table.Variables);
        Assert.Equal(2, table.Outputs.Count);
        Assert.Equal(new[] { 1, 2 }, table.Outputs[0].OnSet.OrderBy(m => m));
        Assert.Equal(new[] { 3 }, table.Outputs[1].OnSet.OrderBy(m => m));
        Assert.Empty(table.Outputs[0].DontCareSet);
    }

    [Fact]
    public void ProcessCsvMultiOutput_HalfAdder_MinimalExpressions()
    {
        var results = CsvProcessor.ProcessCsvMultiOutput(HalfAdderCsv, new[] { "Sum", "Carry" });

        Assert.Equal(2, results.Count);
        Assert.Equal("Sum", results[0].Name);
        Assert.Equal("a & !b | b & !a", results[0].Expression);
        Assert.Equal(("Carry", "a & b"), results[1]);
    }

    [Fact]
    public void ProcessCsvMultiOutput_FullAdder_MinimalExpressions()
    {
        var csv = "a,b,cin,Sum,Cout\n" +
                  "0,0,0,0,0\n0,0,1,1,0\n0,1,0,1,0\n0,1,1,0,1\n" +
                  "1,0,0,1,0\n1,0,1,0,1\n1,1,0,0,1\n1,1,1,1,1";

        var results = CsvProcessor.ProcessCsvMultiOutput(csv, new[] { "Sum", "Cout" });
        var resultMap = results.ToDictionary(r => r.Name, r => r.Expression);

        // Sum is 3-input parity: 12 literals minimal; Cout is majority: 6 literals minimal
        var sumAst = new Parser(new Lexer(resultMap["Sum"]).Tokenize()).Parse();
        var coutAst = new Parser(new Lexer(resultMap["Cout"]).Tokenize()).Parse();
        Assert.Equal(12, AstMetrics.CountLiterals(sumAst));
        Assert.Equal(6, AstMetrics.CountLiterals(coutAst));
    }

    [Fact]
    public void ProcessCsvMultiOutput_PartialTable_SharedDontCares()
    {
        // Unspecified row (1,1) is don't-care for both outputs: X collapses to a | b, Y to b
        var csv = "a,b,X,Y\n0,0,0,0\n0,1,1,1\n1,0,1,0";

        var results = CsvProcessor.ProcessCsvMultiOutput(csv, new[] { "X", "Y" });
        var resultMap = results.ToDictionary(r => r.Name, r => r.Expression);

        Assert.Equal("a | b", resultMap["X"]);
        Assert.Equal("b", resultMap["Y"]);
    }

    [Fact]
    public void ParseCsvToMultiOutputTable_ContradictoryRows_Throws()
    {
        var csv = "a,b,Sum,Carry\n0,0,0,0\n0,0,1,0";
        Assert.Throws<ArgumentException>(() =>
            CsvTruthTableParser.ParseCsvToMultiOutputTable(csv, new[] { "Sum", "Carry" }));
    }

    [Fact]
    public void ParseCsvToMultiOutputTable_MissingOutputColumn_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CsvTruthTableParser.ParseCsvToMultiOutputTable(HalfAdderCsv, new[] { "Sum", "Overflow" }));
        Assert.Contains("Overflow", ex.Message);
    }

    [Fact]
    public void ParseCsvToMultiOutputTable_DuplicateIdenticalRows_Tolerated()
    {
        var csv = "a,b,Sum,Carry\n0,0,0,0\n0,0,0,0\n1,1,0,1";
        var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(csv, new[] { "Sum", "Carry" });

        Assert.Equal(new[] { 3 }, table.Outputs[1].OnSet.OrderBy(m => m));
    }
}
