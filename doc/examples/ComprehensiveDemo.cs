using BooleanOptimizer;

Console.WriteLine("=== COMPREHENSIVE BOOLEAN OPTIMIZER DEMO ===\n");

var optimizer = new BooleanExpressionOptimizer();

// Demonstration expressions with different optimization types
var testCases = new Dictionary<string, string>
{
    // Basic rules
    {"De Morgan", "!(a & b)"},
    {"Double Negation", "!!a"},
    {"Absorption", "a | (a & b)"},
    {"Complement", "a | !a"},
    {"Factorization", "(a | b) & (a | c)"},
    
    // Advanced rules
    {"Extended Absorption", "a | (!a & b)"},
    {"Consensus", "a & b | !a & c"},
    {"Complex Consensus", "a & b | !a & c | b & c"},
    {"Distribution", "(a | b) & (c | d)"},
    
    // Complex expressions
    {"Tautology", "a | b | !a | c"},
    {"Contradiction", "a & !a"},
    {"Mixed Complex", "a & (b | c) | !a & d | b & c"},
    {"Nested", "((a | b) & c) | ((!a | !b) & !c)"},
    
    // Real examples
    {"Control Logic", "(start & !stop) | (running & !error)"},
    {"State Machine", "(state1 & event_a) | (state2 & event_b) | (state1 & state2)"},
    {"Error Conditions", "(!power | overload) & (temp_high | !cooling)"}
};

foreach (var testCase in testCases)
{
    Console.WriteLine($"=== {testCase.Key} ===");
    Console.WriteLine($"Expression: {testCase.Value}");
    
    try
    {
        // Full optimization with metrics and debugging
        var result = optimizer.OptimizeExpression(testCase.Value, true, false);
        
        Console.WriteLine($"Original: {result.Original}");
        Console.WriteLine($"Optimized: {result.Optimized}");
        Console.WriteLine($"CNF: {result.CNF}");
        Console.WriteLine($"DNF: {result.DNF}");
        
        if (result.Metrics != null)
        {
            var improvement = result.Metrics.OriginalNodes > result.Metrics.OptimizedNodes ? 
                "✓ Improved" : result.Metrics.OriginalNodes < result.Metrics.OptimizedNodes ?
                "⚠ Expanded" : "= Same";
                
            Console.WriteLine($"Optimization: {improvement} ({result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes} nodes)");
            Console.WriteLine($"Performance: {result.Metrics.ElapsedTime.TotalMilliseconds:F2}ms, {result.Metrics.Iterations} iterations");
            
            if (result.Metrics.AppliedRules > 0)
            {
                Console.WriteLine($"Applied {result.Metrics.AppliedRules} rules:");
                foreach (var rule in result.Metrics.RuleApplicationCount.OrderByDescending(r => r.Value))
                {
                    Console.WriteLine($"  • {rule.Key}: {rule.Value}x");
                }
            }
            else
            {
                Console.WriteLine("No optimization rules applied");
            }
        }
        
        // Check variable correctness
        var originalVars = new HashSet<string>();
        var optimizedVars = new HashSet<string>();
        
        try
        {
            var originalParser = new Parser(new Lexer(result.Original).Tokenize());
            var originalAst = originalParser.Parse();
            originalVars = originalAst.GetVariables();
            
            var optimizedParser = new Parser(new Lexer(result.Optimized).Tokenize());
            var optimizedAst = optimizedParser.Parse();
            optimizedVars = optimizedAst.GetVariables();
            
            if (!originalVars.SetEquals(optimizedVars))
            {
                Console.WriteLine($"⚠ Variable mismatch detected!");
                Console.WriteLine($"  Original vars: {string.Join(", ", originalVars.OrderBy(v => v))}");
                Console.WriteLine($"  Optimized vars: {string.Join(", ", optimizedVars.OrderBy(v => v))}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Error checking variables: {ex.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
    
    Console.WriteLine();
}

// Performance statistics
Console.WriteLine("=== PERFORMANCE SUMMARY ===");
var totalTime = 0.0;
var totalOptimizations = 0;
var totalRules = 0;

foreach (var testCase in testCases.Take(5)) // Test only first 5 for statistics
{
    var result = optimizer.OptimizeExpression(testCase.Value, true, false);
    if (result.Metrics != null)
    {
        totalTime += result.Metrics.ElapsedTime.TotalMilliseconds;
        totalOptimizations++;
        totalRules += result.Metrics.AppliedRules;
    }
}

Console.WriteLine($"Average optimization time: {totalTime / totalOptimizations:F2}ms");
Console.WriteLine($"Average rules per optimization: {(double)totalRules / totalOptimizations:F1}");
Console.WriteLine($"Total test cases: {testCases.Count}");

Console.WriteLine("\n=== AST VISUALIZATION EXAMPLES ===");

// Show AST for interesting cases
var interestingCases = new[] { "a & b | !a & c", "(a | b) & (a | c)", "a | (!a & b)" };

foreach (var expr in interestingCases)
{
    Console.WriteLine($"\nExpression: {expr}");
    try
    {
        var parser = new Parser(new Lexer(expr).Tokenize());
        var ast = parser.Parse();
        Console.WriteLine("Original AST:");
        Console.WriteLine(AstVisualizer.VisualizeTree(ast));
        
        var result = optimizer.OptimizeExpression(expr);
        var optimizedParser = new Parser(new Lexer(result.Optimized).Tokenize());
        var optimizedAst = optimizedParser.Parse();
        Console.WriteLine("Optimized AST:");
        Console.WriteLine(AstVisualizer.VisualizeTree(optimizedAst));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}
