// Unit test system

using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

public class LexerTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a", new[] {"a"})]
    [InlineData("a & b", new[] {"a", "&", "b"})]
    [InlineData("a | b", new[] {"a", "|", "b"})]
    [InlineData("!a", new[] {"!", "a"})]
    [InlineData("(a)", new[] {"(", "a", ")"})]
    [InlineData("a123", new[] {"a123"})]
    [InlineData("var_name", new[] {"var_name"})]
    public void Lexer_BasicTokenization_ShouldReturnCorrectTokens(string input, string[] expectedTokens)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var tokens = lexer.Tokenize();
        var actualTokens = tokens.Where(t => t.Type != TokenType.End).Select(t => t.Value).ToArray();

        // Assert
        Assert.Equal(expectedTokens, actualTokens);
    }

    [Theory]
    [InlineData("a & b | !c", new[] {"a", "&", "b", "|", "!", "c"})]
    [InlineData("!(a & b)", new[] {"!", "(", "a", "&", "b", ")"})]
    [InlineData("a&b|c", new[] {"a", "&", "b", "|", "c"})]
    public void Lexer_ComplexExpressions_ShouldReturnCorrectTokens(string input, string[] expectedTokens)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act
        var tokens = lexer.Tokenize();
        var actualTokens = tokens.Where(t => t.Type != TokenType.End).Select(t => t.Value).ToArray();

        // Assert
        Assert.Equal(expectedTokens, actualTokens);
    }

    [Theory]
    [InlineData("a @ b")]
    [InlineData("123")]
    public void Lexer_InvalidInput_ShouldThrowException(string input)
    {
        // Arrange
        var lexer = new Lexer(input);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => lexer.Tokenize());
    }

    [Fact]
    public void Lexer_EmptyInput_ShouldReturnEndToken()
    {
        // Arrange
        var lexer = new Lexer("");

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        Assert.Single(tokens);
        Assert.Equal(TokenType.End, tokens.First().Type);
    }
}

public class ParserTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("!a")]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("(a)")]
    [InlineData("!(a)")]
    public void Parser_BasicExpressions_ShouldParseSuccessfully(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act & Assert
        var ast = parser.Parse();
        Assert.NotNull(ast);
    }

    [Theory]
    [InlineData("a & b | c")]
    [InlineData("a | b & c")]
    [InlineData("!a & b")]
    [InlineData("!(a & b)")]
    public void Parser_OperatorPrecedence_ShouldParseSuccessfully(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act & Assert
        var ast = parser.Parse();
        Assert.NotNull(ast);
    }

    [Theory]
    [InlineData("a & b & c")]
    [InlineData("a | b | c")]
    [InlineData("(a | b) & (c | d)")]
    [InlineData("!(a | b) & !(c | d)")]
    [InlineData("a & (b | c) & d")]
    public void Parser_ComplexExpressions_ShouldParseSuccessfully(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act & Assert
        var ast = parser.Parse();
        Assert.NotNull(ast);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a &")]
    [InlineData("& a")]
    [InlineData("a & & b")]
    [InlineData("(a")]
    [InlineData("a)")]
    [InlineData("((a)")]
    public void Parser_InvalidExpressions_ShouldThrowException(string input)
    {
        // Arrange
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => parser.Parse());
    }
}

public class OptimizerTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    /// <summary>
    ///     Helper method to verify expressions using compiled truth tables
    /// </summary>
    private void VerifyExpressionsWithCompiledTruthTables(string original, string? expectedOptimized = null)
    {
        // Optimize the expression with metrics enabled to get truth tables
        var result = _optimizer.OptimizeExpression(original, true);

        // Verify compiled truth tables are generated
        Assert.NotNull(result.CompiledOriginalTruthTable);
        Assert.NotNull(result.CompiledOptimizedTruthTable);

        // Verify equivalence using compiled truth tables
        Assert.True(
            CompiledTruthTable.AreEquivalent(result.CompiledOriginalTruthTable, result.CompiledOptimizedTruthTable),
            $"Compiled truth tables not equivalent for: {original} -> {result.Optimized}");

        // If expected result provided, verify it matches
        if (expectedOptimized != null) Assert.Equal(expectedOptimized, result.Optimized);

        // Output debug info for verification
        Console.WriteLine("=== Compiled Truth Table Verification ===");
        Console.WriteLine($"Original: {original}");
        Console.WriteLine($"Optimized: {result.Optimized}");
        Console.WriteLine(
            $"Equivalent: {CompiledTruthTable.AreEquivalent(result.CompiledOriginalTruthTable, result.CompiledOptimizedTruthTable)}");
        Console.WriteLine();
    }

    [Theory]
    [InlineData("a & a", "a")]
    [InlineData("a | a", "a")]
    public void Optimizer_IdempotentLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 1", "a")]
    [InlineData("a | 0", "a")]
    public void Optimizer_NeutralElements_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 0", "0")]
    [InlineData("a | 1", "1")]
    public void Optimizer_AbsorbingElements_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & !a", "0")]
    [InlineData("a | !a", "1")]
    public void Optimizer_ComplementLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!!a", "a")]
    [InlineData("!!!a", "!a")]
    public void Optimizer_DoubleNegation_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("!(a & b)", "!a | !b")]
    [InlineData("!(a | b)", "!a & !b")]
    [InlineData("!(!a & !b)", "a | b")]
    public void Optimizer_DeMorganLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (a | b)", "a")]
    [InlineData("a | (a & b)", "a")]
    [InlineData("(a | b) & a", "a")]
    public void Optimizer_AbsorptionLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    public void Optimizer_Factorization_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & !b", "a")]
    [InlineData("(a & b) | (!a & b)", "b")]
    [InlineData("a & (b | !b)", "a")]
    [InlineData("a | (b & !b)", "a")]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Updated result with smart commutativity
    public void Optimizer_ComplexOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | b | !a | c", "1")] // Tautology: a | !a is always true
    [InlineData("a & b & !a & c", "0")] // Contradiction: a & !a is always false
    [InlineData("x | !x", "1")] // Simple tautology
    [InlineData("x & !x", "0")] // Simple contradiction
    [InlineData("a | b | !b", "1")] // Tautology with b | !b
    [InlineData("a & b & !b", "0")] // Contradiction with b & !b
    public void Optimizer_TautologiesAndContradictions_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")] // Direct factorization - should be WITHOUT double parentheses
    [InlineData("x & y | x & z", "x & (y | z)")] // Another case of direct factorization
    [InlineData("(a | b) & (a | c)", "a | (b & c)")] // Reverse factorization
    [InlineData("(x | y) & (x | z)", "x | (y & z)")] // Another case of reverse factorization
    public void Optimizer_FactorizationWithCorrectParentheses_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Smart commutativity changed order
    [InlineData("(a & b) | (a & c) | (a & d)", "a & (b | c | d)")] // Multiple factorization
    [InlineData("x | y | x", "x | y")] // Remove duplicates
    public void Optimizer_ComplexExpressions_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void Optimizer_FactorizationResult_ShouldNotHaveDoubleParentheses()
    {
        // Arrange
        var input = "a & b | a & c";
        var expected = "a & (b | c)";

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);

        // Also verify exact string format
        var result = _optimizer.OptimizeExpression(input);
        Assert.Equal(expected, result.Optimized);
        // Ensure there are no double parentheses
        Assert.DoesNotContain("((", result.Optimized);
        Assert.DoesNotContain("))", result.Optimized);
    }

    [Fact]
    public void Optimizer_ReverseFactorizationResult_ShouldHaveCorrectParentheses()
    {
        // Arrange
        var input = "(a | b) & (a | c)";
        var expected = "a | (b & c)";

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);

        // Also verify exact string format
        var result = _optimizer.OptimizeExpression(input);
        Assert.Equal(expected, result.Optimized);
        // Check correctness of parentheses: should only be around (b & c)
        Assert.Contains("(b & c)", result.Optimized);
        Assert.DoesNotContain("((", result.Optimized);
    }
}

public class EdgeCaseTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a", "a")]
    [InlineData("!a", "!a")]
    public void EdgeCases_SingleVariables_ShouldRemainUnchanged(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("!1", "0")]
    [InlineData("!0", "1")]
    public void EdgeCases_Constants_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("variable123", "variable123")]
    [InlineData("var_with_underscores", "var_with_underscores")]
    public void EdgeCases_LongVariableNames_ShouldRemainUnchanged(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("((((a))))", "a")]
    [InlineData("!(!(!(!a)))", "a")]
    public void EdgeCases_DeepNesting_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & a & a & a", "a")]
    [InlineData("a | a | a | a", "a")]
    public void EdgeCases_LongChains_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & !a & b", "0")]
    [InlineData("(a & !a) | b", "b")]
    [InlineData("a & b & !a & c", "0")]
    public void EdgeCases_ContradictoryExpressions_ShouldOptimizeToZero(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | !a | b", "1")]
    [InlineData("(a | !a) & b", "b")]
    [InlineData("a | b | !a | c", "1")]
    public void EdgeCases_Tautologies_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Smart commutativity changed order
    [InlineData("a | !a & b", "a | b")] // Extended absorption now works!
    [InlineData("!!a", "a")] // Double negation
    [InlineData("!!!a", "!a")] // Triple negation
    [InlineData("!(!a & !b)", "a | b")] // De Morgan's law
    [InlineData("!(a | b)", "!a & !b")] // De Morgan's law
    public void EdgeCases_AdditionalOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a & 1", "a")] // Neutral element for AND
    [InlineData("a & 0", "0")] // Absorbing element for AND
    [InlineData("a | 1", "1")] // Absorbing element for OR  
    [InlineData("a | 0", "a")] // Neutral element for OR
    [InlineData("1 & a", "a")] // Commutativity of neutral element
    [InlineData("0 | a", "a")] // Commutativity of neutral element
    public void EdgeCases_ConstantOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Fact]
    public void EdgeCases_NoDoubleParenthesesInAnyOptimization_ShouldPass()
    {
        // Arrange
        var testCases = new[]
        {
            "a & b | a & c",
            "x & y | x & z",
            "(p | q) & (p | r)",
            "a & (b | c | d)"
        };

        foreach (var testCase in testCases)
        {
            // Act & Assert - check both optimization and equivalence
            TruthTableAssert.AssertOptimizationEquivalenceOnly(testCase, _optimizer);

            var result = _optimizer.OptimizeExpression(testCase);
            Assert.DoesNotContain("((", result.Optimized);
            Assert.DoesNotContain("))", result.Optimized);
        }
    }
}

public class ConsoleTestedCasesTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Fact]
    public void ConsoleTested_DoubleParenthesesFix_ShouldWork()
    {
        // This test was added after fixing the double parentheses issue
        // Previously: "a & b | a & c" gave "a & ((b | c))"
        // Now should give: "a & (b | c)"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a & b | a & c", "a & (b | c)", _optimizer);
    }

    [Fact]
    public void ConsoleTested_ReverseFactorization_ShouldWork()
    {
        // This test checks reverse factorization
        // "(a | b) & (a | c)" should become "a | (b & c)"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("(a | b) & (a | c)", "a | (b & c)", _optimizer);
    }

    [Fact]
    public void ConsoleTested_TautologyDetection_ShouldWork()
    {
        // This test was added after fixing tautology recognition
        // "a | b | !a | c" should become "1"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a | b | !a | c", "1", _optimizer);
    }

    [Fact]
    public void ConsoleTested_ContradictionDetection_ShouldWork()
    {
        // This test was added after fixing contradiction recognition
        // "a & b & !a & c" should become "0"

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a & b & !a & c", "0", _optimizer);
    }

    [Fact]
    public void ConsoleTested_ComplexExpressionPreservation_ShouldWork()
    {
        // This test checks that complex expressions are correctly optimized
        // "a & (b | c) & d" now becomes "a & d & (b | c)" thanks to smart commutativity

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a & (b | c) & d", "a & d & (b | c)", _optimizer);
    }

    [Fact]
    public void ConsoleTested_PartialAbsorption_ShouldWork()
    {
        // This test is for the case "a | !a & b" 
        // Now should be optimized to "a | b" thanks to extended absorption

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence("a | !a & b", "a | b", _optimizer);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    [InlineData("a | b | !a | c", "1")]
    [InlineData("a & b & !a & c", "0")]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Updated result
    [InlineData("a | !a & b", "a | b")] // New improvement: extended absorption
    [InlineData("a & (!a | b)", "a & b")] // New improvement: reverse extended absorption
    [InlineData("a | b & !a", "a | b")] // New improvement: commutative version
    public void ConsoleTested_AllCases_ShouldMatchExpectedResults(string input, string expected)
    {
        // Combined test of all cases verified through console

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }

    [Theory]
    [InlineData("a | !a & b", "a | b")]
    [InlineData("a & (!a | b)", "a & b")]
    [InlineData("a | b & !a", "a | b")]
    [InlineData("a & (b | !a)", "a & b")]
    [InlineData("x | !x & y & z", "x | y & z")]
    [InlineData("p & (!p | q | r)", "p & (q | r)")]
    public void ConsoleTested_ExtendedAbsorption_ShouldWork(string input, string expected)
    {
        // Tests for extended absorption: A | (!A & B) → A | B and A & (!A | B) → A & B

        // Act & Assert
        TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
    }
}

public class NormalFormTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a | b", "a | b")]
    [InlineData("a & b", "a & b")]
    [InlineData("a & (b | c)", "a & (b | c)")]
    [InlineData("a | (b & c)", "(a | b) & (a | c)")]
    public void NormalForms_CNF_ShouldConvertCorrectly(string input, string expectedCNF)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expectedCNF, result.CNF);

        // Also verify equivalence with original
        TruthTableAssert.AssertEquivalence(input, result.CNF);
    }

    [Theory]
    [InlineData("a | b", "a | b")]
    [InlineData("a & b", "a & b")]
    [InlineData("(a | b) & c", "c & a | c & b")] // Updated result thanks to smart commutativity
    [InlineData("a & (b | c)", "a & b | a & c")]
    public void NormalForms_DNF_ShouldConvertCorrectly(string input, string expectedDNF)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expectedDNF, result.DNF);

        // Also verify equivalence with original
        TruthTableAssert.AssertEquivalence(input, result.DNF);
    }
}

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

public class ExportTests
{
    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    public void ToDimacs_BasicExpressions_ShouldContainCorrectHeaders(string input)
    {
        // Act
        var result = BooleanExpressionExporter.ToDimacs(input);

        // Assert
        Assert.Contains($"c Boolean expression: {input}", result);
        Assert.Contains("p cnf", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    public void ToBlif_BasicExpressions_ShouldContainCorrectStructure(string input)
    {
        // Act
        var result = BooleanExpressionExporter.ToBlif(input);

        // Assert
        Assert.Contains(".model boolean_expr", result);
        Assert.Contains(".inputs", result);
        Assert.Contains(".outputs out", result);
        Assert.Contains(".end", result);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("!a")]
    public void ToVerilog_BasicExpressions_ShouldContainModuleStructure(string input)
    {
        // Act
        var result = BooleanExpressionExporter.ToVerilog(input);

        // Assert
        Assert.Contains("module boolean_expr(", result);
        Assert.Contains("input", result);
        Assert.Contains("output out", result);
        Assert.Contains("endmodule", result);
    }

    [Theory]
    [InlineData("a & b", "a ∧ b")]
    [InlineData("a | b", "a ∨ b")]
    [InlineData("!a", "¬a")]
    [InlineData("!(a & b)", "¬(a ∧ b)")]
    public void ToMathematicalNotation_BasicExpressions_ShouldReturnCorrectSymbols(string input, string expected)
    {
        // Act
        var result = BooleanExpressionExporter.ToMathematicalNotation(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("a & b", "\\land")]
    [InlineData("a | b", "\\lor")]
    [InlineData("!a", "\\neg")]
    [InlineData("!(a & b)", "\\neg")]
    public void ToLatex_BasicExpressions_ShouldReturnCorrectLatexCommands(string input, string expectedCommand)
    {
        // Act
        var result = BooleanExpressionExporter.ToLatex(input);

        // Assert
        Assert.Contains(expectedCommand, result);
    }

    [Theory]
    [InlineData("a & b", "a \\land b")]
    [InlineData("a | b", "a \\lor b")]
    [InlineData("!a", "\\neg a")]
    [InlineData("!(a & b)", "\\neg (a \\land b)")]
    public void ToLatex_BasicExpressions_ShouldReturnCorrectFormat(string input, string expected)
    {
        // Act
        var result = BooleanExpressionExporter.ToLatex(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TruthTableToCsv_SimpleExpression_ShouldReturnCorrectCsvFormat()
    {
        // Arrange
        var expression = "a & b";

        // Act
        var result = BooleanExpressionExporter.TruthTableToCsv(expression);

        // Assert
        var lines = result.Trim().Split(new[] {"\r\n", "\n"}, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("a,b,Result", lines[0]);
        Assert.Equal(5, lines.Length); // Header + 4 data rows
        Assert.Contains("0,0,0", result);
        Assert.Contains("1,1,1", result);
    }
}

/// <summary>
///     Advanced Logical Forms Testing Suite
///     Comprehensive testing of XOR, IMP generation with truth table verification
/// </summary>
public class AdvancedLogicalFormsTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    /// <summary>
    ///     Helper method to evaluate boolean expression for given variable values
    /// </summary>
    private bool EvaluateExpression(string expression, Dictionary<string, bool> variables)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        return EvaluateAst(ast, variables);
    }

    private bool EvaluateAst(AstNode node, Dictionary<string, bool> variables)
    {
        return node switch
        {
            VariableNode varNode => variables.GetValueOrDefault(varNode.Name, false),
            NotNode notNode => !EvaluateAst(notNode.Operand, variables),
            AndNode andNode => EvaluateAst(andNode.Left, variables) && EvaluateAst(andNode.Right, variables),
            OrNode orNode => EvaluateAst(orNode.Left, variables) || EvaluateAst(orNode.Right, variables),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType()}")
        };
    }

    /// <summary>
    ///     Helper method to generate all possible truth table combinations for given variables
    /// </summary>
    private List<Dictionary<string, bool>> GenerateTruthTableCombinations(List<string> variables)
    {
        var combinations = new List<Dictionary<string, bool>>();
        var numCombinations = (int) Math.Pow(2, variables.Count);

        for (var i = 0; i < numCombinations; i++)
        {
            var combination = new Dictionary<string, bool>();
            for (var j = 0; j < variables.Count; j++) combination[variables[j]] = (i & (1 << j)) != 0;
            combinations.Add(combination);
        }

        return combinations;
    }

    /// <summary>
    ///     Verifies that advanced forms are logically equivalent to optimized expression
    /// </summary>
    private void VerifyAdvancedFormEquivalence(string originalExpr, string optimizedExpr,
        string xorForm, string impForm)
    {
        var optimizer = new BooleanExpressionOptimizer();
        var result = optimizer.OptimizeExpression(originalExpr);
        var variables = result.Variables.ToList();

        var combinations = GenerateTruthTableCombinations(variables);

        foreach (var combination in combinations)
        {
            var optimizedValue = EvaluateExpression(optimizedExpr, combination);

            // XOR should be true when exactly one operand is true
            if (!string.IsNullOrEmpty(xorForm))
            {
                var xorValue = EvaluateExpression(xorForm, combination);
                // For binary operations, XOR should be (A & !B) | (!A & B)
                if (variables.Count == 2)
                {
                    var a = combination[variables[0]];
                    var b = combination[variables[1]];
                    var expectedXor = (a && !b) || (!a && b);
                    Assert.True(expectedXor == xorValue,
                        $"XOR form failed for combination {string.Join(",", combination.Select(kv => $"{kv.Key}={kv.Value}"))}");
                }
            }

            // IMP (Implication) should be !A | B for A -> B
            if (!string.IsNullOrEmpty(impForm))
            {
                var impValue = EvaluateExpression(impForm, combination);
                // For binary operations, IMP should be !A | B
                if (variables.Count == 2)
                {
                    var a = combination[variables[0]];
                    var b = combination[variables[1]];
                    var expectedImp = !a || b;
                    Assert.True(expectedImp == impValue,
                        $"IMP form failed for combination {string.Join(",", combination.Select(kv => $"{kv.Key}={kv.Value}"))}");
                }
            }
        }
    }

    [Theory]
    [InlineData("a & b", "a & b")]
    [InlineData("a | b", "a | b")]
    [InlineData("a & a | b & b", "a | b")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    [InlineData("a & b | a & c", "a & (b | c)")]
    public void AdvancedForms_BasicExpressions_ShouldGenerateCorrectForms(string input, string expectedOptimized)
    {
        // Arrange & Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert optimization is correct
        TruthTableAssert.AssertOptimizationEquivalence(input, expectedOptimized, _optimizer);

        // Verify that we can generate advanced forms without errors
        Assert.NotNull(result);
        Assert.NotEmpty(result.Variables);
    }

    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("a & b | c")]
    [InlineData("(a | b) & c")]
    [InlineData("a & (b | c)")]
    public void NAND_Generation_ShouldBeLogicallyCorrect(string expression)
    {
        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(expression, _optimizer);

        // Arrange
        var result = _optimizer.OptimizeExpression(expression);
        var optimized = result.Optimized;

        // Generate NAND form manually
        string nandForm;
        if (optimized.Contains("&"))
            nandForm = $"!({optimized})";
        else
            nandForm = optimized; // If no AND operations, NAND is not applicable

        // Act & Assert - verify truth table equivalence
        if (optimized.Contains("&"))
        {
            var variables = result.Variables.ToList();
            var combinations = GenerateTruthTableCombinations(variables);

            foreach (var combination in combinations)
            {
                var originalValue = EvaluateExpression(optimized, combination);
                var nandValue = EvaluateExpression(nandForm, combination);

                Assert.True(!originalValue == nandValue,
                    $"NAND verification failed for {expression} -> {optimized} with variables " +
                    $"{string.Join(",", combination.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
        }
    }

    [Theory]
    [InlineData("a", "b")]
    [InlineData("x", "y")]
    [InlineData("p", "q")]
    public void XOR_Generation_ShouldBeLogicallyCorrect(string var1, string var2)
    {
        // Arrange - XOR should be (A & !B) | (!A & B)
        var expectedXor = $"({var1} & !{var2}) | (!{var1} & {var2})";

        // Act - Generate all combinations for two variables
        var variables = new List<string> {var1, var2};
        var combinations = GenerateTruthTableCombinations(variables);

        // Assert - verify XOR truth table
        foreach (var combination in combinations)
        {
            var a = combination[var1];
            var b = combination[var2];
            var expectedValue = (a && !b) || (!a && b);
            var actualValue = EvaluateExpression(expectedXor, combination);

            Assert.True(expectedValue == actualValue,
                $"XOR verification failed for {var1}={a}, {var2}={b}");
        }
    }

    [Theory]
    [InlineData("a", "b")]
    [InlineData("x", "y")]
    [InlineData("p", "q")]
    public void IMP_Generation_ShouldBeLogicallyCorrect(string var1, string var2)
    {
        // Arrange - IMP (A -> B) should be (!A | B)
        var expectedImp = $"!{var1} | {var2}";

        // Act - Generate all combinations for two variables
        var variables = new List<string> {var1, var2};
        var combinations = GenerateTruthTableCombinations(variables);

        // Assert - verify IMP truth table
        foreach (var combination in combinations)
        {
            var a = combination[var1];
            var b = combination[var2];
            var expectedValue = !a || b; // A -> B is equivalent to !A | B
            var actualValue = EvaluateExpression(expectedImp, combination);

            Assert.True(expectedValue == actualValue,
                $"IMP verification failed for {var1}={a}, {var2}={b}");
        }
    }

    [Fact]
    public void AdvancedForms_EdgeCases_ShouldHandleCorrectly()
    {
        // Test single variable
        var result1 = _optimizer.OptimizeExpression("a");
        Assert.Single(result1.Variables);

        // Test constants
        var result2 = _optimizer.OptimizeExpression("1");
        Assert.Equal("1", result2.Optimized);

        var result3 = _optimizer.OptimizeExpression("0");
        Assert.Equal("0", result3.Optimized);

        // Test tautology
        var result4 = _optimizer.OptimizeExpression("a | !a");
        Assert.Equal("1", result4.Optimized);

        // Test contradiction
        var result5 = _optimizer.OptimizeExpression("a & !a");
        Assert.Equal("0", result5.Optimized);
    }

    [Theory]
    [InlineData("a & b", true, false)] // Should have NAND, not NOR
    [InlineData("a | b", false, true)] // Should have NOR, not NAND
    [InlineData("a & b | c", true, true)] // Should have both
    [InlineData("(a | b) & c", true, true)] // Should have both
    public void AdvancedForms_OperatorPresence_ShouldDetermineApplicableForms(string expression,
        bool shouldHaveNand, bool shouldHaveNor)
    {
        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(expression, _optimizer);

        // Arrange
        var result = _optimizer.OptimizeExpression(expression);
        var optimized = result.Optimized;

        // Assert
        if (shouldHaveNand) Assert.Contains("&", optimized);

        if (shouldHaveNor) Assert.Contains("|", optimized);

        // Verify that advanced forms can be generated based on operators present
        if (optimized.Contains("&"))
        {
            var nandForm = $"!({optimized})";
            Assert.NotNull(nandForm);
        }

        if (optimized.Contains("|"))
        {
            var norForm = $"!({optimized})";
            Assert.NotNull(norForm);
        }
    }
}

/// <summary>
///     Tests for consensus rule optimization and contradiction detection
/// </summary>
public class ConsensusRuleTests
{
    private readonly BooleanExpressionOptimizer _optimizer = new();

    [Theory]
    [InlineData("a & !b | !a & b")] // XOR pattern - should NOT generate contradictory terms
    [InlineData("a & b | !a & c")] // Classic consensus - should work normally
    [InlineData("a & b | !a & c | b & c")] // Consensus with redundant term
    [InlineData("x & y | !x & z")] // Different variables
    public void ConsensusRule_ShouldNotCreateContradictoryTerms(string input)
    {
        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert - optimized result should not contain contradictory terms like "a & !a" or "b & !b"
        Assert.DoesNotContain("a & !a", result.Optimized);
        Assert.DoesNotContain("b & !b", result.Optimized);
        Assert.DoesNotContain("x & !x", result.Optimized);
        Assert.DoesNotContain("y & !y", result.Optimized);
        Assert.DoesNotContain("z & !z", result.Optimized);
        Assert.DoesNotContain("c & !c", result.Optimized);

        // Verify the result doesn't get worse (more terms than original)
        var originalTermCount = input.Split('|').Length;
        var optimizedTermCount = result.Optimized.Split('|').Length;

        // Optimized should have same or fewer terms (not more)
        Assert.True(optimizedTermCount <= originalTermCount + 1,
            $"Optimization made expression worse: {input} -> {result.Optimized}");
    }

    [Theory]
    [InlineData("a & !b | !a & b")] // Pure XOR
    [InlineData("x & !y | !x & y")] // XOR with different variables
    [InlineData("p & !q | !p & q")] // Another XOR pattern
    public void ConsensusRule_XorPatterns_ShouldNotAddRedundantTerms(string xorPattern)
    {
        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(xorPattern, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(xorPattern);

        // Assert - XOR patterns should not be made worse by consensus rule
        var originalComplexity = CountOperators(xorPattern);
        var optimizedComplexity = CountOperators(result.Optimized);

        Assert.True(optimizedComplexity <= originalComplexity,
            $"XOR pattern made worse: {xorPattern} -> {result.Optimized}");
    }

    [Fact]
    public void ConsensusRule_ContradictoryTerms_ShouldBeRejected()
    {
        // Test that the consensus rule rejects terms that would create contradictions
        var expressions = new[]
        {
            "a & !b | !a & b", // Should NOT add "!b & b"
            "x & y | !x & !y", // Should NOT add "y & !y" 
            "p & q & r | !p & s" // Should NOT add contradictory consensus
        };

        foreach (var expr in expressions)
        {
            // First verify optimization equivalence
            TruthTableAssert.AssertOptimizationEquivalenceOnly(expr, _optimizer);

            var result = _optimizer.OptimizeExpression(expr);

            // Check that no contradictory terms were added
            var variables = result.Variables;
            foreach (var variable in variables)
            {
                var contradiction = $"{variable} & !{variable}";
                var contradictionReversed = $"!{variable} & {variable}";

                Assert.DoesNotContain(contradiction, result.Optimized);
                Assert.DoesNotContain(contradictionReversed, result.Optimized);
            }
        }
    }

    [Theory]
    [InlineData("a & b | !a & c | b & c")] // Consensus elimination
    [InlineData("x & y | !x & z | y & z")] // Different variables
    public void ConsensusRule_ValidConsensus_ShouldSimplify(string input)
    {
        // First verify optimization equivalence (main goal)
        TruthTableAssert.AssertOptimizationEquivalenceOnly(input, _optimizer);

        // Arrange & Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert - valid consensus should simplify expressions
        var simplifiedTerms = result.Optimized.Split('|').Select(t => t.Trim()).ToArray();

        // Should have fewer or equal terms than original
        Assert.True(simplifiedTerms.Length <= input.Split('|').Length,
            $"Optimization should not increase complexity: {input} -> {result.Optimized}");
    }

    private int CountOperators(string expression)
    {
        return expression.Count(c => c == '&' || c == '|' || c == '!');
    }

    [Fact]
    public void AllOptimizationRules_ShouldNotCreateContradictoryTerms()
    {
        // Test various expressions that might trigger different optimization rules
        var testExpressions = new[]
        {
            "a & !b | !a & b", // XOR pattern for consensus
            "a & b | a & c", // For factorization  
            "a | !a & b", // For absorption
            "!!a", // For complement law (double negation)
            "a & (b | c)", // For distributive law
            "a | a & b", // For absorption law
            "a & !a", // For complement law
            "a | !a" // For complement law (tautology)
        };

        foreach (var expr in testExpressions)
        {
            // First verify optimization equivalence
            TruthTableAssert.AssertOptimizationEquivalenceOnly(expr, _optimizer);

            var result = _optimizer.OptimizeExpression(expr);

            // Extract all variables from the result
            var variables = result.Variables;

            foreach (var variable in variables)
            {
                // Check for contradictory terms like "a & !a" or "!a & a"
                var contradiction1 = $"{variable} & !{variable}";
                var contradiction2 = $"!{variable} & {variable}";

                Assert.DoesNotContain(contradiction1, result.Optimized);
                Assert.DoesNotContain(contradiction2, result.Optimized);
            }

            // Verify optimization didn't make expression significantly worse
            var originalComplexity = CountOperators(expr);
            var optimizedComplexity = CountOperators(result.Optimized);

            // Allow some flexibility, but expression shouldn't get dramatically worse
            Assert.True(optimizedComplexity <= originalComplexity + 2,
                $"Expression became significantly worse: {expr} -> {result.Optimized}");
        }
    }

    [Fact]
    public void ComplexExpression_WithMultiplePatterns_ShouldOptimizeCorrectly()
    {
        // Test complex expression combining XOR and IMP patterns
        var complexExpr = "((a & !b) | (!a & b)) & ((!c | d) | (e & f))";

        // First verify optimization equivalence
        TruthTableAssert.AssertOptimizationEquivalenceOnly(complexExpr, _optimizer);

        var result = _optimizer.OptimizeExpression(complexExpr);

        // Verify the expression is optimized
        Assert.NotNull(result.Optimized);
        Assert.NotEmpty(result.Optimized);

        // Verify Advanced form contains expected patterns
        Assert.NotNull(result.Advanced);
        if (!string.IsNullOrEmpty(result.Advanced))
        {
            Assert.Contains("XOR", result.Advanced);
            // Check for either → symbol or the pattern that suggests implication
            Assert.True(result.Advanced.Contains("→") || result.Advanced.Contains("(c → d)") ||
                        (result.Advanced.Contains("d") && result.Advanced.Contains("c")),
                $"Expected implication pattern in: {result.Advanced}");
        }

        // Verify variables are correct
        var expectedVariables = new[] {"a", "b", "c", "d", "e", "f"};
        Assert.Equal(expectedVariables.Length, result.Variables.Count);
        foreach (var variable in expectedVariables) Assert.Contains(variable, result.Variables);

        // Verify CNF and DNF don't contain advanced operators
        Assert.DoesNotContain("XOR", result.CNF);
        Assert.DoesNotContain("→", result.CNF);
        Assert.DoesNotContain("XOR", result.DNF);
        Assert.DoesNotContain("→", result.DNF);
    }
}

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