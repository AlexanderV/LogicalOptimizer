using System;
using BooleanOptimizer;

class Program
{
    static void Main()
    {
        // Test LaTeX export with various expressions
        string[] testExpressions = {
            "a & b",
            "a | b", 
            "!a",
            "!(a & b)",
            "a & b | c",
            "(a | b) & (c | d)"
        };

        Console.WriteLine("=== LaTeX Export Test ===");
        Console.WriteLine();

        foreach (var expr in testExpressions)
        {
            try
            {
                var latex = BooleanExpressionExporter.ToLatex(expr);
                Console.WriteLine($"Expression: {expr}");
                Console.WriteLine($"LaTeX:      {latex}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error with '{expr}': {ex.Message}");
            }
        }
    }
}
