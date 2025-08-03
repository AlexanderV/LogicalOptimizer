using BooleanOptimizer;

Console.WriteLine("=== TESTING ADVANCED OPTIMIZATION ===");

var optimizer = new BooleanExpressionOptimizer();

// Test for consensus rules
string[] testExpressions = {
    "a & b | !a & c",  // Should apply consensus rule
    "a & b | a & c",   // Should apply factorization  
    "a | !a & b",      // Should apply absorption
    "a & b | !a & c | b & c",  // Complex consensus case
    "(a | b) & (!a | c)",      // Should give a & c | b & !a
    "a & b & c | !a & d | !b & e | !c & f"  // Complex case
};

foreach (var expr in testExpressions)
{
    Console.WriteLine($"\n--- Testing: {expr} ---");
    var result = optimizer.Optimize(expr, true); // debug mode

    Console.WriteLine($"Original: {result.Original}");
    Console.WriteLine($"Optimized: {result.Optimized}");

    if (result.Metrics != null)
    {
        Console.WriteLine($"Optimization took: {result.Metrics.ElapsedTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"Applied {result.Metrics.AppliedRules} rules in {result.Metrics.Iterations} iterations");
        Console.WriteLine($"Node count: {result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes}");

        if (result.Metrics.RuleApplicationCount.Count > 0)
        {
            Console.WriteLine("Applied rules:");
            foreach (var rule in result.Metrics.RuleApplicationCount)
            {
                Console.WriteLine($"  {rule.Key}: {rule.Value} times");
            }
        }
    }

    // Show AST visualization
    var parser = new Parser(new Lexer(expr).Tokenize());
    var ast = parser.Parse();
    Console.WriteLine("\nAST Visualization:");
    Console.WriteLine(AstVisualizer.VisualizeTree(ast));
}
