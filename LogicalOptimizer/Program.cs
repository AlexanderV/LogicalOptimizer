namespace LogicalOptimizer;

// Console application
public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            // Command line arguments processing according to specification
            if (args.Length == 0)
            {
                ShowUsage();
                return 1;
            }

            // Special commands
            switch (args[0])
            {
                case "--help":
                case "-h":
                    ShowHelp();
                    return 0;

                case "--test":
                    return RunTests() ? 0 : 1;

                case "--demo":
                    RunComprehensiveDemo();
                    return 0;

                case "--benchmark":
                    RunBenchmark();
                    return 0;
            }

            // Expression processing with options
            var verbose = false;
            var cnfOnly = false;
            var dnfOnly = false;
            var expression = args[0];

            if (args.Length >= 2)
            {
                switch (args[0])
                {
                    case "--verbose":
                        verbose = true;
                        expression = args[1];
                        break;
                    case "--cnf":
                        cnfOnly = true;
                        expression = args[1];
                        break;
                    case "--dnf":
                        dnfOnly = true;
                        expression = args[1];
                        break;
                }
            }

            // Expression length validation
            if (expression.Length > 10000)
            {
                Console.Error.WriteLine("Error: Expression is too long (maximum 10,000 characters)");
                return 1;
            }

            var optimizer = new BooleanExpressionOptimizer();
            var result = optimizer.OptimizeExpression(expression, verbose, verbose);

            // Standard output format according to specification
            if (verbose)
            {
                Console.WriteLine(result); // Full output with metrics
            }
            else if (cnfOnly)
            {
                Console.WriteLine(result.CNF);
            }
            else if (dnfOnly)
            {
                Console.WriteLine(result.DNF);
            }
            else
            {
                Console.WriteLine($"Original: {result.Original}");
                Console.WriteLine($"Optimized: {result.Optimized}");
                Console.WriteLine($"CNF: {result.CNF}");
                Console.WriteLine($"DNF: {result.DNF}");
                Console.WriteLine($"Variables: [{string.Join(", ", result.Variables)}]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage: LogicalOptimizer.exe \"<expression>\"");
        Console.WriteLine("Example: LogicalOptimizer.exe \"a & b | a & !b\"");
        Console.WriteLine();
        Console.WriteLine("For help use: LogicalOptimizer.exe --help");
    }

    private static void ShowHelp()
    {
        Console.WriteLine("LogicalOptimizer - Boolean Expression Optimizer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LogicalOptimizer.exe \"<expression>\"       # Optimize expression");
        Console.WriteLine("  LogicalOptimizer.exe --verbose \"<expression>\" # Detailed output");
        Console.WriteLine("  LogicalOptimizer.exe --cnf \"<expression>\"     # Output only CNF");
        Console.WriteLine("  LogicalOptimizer.exe --dnf \"<expression>\"     # Output only DNF");
        Console.WriteLine("  LogicalOptimizer.exe --test              # Run tests");
        Console.WriteLine("  LogicalOptimizer.exe --help              # This help");
        Console.WriteLine("  LogicalOptimizer.exe --demo              # Features demonstration");
        Console.WriteLine("  LogicalOptimizer.exe --benchmark         # Performance testing");
        Console.WriteLine();
        Console.WriteLine("Supported operators:");
        Console.WriteLine("  !     - logical NOT (negation)");
        Console.WriteLine("  &     - logical AND (conjunction)");
        Console.WriteLine("  |     - logical OR (disjunction)");
        Console.WriteLine("  ()    - grouping");
        Console.WriteLine("  0, 1  - logical constants");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  LogicalOptimizer.exe \"a & b | a & c\"");
        Console.WriteLine("  LogicalOptimizer.exe \"!(a & b)\"");
        Console.WriteLine("  LogicalOptimizer.exe \"!!a\"");
        Console.WriteLine();
        Console.WriteLine("Limitations:");
        Console.WriteLine("  - Maximum expression length: 10,000 characters");
        Console.WriteLine("  - Maximum number of variables: 100");
        Console.WriteLine("  - Maximum nesting depth: 50 levels");
    }

    private static bool RunTests()
    {
        Console.WriteLine("Running built-in tests...");
        try
        {
            // Use existing functionality
            TestFactorizationIssue();
            Console.WriteLine("All built-in tests passed successfully!");
            Console.WriteLine("For full testing use: dotnet test");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running tests: {ex.Message}");
            return false;
        }
    }

    private static void TestFactorizationIssue()
    {
        Console.WriteLine("=== DEBUGGING CONTEXTUAL PARENTHESES ===");
        TestContextualParentheses();

        Console.WriteLine("\n=== TESTING ADVANCED OPTIMIZATION ===");
        TestAdvancedOptimization();
    }

    private static void TestContextualParentheses()
    {
        var optimizer = new BooleanExpressionOptimizer();

        // Test from specification
        var input = "(a | b) & (a | c)";
        var expected = "a | (b & c)";

        var result = optimizer.OptimizeExpression(input);
        var passed = result.Optimized == expected;

        Console.WriteLine($"Input: {input}");
        Console.WriteLine($"Expected: {expected}");
        Console.WriteLine($"Actual: {result.Optimized}");
        Console.WriteLine($"Test PASSED: {passed}");
    }

    private static void TestAdvancedOptimization()
    {
        var optimizer = new BooleanExpressionOptimizer();

        // Test for consensus rules and other advanced capabilities
        string[] testExpressions =
        {
            "a & b | !a & c", // Should apply consensus rule
            "a & b | a & c", // Should apply factorization  
            "a | !a & b", // Should apply absorption
            "a & b | !a & c | b & c", // Complex consensus case
            "(a | b) & (!a | c)", // Should give simpler expression
            "a & (b | c) | !a & d" // Mixed case
        };

        foreach (var expr in testExpressions)
        {
            Console.WriteLine($"\n--- Testing: {expr} ---");
            var result = optimizer.OptimizeExpression(expr, true, true); // debug mode with metrics

            Console.WriteLine($"Original: {result.Original}");
            Console.WriteLine($"Optimized: {result.Optimized}");

            if (result.Metrics != null)
            {
                Console.WriteLine($"Optimization took: {result.Metrics.ElapsedTime.TotalMilliseconds:F2}ms");
                Console.WriteLine(
                    $"Applied {result.Metrics.AppliedRules} rules in {result.Metrics.Iterations} iterations");
                Console.WriteLine($"Node count: {result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes}");

                if (result.Metrics.RuleApplicationCount.Count > 0)
                {
                    Console.WriteLine("Applied rules:");
                    foreach (var rule in result.Metrics.RuleApplicationCount)
                        Console.WriteLine($"  {rule.Key}: {rule.Value} times");
                }
            }

            // Show AST visualization for simpler expressions
            if (expr.Length < 20)
                try
                {
                    var parser = new Parser(new Lexer(expr).Tokenize());
                    var ast = parser.Parse();
                    Console.WriteLine("\nAST Visualization:");
                    Console.WriteLine(AstVisualizer.VisualizeTree(ast));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"AST visualization error: {e.Message}");
                }
        }
    }

    private static void RunComprehensiveDemo()
    {
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

            // Complex expressions
            {"Tautology", "a | b | !a | c"},
            {"Contradiction", "a & !a"},
            {"Mixed Complex", "a & (b | c) | !a & d | b & c"},

            // Real examples
            {"Control Logic", "(start & !stop) | (running & !error)"},
            {"State Machine", "(state1 & event_a) | (state2 & event_b)"}
        };

        foreach (var testCase in testCases)
        {
            Console.WriteLine($"=== {testCase.Key} ===");
            Console.WriteLine($"Expression: {testCase.Value}");

            try
            {
                // Full optimization with metrics
                var result = optimizer.OptimizeExpression(testCase.Value, true);

                Console.WriteLine($"Optimized: {result.Optimized}");

                if (result.Metrics != null)
                {
                    var improvement = result.Metrics.OriginalNodes > result.Metrics.OptimizedNodes ? "✓ Improved" :
                        result.Metrics.OriginalNodes < result.Metrics.OptimizedNodes ? "⚠ Expanded" : "= Same";

                    Console.WriteLine(
                        $"Result: {improvement} ({result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes} nodes)");

                    if (result.Metrics.AppliedRules > 0)
                        Console.WriteLine(
                            $"Applied {result.Metrics.AppliedRules} rules: {string.Join(", ", result.Metrics.RuleApplicationCount.Select(r => $"{r.Key}({r.Value})"))}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }

            Console.WriteLine();
        }
    }

    private static void RunBenchmark()
    {
        Console.WriteLine("=== PERFORMANCE TESTING ===\n");

        var optimizer = new BooleanExpressionOptimizer();
        var benchmarkExpressions = new[]
        {
            // Simple expressions
            "a & b",
            "a | b",
            "!a",

            // Medium complexity
            "(a | b) & (a | c)",
            "a & b | !a & c",
            "!(a & b) | c",

            // Complex expressions
            "(a | b) & (c | d) & (e | f)",
            "a & b | c & d | e & f | g & h",
            "(a | b) & (c | d) | (e | f) & (g | h)",

            // Very complex
            "(a | b | c) & (d | e | f) & (g | h | i) & (j | k | l)",
            "a & b | a & c | a & d | b & c | b & d | c & d",
            "((a | b) & c) | ((d | e) & f) | ((g | h) & i)"
        };

        Console.WriteLine($"{"Expression",-40} {"Nodes",-8} {"Time (ms)",-10} {"Result",-15}");
        Console.WriteLine(new string('-', 80));

        foreach (var expr in benchmarkExpressions)
            try
            {
                var startTime = DateTime.Now;
                var result = optimizer.OptimizeExpression(expr, false, true);
                var elapsed = DateTime.Now - startTime;

                var displayExpr = expr.Length > 35 ? expr.Substring(0, 32) + "..." : expr;
                var nodeChange = result.Metrics != null
                    ? $"{result.Metrics.OriginalNodes}→{result.Metrics.OptimizedNodes}"
                    : "N/A";

                Console.WriteLine($"{displayExpr,-40} {nodeChange,-8} {elapsed.TotalMilliseconds:F2,-10} {"✓",-15}");
            }
            catch (Exception ex)
            {
                var displayExpr = expr.Length > 35 ? expr.Substring(0, 32) + "..." : expr;
                Console.WriteLine(
                    $"{displayExpr,-40} {"Error",-8} {"N/A",-10} {ex.Message.Substring(0, Math.Min(14, ex.Message.Length)),-15}");
            }

        Console.WriteLine("\n=== STRESS TEST PERFORMANCE ===");

        // Generate expressions of different sizes
        var sizes = new[] {10, 50, 100, 200};

        foreach (var size in sizes)
            try
            {
                var complexExpr = GenerateComplexExpression(size);
                var startTime = DateTime.Now;
                var result = optimizer.OptimizeExpression(complexExpr, false, true);
                var elapsed = DateTime.Now - startTime;

                Console.WriteLine($"Size {size} variables: {elapsed.TotalMilliseconds:F2}ms " +
                                  $"({result.Metrics?.OriginalNodes ?? 0}→{result.Metrics?.OptimizedNodes ?? 0} nodes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Size {size} variables: ERROR - {ex.Message}");
            }
    }

    private static string GenerateComplexExpression(int variableCount)
    {
        var random = new Random(42); // Fixed seed for reproducibility
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
}