using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for compiled C# expression evaluation and truth table generation
/// </summary>
public class CompiledTruthTableTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Fact]
    public void CompiledTruthTable_SimpleExpression_ShouldGenerateCorrectly()
    {
        // Test simple expression: a & b
        var result = _optimizer.OptimizeExpression("a & b", true);

        // Verify compiled truth tables are generated
        Assert.NotNull(result.CompiledOriginalTruthTable);
        Assert.NotNull(result.CompiledOptimizedTruthTable);

        // Verify they are equivalent
        Assert.True(CompiledTruthTable.AreEquivalent(result.CompiledOriginalTruthTable,
            result.CompiledOptimizedTruthTable));

        // Verify truth table has correct structure
        Assert.Equal(2, result.CompiledOriginalTruthTable.Variables.Count);
        Assert.Equal(4, result.CompiledOriginalTruthTable.Rows.Count); // 2^2 = 4 combinations
    }

    [Theory]
    [InlineData("a & b", "a & b")]
    [InlineData("a | b", "a | b")]
    [InlineData("a & !b", "a & !b")]
    [InlineData("!a | b", "b | !a")]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    public void CompiledTruthTable_OptimizationEquivalence_ShouldVerifyCorrectly(string input, string expectedOptimized)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expectedOptimized, _optimizer);

        var result = _optimizer.OptimizeExpression(input, true);

        // Verify compiled truth tables exist and are equivalent
        Assert.NotNull(result.CompiledOriginalTruthTable);
        Assert.NotNull(result.CompiledOptimizedTruthTable);
        Assert.True(
            CompiledTruthTable.AreEquivalent(result.CompiledOriginalTruthTable, result.CompiledOptimizedTruthTable),
            $"Truth tables not equivalent for: {input} -> {result.Optimized}");
    }

    [Fact]
    public void CompiledTruthTable_ComplexExpression_ShouldEvaluateCorrectly()
    {
        // Test complex expression with multiple variables
        var complexExpr = "((a & !b) | (!a & b)) & (c | d)";
        var result = _optimizer.OptimizeExpression(complexExpr, true);

        var compiledTable = result.CompiledOriginalTruthTable!;

        // Verify structure
        Assert.Equal(4, compiledTable.Variables.Count);
        Assert.Equal(16, compiledTable.Rows.Count); // 2^4 = 16 combinations

        // Verify specific combinations manually
        // When a=T, b=F, c=T, d=F: (T & !F) | (!T & F) = T | F = T, and (T | F) = T, so result should be T & T = T
        var testRow = compiledTable.Rows.FirstOrDefault(r =>
            r.Variables["a"] && r.Variables["b"] == false &&
            r.Variables["c"] && r.Variables["d"] == false);

        Assert.NotNull(testRow);
        Assert.True(testRow.Result);
    }

    [Fact]
    public void CSharpExpressionExporter_BasicExpressions_ShouldExportCorrectly()
    {
        var lexer = new Lexer("a & b | !c");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Test expression export
        var expression = CSharpExpressionExporter.ToExpression(ast);
        Assert.Contains("&&", expression);
        Assert.Contains("||", expression);
        Assert.Contains("!", expression);

        // Test method generation
        var method = CSharpExpressionExporter.GenerateMethod(ast);
        Assert.Contains("public static bool", method);
        Assert.Contains("bool a", method);
        Assert.Contains("bool b", method);
        Assert.Contains("bool c", method);
    }

    [Fact]
    public void CompiledExpressionEvaluator_BasicEvaluation_ShouldWorkCorrectly()
    {
        var lexer = new Lexer("a & b");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        var evaluator = new CompiledExpressionEvaluator(ast);

        // Test all combinations for a & b
        Assert.False(evaluator.Evaluate(new Dictionary<string, bool> {{"a", false}, {"b", false}}));
        Assert.False(evaluator.Evaluate(new Dictionary<string, bool> {{"a", false}, {"b", true}}));
        Assert.False(evaluator.Evaluate(new Dictionary<string, bool> {{"a", true}, {"b", false}}));
        Assert.True(evaluator.Evaluate(new Dictionary<string, bool> {{"a", true}, {"b", true}}));
    }
}
