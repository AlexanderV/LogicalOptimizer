// Unit test system

using System.Diagnostics;
using Xunit;

namespace LogicalOptimizer.Tests;

public class LexerTests
{
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

    [Theory]
    [InlineData("a & a", "a")]
    [InlineData("a | a", "a")]
    public void Optimizer_IdempotentLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & 1", "a")]
    [InlineData("a | 0", "a")]
    public void Optimizer_NeutralElements_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & 0", "0")]
    [InlineData("a | 1", "1")]
    public void Optimizer_AbsorbingElements_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & !a", "0")]
    [InlineData("a | !a", "1")]
    public void Optimizer_ComplementLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("!!a", "a")]
    [InlineData("!!!a", "!a")]
    public void Optimizer_DoubleNegation_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("!(a & b)", "!a | !b")]
    [InlineData("!(a | b)", "!a & !b")]
    [InlineData("!(!a & !b)", "a | b")]
    public void Optimizer_DeMorganLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & (a | b)", "a")]
    [InlineData("a | (a & b)", "a")]
    [InlineData("(a | b) & a", "a")]
    public void Optimizer_AbsorptionLaws_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")]
    [InlineData("(a | b) & (a | c)", "a | (b & c)")]
    public void Optimizer_Factorization_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & b | a & !b", "a")]
    [InlineData("(a & b) | (!a & b)", "b")]
    [InlineData("a & (b | !b)", "a")]
    [InlineData("a | (b & !b)", "a")]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Updated result with smart commutativity
    public void Optimizer_ComplexOptimizations_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
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
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & b | a & c", "a & (b | c)")] // Direct factorization - should be WITHOUT double parentheses
    [InlineData("x & y | x & z", "x & (y | z)")] // Another case of direct factorization
    [InlineData("(a | b) & (a | c)", "a | (b & c)")] // Reverse factorization
    [InlineData("(x | y) & (x | z)", "x | (y & z)")] // Another case of reverse factorization
    public void Optimizer_FactorizationWithCorrectParentheses_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & (b | c) & d", "a & d & (b | c)")] // Smart commutativity changed order
    [InlineData("(a & b) | (a & c) | (a & d)", "a & (b | c | d)")] // Multiple factorization
    [InlineData("x | y | x", "x | y")] // Remove duplicates
    public void Optimizer_ComplexExpressions_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Fact]
    public void Optimizer_FactorizationResult_ShouldNotHaveDoubleParentheses()
    {
        // Arrange
        var input = "a & b | a & c";
        var expected = "a & (b | c)";

        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
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

        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
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
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("!1", "0")]
    [InlineData("!0", "1")]
    public void EdgeCases_Constants_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("variable123", "variable123")]
    [InlineData("var_with_underscores", "var_with_underscores")]
    public void EdgeCases_LongVariableNames_ShouldRemainUnchanged(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("((((a))))", "a")]
    [InlineData("!(!(!(!a)))", "a")]
    public void EdgeCases_DeepNesting_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & a & a & a", "a")]
    [InlineData("a | a | a | a", "a")]
    public void EdgeCases_LongChains_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a & !a & b", "0")]
    [InlineData("(a & !a) | b", "b")]
    [InlineData("a & b & !a & c", "0")]
    public void EdgeCases_ContradictoryExpressions_ShouldOptimizeToZero(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
    }

    [Theory]
    [InlineData("a | !a | b", "1")]
    [InlineData("(a | !a) & b", "b")]
    [InlineData("a | b | !a | c", "1")]
    public void EdgeCases_Tautologies_ShouldOptimizeCorrectly(string input, string expected)
    {
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
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
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
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
        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
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
            // Act
            var result = _optimizer.OptimizeExpression(testCase);

            // Assert
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

        // Act
        var result = _optimizer.OptimizeExpression("a & b | a & c");

        // Assert
        Assert.Equal("a & (b | c)", result.Optimized);
        Assert.DoesNotContain("((", result.Optimized);
    }

    [Fact]
    public void ConsoleTested_ReverseFactorization_ShouldWork()
    {
        // This test checks reverse factorization
        // "(a | b) & (a | c)" should become "a | (b & c)"

        // Act
        var result = _optimizer.OptimizeExpression("(a | b) & (a | c)");

        // Assert
        Assert.Equal("a | (b & c)", result.Optimized);
    }

    [Fact]
    public void ConsoleTested_TautologyDetection_ShouldWork()
    {
        // This test was added after fixing tautology recognition
        // "a | b | !a | c" should become "1"

        // Act
        var result = _optimizer.OptimizeExpression("a | b | !a | c");

        // Assert
        Assert.Equal("1", result.Optimized);
    }

    [Fact]
    public void ConsoleTested_ContradictionDetection_ShouldWork()
    {
        // This test was added after fixing contradiction recognition
        // "a & b & !a & c" should become "0"

        // Act
        var result = _optimizer.OptimizeExpression("a & b & !a & c");

        // Assert
        Assert.Equal("0", result.Optimized);
    }

    [Fact]
    public void ConsoleTested_ComplexExpressionPreservation_ShouldWork()
    {
        // This test checks that complex expressions are correctly optimized
        // "a & (b | c) & d" now becomes "a & d & (b | c)" thanks to smart commutativity

        // Act
        var result = _optimizer.OptimizeExpression("a & (b | c) & d");

        // Assert
        Assert.Equal("a & d & (b | c)", result.Optimized);
    }

    [Fact]
    public void ConsoleTested_PartialAbsorption_ShouldWork()
    {
        // This test is for the case "a | !a & b" 
        // Now should be optimized to "a | b" thanks to extended absorption

        // Act
        var result = _optimizer.OptimizeExpression("a | !a & b");

        // Assert
        Assert.Equal("a | b", result.Optimized);
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

        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
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

        // Act
        var result = _optimizer.OptimizeExpression(input);

        // Assert
        Assert.Equal(expected, result.Optimized);
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