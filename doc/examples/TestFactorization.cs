using BooleanOptimizer;

class TestFactorization
{
    static void Main()
    {
        var optimizer = new BooleanExpressionOptimizer();
        var result = optimizer.OptimizeExpression("(a | b) & (a | c)");
        
        Console.WriteLine($"Input: (a | b) & (a | c)");
        Console.WriteLine($"Expected: a | (b & c)");
        Console.WriteLine($"Actual: {result.Optimized}");
        Console.WriteLine($"Test PASSED: {result.Optimized == "a | (b & c)"}");
    }
}