using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Extended tests for verifying truth table correctness in various scenarios
/// </summary>
public class TruthTableAdvancedTests
{
    [Fact]
    public void TruthTable_FactorizationEquivalence_ShouldBeCorrect()
    {
        // Arrange - test from specification
        var original = "(a | b) & (a | c)";
        var factorized = "a | (b & c)";

        // Act
        var table1 = TruthTable.Generate(original);
        var table2 = TruthTable.Generate(factorized);

        // Assert
        Assert.True(table1.IsEquivalentTo(table2),
            $"Expressions should be equivalent:\n" +
            $"Original: {original} -> {table1.GetResultsString()}\n" +
            $"Factorized: {factorized} -> {table2.GetResultsString()}");

        // Check correctness for each combination of inputs
        Assert.Equal(3, table1.Variables.Count);
        Assert.Equal(8, table1.Results.Count);

        // Manual verification of some key cases
        var rows1 = table1.Rows;
        var rows2 = table2.Rows;

        for (var i = 0; i < rows1.Count; i++)
        {
            var a = rows1[i]["a"];
            var b = rows1[i]["b"];
            var c = rows1[i]["c"];

            var original_result = (a || b) && (a || c);
            var factorized_result = a || (b && c);

            Assert.Equal(original_result, table1.Results[i]);
            Assert.Equal(factorized_result, table2.Results[i]);
            Assert.Equal(original_result, factorized_result);
        }
    }

    [Fact]
    public void TruthTable_ExtendedAbsorption_ShouldBeCorrect()
    {
        // Test: a | (!a & b) = a | b
        var table1 = TruthTable.Generate("a | (!a & b)");
        var table2 = TruthTable.Generate("a | b");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: a & (!a | b) = a & b
        var table3 = TruthTable.Generate("a & (!a | b)");
        var table4 = TruthTable.Generate("a & b");
        Assert.True(table3.IsEquivalentTo(table4));
    }

    [Fact]
    public void TruthTable_AbsorbingElements_ShouldBeCorrect()
    {
        // Test: a & 0 = 0
        var table1 = TruthTable.Generate("a & 0");
        var table2 = TruthTable.Generate("0");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: a | 1 = 1
        var table3 = TruthTable.Generate("a | 1");
        var table4 = TruthTable.Generate("1");
        Assert.True(table3.IsEquivalentTo(table4));
    }

    [Theory]
    [InlineData("(a & b) & c", "a & (b & c)")] // Associativity of AND
    [InlineData("(a | b) | c", "a | (b | c)")] // Associativity of OR
    [InlineData("a & b", "b & a")] // Commutativity of AND
    [InlineData("a | b", "b | a")] // Commutativity of OR
    [InlineData("a & (b | c)", "(a & b) | (a & c)")] // Distributivity
    [InlineData("a | (b & c)", "(a | b) & (a | c)")] // Distributivity
    [InlineData("!(a & b)", "!a | !b")] // De Morgan (AND)
    [InlineData("!(a | b)", "!a & !b")] // De Morgan (OR)
    [InlineData("!!a", "a")] // Double negation
    [InlineData("!!!a", "!a")] // Triple negation
    [InlineData("!!!!a", "a")] // Quadruple negation
    public void TruthTable_BasicLaws_ShouldBeEquivalent(string expr1, string expr2)
    {
        // Act
        var table1 = TruthTable.Generate(expr1);
        var table2 = TruthTable.Generate(expr2);

        // Assert
        Assert.True(table1.IsEquivalentTo(table2),
            $"Expressions should be equivalent:\n" +
            $"Expression 1: {expr1} -> {table1.GetResultsString()}\n" +
            $"Expression 2: {expr2} -> {table2.GetResultsString()}");
    }

    [Fact]
    public void TruthTable_ComplexEquivalence_Quine1_ShouldBeCorrect()
    {
        // Test a complex optimization case from Quine-McCluskey method
        var original = "(a & b & c) | (a & b & !c) | (a & !b & c) | (!a & b & c)";
        var optimized = "(a & b) | (a & c) | (b & c)";

        var table1 = TruthTable.Generate(original);
        var table2 = TruthTable.Generate(optimized);

        Assert.True(table1.IsEquivalentTo(table2),
            $"Complex optimization should be equivalent:\n" +
            $"Original: {original} -> {table1.GetResultsString()}\n" +
            $"Optimized: {optimized} -> {table2.GetResultsString()}");
    }

    [Fact]
    public void TruthTable_MixedConstants_ShouldBeCorrect()
    {
        // Test expressions with mixed variables and constants
        var testCases = new[]
        {
            ("a & 1 & b", "a & b"),
            ("a | 0 | b", "a | b"),
            ("a & 0 & b", "0"),
            ("a | 1 | b", "1"),
            ("(a | 0) & (b | 1)", "a"),
            ("(a & 1) | (b & 0)", "a")
        };

        foreach (var (expr1, expr2) in testCases)
        {
            var table1 = TruthTable.Generate(expr1);
            var table2 = TruthTable.Generate(expr2);

            Assert.True(table1.IsEquivalentTo(table2),
                $"Mixed constant expressions should be equivalent:\n" +
                $"Expression 1: {expr1} -> {table1.GetResultsString()}\n" +
                $"Expression 2: {expr2} -> {table2.GetResultsString()}");
        }
    }

    [Fact]
    public void TruthTable_NestedParentheses_ShouldBeCorrect()
    {
        // Test deeply nested expressions
        var nested = "((a & b) | (c & d)) & ((e | f) & (g | h))";
        var table = TruthTable.Generate(nested);

        Assert.Equal(8, table.Variables.Count); // a, b, c, d, e, f, g, h
        Assert.Equal(256, table.Results.Count); // 2^8 = 256

        // Verify every row against a C# oracle
        var rows = table.Rows;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool a = row["a"], b = row["b"], c = row["c"], d = row["d"];
            bool e = row["e"], f = row["f"], g = row["g"], h = row["h"];

            var expected = ((a && b) || (c && d)) && (e || f) && (g || h);
            Assert.Equal(expected, table.Results[i]);
        }
    }

    [Theory]
    [InlineData("x1 & x2", new[] { "x1", "x2" })]
    [InlineData("var_a | var_b", new[] { "var_a", "var_b" })]
    [InlineData("A & B", new[] { "A", "B" })] // Uppercase
    [InlineData("a1 & a2 & a3", new[] { "a1", "a2", "a3" })]
    [InlineData("test_var1 | test_var2", new[] { "test_var1", "test_var2" })]
    public void TruthTable_VariableNaming_EdgeCases_ShouldWork(string expression, string[] expectedVariables)
    {
        var table = TruthTable.Generate(expression);

        Assert.Equal(expectedVariables, table.Variables);
        Assert.Equal(1 << expectedVariables.Length, table.Results.Count);
        Assert.NotEmpty(table.GetResultsString());
    }

    [Theory]
    [InlineData("a & b", "a | b")]
    [InlineData("a", "!a")]
    [InlineData("a & b & c", "a | b | c")]
    [InlineData("a & (b | c)", "a | (b & c)")]
    [InlineData("(a & b) | c", "a & (b | c)")]
    public void TruthTable_NonEquivalent_ShouldBeDetected(string expr1, string expr2)
    {
        var table1 = TruthTable.Generate(expr1);
        var table2 = TruthTable.Generate(expr2);

        Assert.False(table1.IsEquivalentTo(table2),
            $"Expressions should NOT be equivalent:\n" +
            $"Expression 1: {expr1} -> {table1.GetResultsString()}\n" +
            $"Expression 2: {expr2} -> {table2.GetResultsString()}");

        // Non-equivalence must be symmetric
        Assert.False(table2.IsEquivalentTo(table1),
            $"Non-equivalence should be symmetric for '{expr1}' and '{expr2}'");
    }
}
