using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer;

/// <summary>
/// Utility class for generating demonstration expressions and random test cases
/// </summary>
public static class ExpressionGenerator
{
    /// <summary>
    /// Generate a random boolean expression for testing
    /// </summary>
    public static string GenerateRandomExpression(int variableCount = 3)
    {
        var random = new Random();
        var variables = Enumerable.Range(0, variableCount)
            .Select(i => $"v{i}")
            .ToArray();

        var terms = new List<string>();

        // Generate random terms
        for (var i = 0; i < variableCount / 2; i++)
        {
            var var1 = variables[random.Next(variables.Length)];
            var var2 = variables[random.Next(variables.Length)];
            var neg1 = random.Next(2) == 0 ? "!" : "";
            var neg2 = random.Next(2) == 0 ? "!" : "";
            var op = random.Next(2) == 0 ? "&" : "|";

            terms.Add($"({neg1}{var1} {op} {neg2}{var2})");
        }

        return string.Join(" | ", terms);
    }

    /// <summary>
    /// Get demonstration expressions showcasing various features
    /// </summary>
    public static Dictionary<string, string> GetDemonstrationExpressions()
    {
        return new Dictionary<string, string>
        {
            {"Simple Factorization", "a & b | a & c"},
            {"De Morgan Law", "!(a & b)"},
            {"Double Negation", "!!a"},
            {"Absorption", "a | a & b"},
            {"XOR Pattern", "a & !b | !a & b"},
            {"Implication", "!a | b"},
            {"Consensus", "a & b | !a & c"},
            {"Complex Consensus", "a & b | !a & c | b & c"},
            {"Tautology", "a | !a"},
            {"Contradiction", "a & !a"},
            {"Complex Expression", "(a | b) & (c | d) & (a | d)"},
            {"Mixed Operations", "a & (b | c) & !d | e"},
            {"Nested Parentheses", "((a | b) & c) | (d & (e | f))"}
        };
    }

    /// <summary>
    /// Get test expressions for performance benchmarking
    /// </summary>
    public static Dictionary<string, string> GetBenchmarkExpressions()
    {
        return new Dictionary<string, string>
        {
            {"Small (3 vars)", "a & b | a & c | b & c"},
            {"Medium (5 vars)", "a & b & c | d & e | a & d | b & e | c & d"},
            {"Large (8 vars)", "a & b & c | d & e & f | g & h | a & g | b & h | c & d | e & g | f & h"},
            {"Complex Nested", "((a | b) & (c | d)) | ((e | f) & (g | h)) | (a & e) | (b & f)"},
            {"Deep Factorization", "a & b & c | a & b & d | a & e & f | a & e & g"},
            {"XOR Chain", "a & !b | !a & b | c & !d | !c & d"},
            {"Implication Chain", "!a | b | !c | d | !e | f"}
        };
    }
}
