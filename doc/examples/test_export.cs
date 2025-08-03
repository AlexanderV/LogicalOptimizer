using System;
using BooleanOptimizer;

class TestExport
{
    static void Main()
    {
        Console.WriteLine("=== DIMACS Export ===");
        Console.WriteLine(BooleanExpressionExporter.ToDimacs("a & b"));
        
        Console.WriteLine("\n=== BLIF Export ===");
        Console.WriteLine(BooleanExpressionExporter.ToBlif("a & b"));
        
        Console.WriteLine("\n=== LaTeX Export ===");
        Console.WriteLine(BooleanExpressionExporter.ToLatex("a & b"));
        
        Console.WriteLine("\n=== Mathematical Notation ===");
        Console.WriteLine(BooleanExpressionExporter.ToMathematicalNotation("a & b"));
        
        Console.WriteLine("\n=== CSV Export ===");
        Console.WriteLine(BooleanExpressionExporter.TruthTableToCsv("a & b"));
    }
}
