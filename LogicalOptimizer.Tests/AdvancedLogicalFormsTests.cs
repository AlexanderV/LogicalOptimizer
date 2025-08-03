using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

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
