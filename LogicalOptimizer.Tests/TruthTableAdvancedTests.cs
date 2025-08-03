using System.Diagnostics;
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
    public void TruthTable_ConsensusRule_ShouldBeCorrect()
    {
        // Arrange - testing consensus rule: (a & b) | (!a & c) | (b & c) = (a & b) | (!a & c)
        var withConsensus = "(a & b) | (!a & c) | (b & c)";
        var withoutConsensus = "(a & b) | (!a & c)";

        // Act
        var table1 = TruthTable.Generate(withConsensus);
        var table2 = TruthTable.Generate(withoutConsensus);

        // Assert
        Assert.True(table1.IsEquivalentTo(table2),
            $"Consensus rule should make expressions equivalent:\n" +
            $"With consensus: {withConsensus} -> {table1.GetResultsString()}\n" +
            $"Without consensus: {withoutConsensus} -> {table2.GetResultsString()}");

        // Manual verification of key cases
        var rows = table1.Rows;
        for (var i = 0; i < rows.Count; i++)
        {
            var a = rows[i]["a"];
            var b = rows[i]["b"];
            var c = rows[i]["c"];

            var with_consensus = (a && b) || (!a && c) || (b && c);
            var without_consensus = (a && b) || (!a && c);

            Assert.Equal(with_consensus, without_consensus);
        }
    }

    [Fact]
    public void TruthTable_AbsorptionLaws_ShouldBeCorrect()
    {
        // Test: a | (a & b) = a
        var table1 = TruthTable.Generate("a | (a & b)");
        var table2 = TruthTable.Generate("a");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: a & (a | b) = a
        var table3 = TruthTable.Generate("a & (a | b)");
        var table4 = TruthTable.Generate("a");
        Assert.True(table3.IsEquivalentTo(table4));
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
    public void TruthTable_IdempotentLaws_ShouldBeCorrect()
    {
        // Test: a & a = a
        var table1 = TruthTable.Generate("a & a");
        var table2 = TruthTable.Generate("a");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: a | a = a
        var table3 = TruthTable.Generate("a | a");
        var table4 = TruthTable.Generate("a");
        Assert.True(table3.IsEquivalentTo(table4));
    }

    [Fact]
    public void TruthTable_NeutralElements_ShouldBeCorrect()
    {
        // Test: a & 1 = a
        var table1 = TruthTable.Generate("a & 1");
        var table2 = TruthTable.Generate("a");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: a | 0 = a
        var table3 = TruthTable.Generate("a | 0");
        var table4 = TruthTable.Generate("a");
        Assert.True(table3.IsEquivalentTo(table4));

        // Test: 1 & a = a
        var table5 = TruthTable.Generate("1 & a");
        var table6 = TruthTable.Generate("a");
        Assert.True(table5.IsEquivalentTo(table6));

        // Test: 0 | a = a
        var table7 = TruthTable.Generate("0 | a");
        var table8 = TruthTable.Generate("a");
        Assert.True(table7.IsEquivalentTo(table8));
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

    [Fact]
    public void TruthTable_ComplementLaws_ShouldBeCorrect()
    {
        // Test: a & !a = 0
        var table1 = TruthTable.Generate("a & !a");
        var table2 = TruthTable.Generate("0");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: a | !a = 1
        var table3 = TruthTable.Generate("a | !a");
        var table4 = TruthTable.Generate("1");
        Assert.True(table3.IsEquivalentTo(table4));
    }

    [Fact]
    public void TruthTable_DoubleNegation_ShouldBeCorrect()
    {
        // Test: !!a = a
        var table1 = TruthTable.Generate("!!a");
        var table2 = TruthTable.Generate("a");
        Assert.True(table1.IsEquivalentTo(table2));

        // Test: !!!a = !a
        var table3 = TruthTable.Generate("!!!a");
        var table4 = TruthTable.Generate("!a");
        Assert.True(table3.IsEquivalentTo(table4));

        // Test: !!!!a = a
        var table5 = TruthTable.Generate("!!!!a");
        var table6 = TruthTable.Generate("a");
        Assert.True(table5.IsEquivalentTo(table6));
    }

    [Theory]
    [InlineData("(a & b) & c", "a & (b & c)")] // Associativity of AND
    [InlineData("(a | b) | c", "a | (b | c)")] // Associativity of OR
    [InlineData("a & b", "b & a")] // Commutativity of AND
    [InlineData("a | b", "b | a")] // Commutativity of OR
    [InlineData("a & (b | c)", "(a & b) | (a & c)")] // Distributivity
    [InlineData("a | (b & c)", "(a | b) & (a | c)")] // Distributivity
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
    public void TruthTable_ComplexEquivalence_Quine2_ShouldBeCorrect()
    {
        // Another complex case
        var original = "(a & b) | (a & c) | (b & c)";
        var expanded = "(a & b & c) | (a & b & !c) | (a & !b & c) | (!a & b & c)";

        var table1 = TruthTable.Generate(original);
        var table2 = TruthTable.Generate(expanded);

        Assert.True(table1.IsEquivalentTo(table2),
            $"Complex equivalence should hold:\n" +
            $"Original: {original} -> {table1.GetResultsString()}\n" +
            $"Expanded: {expanded} -> {table2.GetResultsString()}");
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

        // Verify specific cases manually
        var rows = table.Rows;
        for (var i = 0; i < Math.Min(10, rows.Count); i++) // Check first 10 rows
        {
            var row = rows[i];
            bool a = row["a"], b = row["b"], c = row["c"], d = row["d"];
            bool e = row["e"], f = row["f"], g = row["g"], h = row["h"];

            var expected = ((a && b) || (c && d)) && (e || f) && (g || h);
            Assert.Equal(expected, table.Results[i]);
        }
    }

    [Fact]
    public void TruthTable_VariableNaming_EdgeCases_ShouldWork()
    {
        // Test with various variable naming patterns
        var expressions = new[]
        {
            "x1 & x2",
            "var_a | var_b",
            "A & B", // Uppercase
            "a1 & a2 & a3",
            "test_var1 | test_var2"
        };

        foreach (var expr in expressions)
        {
            var table = TruthTable.Generate(expr);
            Assert.True(table.Variables.Count >= 2);
            Assert.True(table.Results.Count >= 4);
            Assert.NotEmpty(table.GetResultsString());
        }
    }

    [Fact]
    public void TruthTable_Performance_LargeExpression_ShouldComplete()
    {
        // Test with a moderately large expression (6 variables = 64 rows)
        var largeExpr = "(a & b) | (c & d) | (e & f) | (!a & !b) | (!c & !d) | (!e & !f)";

        var stopwatch = Stopwatch.StartNew();
        var table = TruthTable.Generate(largeExpr);
        stopwatch.Stop();

        Assert.Equal(6, table.Variables.Count);
        Assert.Equal(64, table.Results.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, "Truth table generation should be fast");
    }

    [Fact]
    public void TruthTable_EquivalenceTransitivity_ShouldWork()
    {
        // Test transitive property: if A=B and B=C, then A=C
        var exprA = "a & b";
        var exprB = "b & a"; // Commutative
        var exprC = "a & b"; // Same as A

        var tableA = TruthTable.Generate(exprA);
        var tableB = TruthTable.Generate(exprB);
        var tableC = TruthTable.Generate(exprC);

        Assert.True(tableA.IsEquivalentTo(tableB));
        Assert.True(tableB.IsEquivalentTo(tableC));
        Assert.True(tableA.IsEquivalentTo(tableC)); // Transitivity
    }

    [Fact]
    public void TruthTable_NonEquivalent_ShouldBeDetected()
    {
        // Test cases that should NOT be equivalent
        var nonEquivalentPairs = new[]
        {
            ("a & b", "a | b"),
            ("a", "!a"),
            ("a & b & c", "a | b | c"),
            ("a & (b | c)", "a | (b & c)"),
            ("(a & b) | c", "a & (b | c)")
        };

        foreach (var (expr1, expr2) in nonEquivalentPairs)
        {
            var table1 = TruthTable.Generate(expr1);
            var table2 = TruthTable.Generate(expr2);

            Assert.False(table1.IsEquivalentTo(table2),
                $"Expressions should NOT be equivalent:\n" +
                $"Expression 1: {expr1} -> {table1.GetResultsString()}\n" +
                $"Expression 2: {expr2} -> {table2.GetResultsString()}");
        }
    }
}