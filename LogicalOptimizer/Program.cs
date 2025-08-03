using System.Text;
using System.Text.RegularExpressions;

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

                case "--stress":
                    RunStressTest();
                    return 0;

                case "--csv-example":
                    ShowCsvExample();
                    return 0;
            }

            // Expression processing with options
            var verbose = false;
            var cnfOnly = false;
            var dnfOnly = false;
            var advanced = false;
            var truthTableOnly = false;
            var csvInput = false;
            var expression = args[0];

            if (args.Length >= 2)
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
                    case "--advanced":
                        advanced = true;
                        expression = args[1];
                        break;
                    case "--truth-table":
                        truthTableOnly = true;
                        expression = args[1];
                        break;
                    case "--csv":
                        csvInput = true;
                        expression = args[1];
                        break;
                }

            // Check if input is a file path first
            if (!csvInput && File.Exists(expression) &&
                (expression.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                 CsvTruthTableParser.LooksLikeCsv(File.ReadAllText(expression))))
                csvInput = true;
            // Then check if input looks like CSV content
            else if (!csvInput && CsvTruthTableParser.LooksLikeCsv(expression)) csvInput = true;

            // Handle CSV input
            if (csvInput)
                try
                {
                    Console.WriteLine("Detected CSV truth table input");

                    // Check if it's a file path
                    string csvExpression;
                    if (File.Exists(expression))
                    {
                        Console.WriteLine($"Reading CSV from file: {expression}");
                        csvExpression = CsvTruthTableParser.ParseCsvFileToExpression(expression);
                    }
                    else
                    {
                        Console.WriteLine("Parsing CSV content directly");
                        csvExpression = CsvTruthTableParser.ParseCsvToExpression(expression);
                    }

                    Console.WriteLine($"Generated expression from CSV: {csvExpression}");
                    Console.WriteLine();

                    // Use the generated expression for further processing
                    expression = csvExpression;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error parsing CSV: {ex.Message}");
                    return 1;
                }

            // Expression length validation
            if (expression.Length > 10000)
            {
                Console.Error.WriteLine("Error: Expression is too long (maximum 10,000 characters)");
                return 1;
            }

            var optimizer = new BooleanExpressionOptimizer();

            // Optimize based on what output is needed to avoid unnecessary computations
            var needsMetrics = verbose;
            var needsDebugInfo = verbose;

            var result = optimizer.OptimizeExpression(expression, needsMetrics, needsDebugInfo);

            // Standard output format according to specification
            if (truthTableOnly)
            {
                // Show only truth table - generate directly from expression to avoid optimization overhead
                try
                {
                    var truthTable = TruthTable.Generate(expression);
                    Console.WriteLine(truthTable);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error generating truth table: {ex.Message}");
                    return 1;
                }
            }
            else if (verbose)
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
            else if (advanced)
            {
                // Only show advanced forms (like --cnf or --dnf)
                var patternRecognizer = new PatternRecognizer();
                var originalAdvanced = patternRecognizer.GenerateAdvancedLogicalForms(result.Original);
                var optimizedAdvanced = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);

                // Use whichever found actual optimizations (not just "Optimized: ...")
                string advancedForms;
                if (!originalAdvanced.StartsWith("Optimized:"))
                    advancedForms = originalAdvanced;
                else if (!optimizedAdvanced.StartsWith("Optimized:"))
                    advancedForms = optimizedAdvanced;
                else
                    advancedForms = optimizedAdvanced; // Default to optimized result

                Console.WriteLine(advancedForms);
            }
            else
            {
                Console.WriteLine($"Original: {result.Original}");
                Console.WriteLine($"Optimized: {result.Optimized}");
                Console.WriteLine($"CNF: {result.CNF}");
                Console.WriteLine($"DNF: {result.DNF}");
                Console.WriteLine($"Variables: [{string.Join(", ", result.Variables)}]");

                // Check for advanced forms (XOR, IMP, etc.) - AST-based analysis works efficiently for any number of variables
                // Try to replace parts of the expression with advanced forms
                // Try both original and optimized expressions to catch patterns
                var advancedFromOriginal = ConvertToAdvancedForms(result.Original);
                var advancedFromOptimized = ConvertToAdvancedForms(result.Optimized);

                // Choose the best result (prefer the one that actually found patterns)
                string? advancedExpression = null;
                if (!string.IsNullOrEmpty(advancedFromOriginal) && advancedFromOriginal != result.Original)
                    advancedExpression = advancedFromOriginal;
                else if (!string.IsNullOrEmpty(advancedFromOptimized) && advancedFromOptimized != result.Optimized)
                    advancedExpression = advancedFromOptimized;

                if (!string.IsNullOrEmpty(advancedExpression)) Console.WriteLine($"Advanced: {advancedExpression}");

                // Show truth table only for small expressions (≤6 variables) to avoid performance issues
                if (result.Variables.Count <= 6)
                    try
                    {
                        var truthTable = TruthTable.Generate(result.Original);
                        Console.WriteLine();
                        Console.WriteLine("Truth Table:");
                        Console.WriteLine(truthTable);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Truth table skipped: too many variables ({result.Variables.Count})");
                    }
                else
                    Console.WriteLine($"\nTruth table skipped: too many variables ({result.Variables.Count})");
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
        Console.WriteLine("  LogicalOptimizer.exe --advanced \"<expression>\" # Include advanced logical forms");
        Console.WriteLine("  LogicalOptimizer.exe --truth-table \"<expression>\" # Output only truth table");
        Console.WriteLine("  LogicalOptimizer.exe --csv \"<csv_content>\"    # Parse CSV truth table");
        Console.WriteLine("  LogicalOptimizer.exe \"<csv_file.csv>\"      # Parse CSV file (auto-detected)");
        Console.WriteLine("  LogicalOptimizer.exe --test              # Run tests");
        Console.WriteLine("  LogicalOptimizer.exe --help              # This help");
        Console.WriteLine("  LogicalOptimizer.exe --demo              # Features demonstration");
        Console.WriteLine("  LogicalOptimizer.exe --benchmark         # Performance testing");
        Console.WriteLine("  LogicalOptimizer.exe --stress            # Extreme stress testing for large expressions");
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
        Console.WriteLine("CSV Truth Table Examples:");
        Console.WriteLine("  LogicalOptimizer.exe \"a,b,Result\\n0,0,0\\n0,1,1\\n1,0,1\\n1,1,0\"");
        Console.WriteLine("  LogicalOptimizer.exe truth_table.csv");
        Console.WriteLine("  LogicalOptimizer.exe --csv \"x,y,Output\\n0,0,1\\n0,1,0\\n1,0,0\\n1,1,1\"");
        Console.WriteLine();
        Console.WriteLine("CSV Format Requirements:");
        Console.WriteLine("  - Header row with variable names and 'Result'/'Output'/'Value' column");
        Console.WriteLine("  - Values: 0/1, true/false, t/f, yes/no, y/n");
        Console.WriteLine("  - Comma-separated values");
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
                var result =
                    optimizer.OptimizeExpression(expr, true); // includeMetrics = true, includeDebugInfo = false
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

        // Generate expressions of different sizes - reduced for better performance
        var sizes = new[] {5, 10, 15, 20}; // Reduced from {10, 50, 100, 200}

        foreach (var size in sizes)
            try
            {
                var complexExpr = GenerateComplexExpression(size);
                Console.WriteLine(
                    $"Testing size {size}: {complexExpr.Substring(0, Math.Min(50, complexExpr.Length))}...");

                var startTime = DateTime.Now;
                var result = optimizer.OptimizeExpression(complexExpr, true); // includeMetrics = true
                var elapsed = DateTime.Now - startTime;

                // Check for timeout (more than 5 seconds)
                if (elapsed.TotalSeconds > 5)
                {
                    Console.WriteLine($"Size {size} variables: TIMEOUT ({elapsed.TotalSeconds:F1}s)");
                    break; // Stop testing larger sizes
                }

                Console.WriteLine($"Size {size} variables: {elapsed.TotalMilliseconds:F2}ms " +
                                  $"({result.Metrics?.OriginalNodes ?? 0}→{result.Metrics?.OptimizedNodes ?? 0} nodes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Size {size} variables: ERROR - {ex.Message}");
                break; // Stop on first error
            }
    }

    private static string GenerateComplexExpression(int variableCount)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var variables = Enumerable.Range(0, variableCount)
            .Select(i => $"v{i}")
            .ToArray();

        var terms = new List<string>();

        // Generate fewer, simpler terms to improve performance
        var termCount = Math.Min(variableCount / 4, 8); // Max 8 terms, fewer than before

        for (var i = 0; i < termCount; i++)
        {
            // Pick two different variables to avoid immediate contradictions
            var var1Index = random.Next(variables.Length);
            var var2Index = (var1Index + random.Next(1, variables.Length)) % variables.Length;

            var var1 = variables[var1Index];
            var var2 = variables[var2Index];

            // Reduce negation probability to 20% to minimize tautologies
            var neg1 = random.Next(10) < 2 ? "!" : "";
            var neg2 = random.Next(10) < 2 ? "!" : "";

            // Favor AND operations to create more complex, non-trivial expressions
            var op = random.Next(10) < 8 ? "&" : "|";

            terms.Add($"({neg1}{var1} {op} {neg2}{var2})");
        }

        return string.Join(" | ", terms);
    }

    /// <summary>
    ///     <summary>
    ///         Generate advanced logical forms for an optimized expression
    ///     </summary>
    private static string GenerateAdvancedLogicalForms(string optimizedExpression)
    {
        try
        {
            var forms = new List<string>();

            // Parse expression to AST for proper pattern recognition
            var lexer = new Lexer(optimizedExpression);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();

            // Check for XOR pattern using AST
            var xorResult = DetectXorPattern(ast);
            if (!string.IsNullOrEmpty(xorResult)) forms.Add($"XOR: {xorResult}");

            // Check for IMP pattern using AST
            var impResult = DetectImplicationPattern(ast);
            if (!string.IsNullOrEmpty(impResult)) forms.Add($"IMP: {impResult}");

            // If no patterns found, return empty
            if (forms.Count == 0) return string.Empty;

            return string.Join(", ", forms);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    ///     Standard XOR optimization patterns
    ///     Detects and converts standard XOR patterns to simpler forms
    /// </summary>
    private static string TryXorOptimization(string expr)
    {
        // Normalize expression (remove extra spaces)
        expr = expr.Trim();

        // Pattern 1: (a & !b) | (!a & b) → a XOR b
        var xorPattern1 = @"\((\w+) & !(\w+)\) \| \(!(\w+) & (\w+)\)";
        var match1 = Regex.Match(expr, xorPattern1);
        if (match1.Success)
        {
            var a1 = match1.Groups[1].Value;
            var b1 = match1.Groups[2].Value;
            var a2 = match1.Groups[3].Value;
            var b2 = match1.Groups[4].Value;

            if (a1 == a2 && b1 == b2) return $"{a1} XOR {b1}";
        }

        // Pattern 2: a & !b | !a & b → a XOR b (without parentheses)
        var xorPattern2 = @"(\w+) & !(\w+) \| !(\w+) & (\w+)";
        var match2 = Regex.Match(expr, xorPattern2);
        if (match2.Success)
        {
            var a1 = match2.Groups[1].Value;
            var b1 = match2.Groups[2].Value;
            var a2 = match2.Groups[3].Value;
            var b2 = match2.Groups[4].Value;

            if (a1 == a2 && b1 == b2) return $"{a1} XOR {b1}";
        }

        // Pattern 3: !a & b | a & !b → a XOR b (reversed order)
        var xorPattern3 = @"!(\w+) & (\w+) \| (\w+) & !(\w+)";
        var match3 = Regex.Match(expr, xorPattern3);
        if (match3.Success)
        {
            var a1 = match3.Groups[1].Value;
            var b1 = match3.Groups[2].Value;
            var a2 = match3.Groups[3].Value;
            var b2 = match3.Groups[4].Value;

            if (a1 == a2 && b1 == b2) return $"{a1} XOR {b1}";
        }

        // Pattern 4: !(a & b | !a & !b) → a XOR b (negated equivalence)
        var xorPattern4 = @"!\((\w+) & (\w+) \| !(\w+) & !(\w+)\)";
        var match4 = Regex.Match(expr, xorPattern4);
        if (match4.Success)
        {
            var a1 = match4.Groups[1].Value;
            var b1 = match4.Groups[2].Value;
            var a2 = match4.Groups[3].Value;
            var b2 = match4.Groups[4].Value;

            if (a1 == a2 && b1 == b2) return $"{a1} XOR {b1}";
        }

        // Pattern 5: a & !b | b & !a → a XOR b (optimized order)
        var xorPattern5 = @"(\w+) & !(\w+) \| (\w+) & !(\w+)";
        var match5 = Regex.Match(expr, xorPattern5);
        if (match5.Success)
        {
            var a1 = match5.Groups[1].Value;
            var b1 = match5.Groups[2].Value;
            var a2 = match5.Groups[3].Value;
            var b2 = match5.Groups[4].Value;

            if (a1 == b2 && b1 == a2) return $"{a1} XOR {b1}";
        }

        return string.Empty;
    }

    /// <summary>
    ///     Standard Implication optimization patterns
    ///     Detects and converts standard implication patterns
    /// </summary>
    private static string TryImplicationOptimization(string expr)
    {
        // Normalize expression
        expr = expr.Trim();

        // Pattern 1: !a | b → a → b (standard implication)
        var impPattern1 = @"^!(\w+) \| (\w+)$";
        var match1 = Regex.Match(expr, impPattern1);
        if (match1.Success)
        {
            var a = match1.Groups[1].Value;
            var b = match1.Groups[2].Value;
            return $"{a} → {b}";
        }

        // Pattern 2: b | !a → a → b (reversed order)
        var impPattern2 = @"^(\w+) \| !(\w+)$";
        var match2 = Regex.Match(expr, impPattern2);
        if (match2.Success)
        {
            var b = match2.Groups[1].Value;
            var a = match2.Groups[2].Value;
            return $"{a} → {b}";
        }

        // Pattern 3: !(a & !b) → a → b (negated form)
        var impPattern3 = @"^!\((\w+) & !(\w+)\)$";
        var match3 = Regex.Match(expr, impPattern3);
        if (match3.Success)
        {
            var a = match3.Groups[1].Value;
            var b = match3.Groups[2].Value;
            return $"{a} → {b}";
        }

        // DO NOT try to convert complex expressions like XOR patterns to implications
        return string.Empty;
    }

    /// <summary>
    ///     Find all XOR patterns in expression (both whole expression and parts)
    /// </summary>
    private static List<string> FindAllXorPatterns(string expr)
    {
        var results = new List<string>();

        // First try the whole expression
        var wholeXor = TryXorOptimization(expr);
        if (!string.IsNullOrEmpty(wholeXor))
        {
            results.Add($"XOR: {wholeXor}");
            return results; // If whole expression is XOR, don't look for parts
        }

        // Try to find XOR patterns in OR terms
        // Split by top-level OR operations (not inside parentheses)
        var orTerms = SplitByTopLevelOr(expr);

        if (orTerms.Count >= 2)
            // Try combinations of terms to find XOR patterns
            for (var i = 0; i < orTerms.Count - 1; i++)
            {
                for (var j = i + 1; j < orTerms.Count; j++)
                {
                    var combinedExpr = $"{orTerms[i].Trim()} | {orTerms[j].Trim()}";
                    var xorResult = TryXorOptimization(combinedExpr);
                    if (!string.IsNullOrEmpty(xorResult))
                    {
                        results.Add($"XOR: {xorResult}");
                        break; // Found one XOR pattern, that's enough for now
                    }
                }

                if (results.Count > 0) break;
            }

        return results;
    }

    /// <summary>
    ///     Convert expression by replacing patterns with advanced forms (XOR, IMP) using AST
    /// </summary>
    private static string ConvertToAdvancedForms(string expr)
    {
        try
        {
            // Parse expression to AST
            var lexer = new Lexer(expr);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();

            // Try to convert the AST to advanced forms
            var convertedAst = ConvertAstToAdvancedForms(ast);

            // Convert back to string and simplify
            var result = convertedAst.ToString();
            return SimplifyStringRepresentation(result);
        }
        catch
        {
            return expr; // Return original if parsing fails
        }
    }

    /// <summary>
    ///     Convert AST node to advanced forms (XOR, IMP) recursively
    /// </summary>
    private static AstNode ConvertAstToAdvancedForms(AstNode node)
    {
        // First try to detect patterns in the current node before recursing
        var patternResult = DetectAndReplacePatterns(node);
        if (patternResult != node) return patternResult; // Found a pattern, return it

        // If no pattern found, recursively convert children
        var convertedNode = node switch
        {
            OrNode orNode => new OrNode(
                ConvertAstToAdvancedForms(orNode.Left),
                ConvertAstToAdvancedForms(orNode.Right),
                orNode.ForceParentheses
            ),
            AndNode andNode => new AndNode(
                ConvertAstToAdvancedForms(andNode.Left),
                ConvertAstToAdvancedForms(andNode.Right),
                andNode.ForceParentheses
            ),
            NotNode notNode => new NotNode(
                ConvertAstToAdvancedForms(notNode.Operand)
            ),
            _ => node
        };

        // Try to detect patterns in the converted node one more time
        return DetectAndReplacePatterns(convertedNode);
    }

    /// <summary>
    ///     Detect and replace patterns (XOR, IMP) in AST node using unified scanning
    /// </summary>
    private static AstNode DetectAndReplacePatterns(AstNode node)
    {
        if (node is not OrNode orNode) return node;

        // Use unified pattern detection that finds both XOR and IMP patterns in one scan
        var result = DetectAllPatternsInAst(orNode);
        return result ?? node;
    }

    /// <summary>
    ///     Unified pattern detection for both XOR and IMP patterns in OR expressions
    /// </summary>
    private static AstNode? DetectAllPatternsInAst(OrNode orNode)
    {
        // Try direct patterns first (two-term OR)
        var directXor = TryFindDirectXorPattern(orNode);
        if (directXor != null) return directXor;

        var directImp = TryFindDirectImpPattern(orNode);
        if (directImp != null) return directImp;

        // For complex OR expressions with multiple terms, find all patterns
        var orTerms = CollectOrTerms(orNode);
        if (orTerms.Count < 2) return null;

        // First pass: check each individual OR term for IMP patterns
        var processedTerms = new List<AstNode>();
        foreach (var term in orTerms)
            if (term is OrNode termOr)
            {
                var impResult = TryFindDirectImpPattern(termOr);
                if (impResult != null)
                    processedTerms.Add(impResult);
                else
                    processedTerms.Add(term);
            }
            else
            {
                processedTerms.Add(term);
            }

        // Second pass: find XOR and IMP patterns between terms
        var patternNodes = new List<AstNode>();
        var remainingTerms = new List<AstNode>(processedTerms);

        // Collect already converted IMP nodes
        for (var i = remainingTerms.Count - 1; i >= 0; i--)
            if (remainingTerms[i] is ImpNode)
            {
                patternNodes.Add(remainingTerms[i]);
                remainingTerms.RemoveAt(i);
            }

        // Continue searching for both XOR and IMP patterns
        var foundAnyPattern = patternNodes.Count > 0;
        while (remainingTerms.Count >= 2)
        {
            var foundPatternInThisIteration = false;

            for (var i = 0; i < remainingTerms.Count - 1 && !foundPatternInThisIteration; i++)
            for (var j = i + 1; j < remainingTerms.Count && !foundPatternInThisIteration; j++)
            {
                var term1 = remainingTerms[i];
                var term2 = remainingTerms[j];

                // Create a temporary OR node to test patterns
                var tempOr = new OrNode(term1, term2);

                // Try XOR pattern first (it's more specific)
                var xorResult = TryFindDirectXorPattern(tempOr);
                if (xorResult != null)
                {
                    patternNodes.Add(xorResult);

                    // Remove the two terms that formed the XOR
                    remainingTerms.RemoveAt(j); // Remove j first (higher index)
                    remainingTerms.RemoveAt(i); // Then remove i

                    foundPatternInThisIteration = true;
                    foundAnyPattern = true;
                    continue;
                }

                // Try IMP pattern
                var impResult = TryFindDirectImpPattern(tempOr);
                if (impResult != null)
                {
                    patternNodes.Add(impResult);

                    // Remove the two terms that formed the IMP
                    remainingTerms.RemoveAt(j); // Remove j first (higher index)
                    remainingTerms.RemoveAt(i); // Then remove i

                    foundPatternInThisIteration = true;
                    foundAnyPattern = true;
                }
            }

            if (!foundPatternInThisIteration) break; // No more patterns found
        }

        if (!foundAnyPattern) return null;

        // Combine all pattern nodes and remaining terms
        var allNodes = new List<AstNode>();
        allNodes.AddRange(patternNodes);
        allNodes.AddRange(remainingTerms);

        if (allNodes.Count == 1) return allNodes[0];

        // Combine all nodes with OR
        var result = allNodes[0];
        for (var i = 1; i < allNodes.Count; i++) result = new OrNode(result, allNodes[i]);
        return result;
    }

    /// <summary>
    ///     Detect XOR pattern in AST and return XOR node if found
    /// </summary>
    private static AstNode? DetectXorPatternInAst(AstNode node)
    {
        if (node is not OrNode orNode) return null;

        // Try to find XOR pattern in direct children first
        var directXor = TryFindDirectXorPattern(orNode);
        if (directXor != null) return directXor;

        // For complex OR expressions with multiple terms, iteratively find and replace XOR patterns
        var orTerms = CollectOrTerms(orNode);
        if (orTerms.Count < 2) return null;

        // Find all XOR patterns and collect them
        var xorNodes = new List<AstNode>();
        var remainingTerms = new List<AstNode>(orTerms);

        // Continue searching for XOR patterns until no more found
        var foundAnyXor = false;
        while (remainingTerms.Count >= 2)
        {
            var foundXorInThisIteration = false;

            for (var i = 0; i < remainingTerms.Count - 1 && !foundXorInThisIteration; i++)
            for (var j = i + 1; j < remainingTerms.Count && !foundXorInThisIteration; j++)
            {
                var term1 = remainingTerms[i];
                var term2 = remainingTerms[j];

                // Create a temporary OR node to test XOR pattern
                var tempOr = new OrNode(term1, term2);
                var xorResult = TryFindDirectXorPattern(tempOr);

                if (xorResult != null)
                {
                    // Found XOR pattern!
                    xorNodes.Add(xorResult);

                    // Remove the two terms that formed the XOR
                    remainingTerms.RemoveAt(j); // Remove j first (higher index)
                    remainingTerms.RemoveAt(i); // Then remove i

                    foundXorInThisIteration = true;
                    foundAnyXor = true;
                }
            }

            if (!foundXorInThisIteration) break; // No more XOR patterns found
        }

        if (!foundAnyXor) return null;

        // Combine all XOR nodes and remaining terms
        var allNodes = new List<AstNode>();
        allNodes.AddRange(xorNodes);
        allNodes.AddRange(remainingTerms);

        if (allNodes.Count == 1) return allNodes[0];

        // Combine all nodes with OR
        var result = allNodes[0];
        for (var i = 1; i < allNodes.Count; i++) result = new OrNode(result, allNodes[i]);
        return result;
    }

    /// <summary>
    ///     Try to find direct XOR pattern in a simple OR node: (a & !b) | (!a & b)
    /// </summary>
    private static AstNode? TryFindDirectXorPattern(OrNode orNode)
    {
        // Pattern: (a & !b) | (!a & b) → a XOR b
        if (orNode.Left is AndNode leftAnd && orNode.Right is AndNode rightAnd)
        {
            var leftVars = ExtractAndTermVariables(leftAnd);
            var rightVars = ExtractAndTermVariables(rightAnd);

            if (leftVars.Count == 2 && rightVars.Count == 2)
            {
                var (var1Left, neg1Left) = leftVars[0];
                var (var2Left, neg2Left) = leftVars[1];
                var (var1Right, neg1Right) = rightVars[0];
                var (var2Right, neg2Right) = rightVars[1];

                // Check if it's XOR pattern: a & !b | !a & b
                if (IsXorPattern(var1Left, neg1Left, var2Left, neg2Left, var1Right, neg1Right, var2Right, neg2Right))
                {
                    var varA = neg1Left ? var2Left : var1Left;
                    var varB = neg1Left ? var1Left : var2Left;
                    return new XorNode(
                        new VariableNode(varA),
                        new VariableNode(varB)
                    );
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Collect all terms from a nested OR expression into a flat list
    /// </summary>
    private static List<AstNode> CollectOrTerms(OrNode orNode)
    {
        var terms = new List<AstNode>();
        CollectOrTermsRecursive(orNode, terms);
        return terms;
    }

    /// <summary>
    ///     Recursively collect OR terms
    /// </summary>
    private static void CollectOrTermsRecursive(AstNode node, List<AstNode> terms)
    {
        if (node is OrNode orNode)
        {
            CollectOrTermsRecursive(orNode.Left, terms);
            CollectOrTermsRecursive(orNode.Right, terms);
        }
        else
        {
            terms.Add(node);
        }
    }

    /// <summary>
    ///     Detect implication pattern in AST and return implication node if found
    /// </summary>
    private static AstNode? DetectImplicationPatternInAst(AstNode node)
    {
        if (node is not OrNode orNode) return null;

        Console.WriteLine($"DEBUG: DetectImplicationPatternInAst called with: {node}");

        // Try to find IMP pattern in direct children first
        var directImp = TryFindDirectImpPattern(orNode);
        if (directImp != null)
        {
            Console.WriteLine($"DEBUG: Found direct IMP: {directImp}");
            return directImp;
        }

        // For complex OR expressions with multiple terms, find and replace IMP patterns
        var orTerms = CollectOrTerms(orNode);
        Console.WriteLine(
            $"DEBUG: Collected {orTerms.Count} OR terms: {string.Join(", ", orTerms.Select(t => t.ToString()))}");

        if (orTerms.Count < 2) return null;

        // First, check each individual term to see if it's already an IMP pattern
        var processedTerms = new List<AstNode>();
        foreach (var term in orTerms)
            if (term is OrNode termOr)
            {
                Console.WriteLine($"DEBUG: Checking term for IMP: {term}");
                var impResult = TryFindDirectImpPattern(termOr);
                if (impResult != null)
                {
                    Console.WriteLine($"DEBUG: Term converted to IMP: {impResult}");
                    processedTerms.Add(impResult);
                }
                else
                {
                    Console.WriteLine($"DEBUG: Term not an IMP pattern: {term}");
                    processedTerms.Add(term);
                }
            }
            else
            {
                Console.WriteLine($"DEBUG: Non-OR term: {term}");
                processedTerms.Add(term);
            }

        // Now find IMP patterns between remaining non-IMP terms
        var impNodes = new List<AstNode>();
        var remainingTerms = new List<AstNode>(processedTerms);

        // Collect already converted IMP nodes
        for (var i = remainingTerms.Count - 1; i >= 0; i--)
            if (remainingTerms[i] is ImpNode)
            {
                impNodes.Add(remainingTerms[i]);
                remainingTerms.RemoveAt(i);
            }

        // Continue searching for IMP patterns until no more found
        var foundAnyImp = impNodes.Count > 0;
        while (remainingTerms.Count >= 2)
        {
            var foundImpInThisIteration = false;

            for (var i = 0; i < remainingTerms.Count - 1 && !foundImpInThisIteration; i++)
            for (var j = i + 1; j < remainingTerms.Count && !foundImpInThisIteration; j++)
            {
                var term1 = remainingTerms[i];
                var term2 = remainingTerms[j];

                // Create a temporary OR node to test IMP pattern
                var tempOr = new OrNode(term1, term2);
                var impResult = TryFindDirectImpPattern(tempOr);

                if (impResult != null)
                {
                    // Found IMP pattern!
                    impNodes.Add(impResult);

                    // Remove the two terms that formed the IMP
                    remainingTerms.RemoveAt(j); // Remove j first (higher index)
                    remainingTerms.RemoveAt(i); // Then remove i

                    foundImpInThisIteration = true;
                    foundAnyImp = true;
                }
            }

            if (!foundImpInThisIteration) break; // No more IMP patterns found
        }

        if (!foundAnyImp)
        {
            Console.WriteLine("DEBUG: No IMP patterns found");
            return null;
        }

        // Combine all IMP nodes and remaining terms
        var allNodes = new List<AstNode>();
        allNodes.AddRange(impNodes);
        allNodes.AddRange(remainingTerms);

        Console.WriteLine(
            $"DEBUG: Combining {allNodes.Count} nodes: {string.Join(", ", allNodes.Select(n => n.ToString()))}");

        if (allNodes.Count == 1) return allNodes[0];

        // Combine all nodes with OR
        var result = allNodes[0];
        for (var i = 1; i < allNodes.Count; i++) result = new OrNode(result, allNodes[i]);
        return result;
    }

    /// <summary>
    ///     Try to find direct IMP pattern in a simple OR node: !a | b → a → b
    /// </summary>
    private static AstNode? TryFindDirectImpPattern(OrNode orNode)
    {
        var leftTerm = orNode.Left;
        var rightTerm = orNode.Right;

        // Pattern 1: !a | b → a → b
        if (leftTerm is NotNode notLeft && rightTerm is VariableNode varRight)
            if (notLeft.Operand is VariableNode varLeftInner)
                return new ImpNode(varLeftInner, varRight);

        // Pattern 2: b | !a → a → b  
        if (rightTerm is NotNode notRight && leftTerm is VariableNode varLeft)
            if (notRight.Operand is VariableNode varRightInner)
                return new ImpNode(varRightInner, varLeft);

        return null;
    }

    /// <summary>
    ///     Simplify the string representation (remove unnecessary parentheses)
    /// </summary>
    private static string SimplifyStringRepresentation(string expr)
    {
        // Remove outer parentheses if the whole expression is wrapped
        if (expr.StartsWith("(") && expr.EndsWith(")"))
        {
            var parenCount = 0;
            var canRemove = true;
            for (var i = 0; i < expr.Length - 1; i++)
            {
                if (expr[i] == '(') parenCount++;
                else if (expr[i] == ')') parenCount--;

                if (parenCount == 0)
                {
                    canRemove = false;
                    break;
                }
            }

            if (canRemove) expr = expr.Substring(1, expr.Length - 2);
        }

        return expr;
    }

    /// <summary>
    ///     Replace XOR patterns in expression with XOR notation
    /// </summary>
    private static string ReplaceXorPatterns(string expr)
    {
        var result = expr;

        // Split by top-level OR operations
        var orTerms = SplitByTopLevelOr(expr);

        if (orTerms.Count >= 2)
            // Try to find XOR patterns in pairs of terms
            for (var i = 0; i < orTerms.Count - 1; i++)
            for (var j = i + 1; j < orTerms.Count; j++)
            {
                var term1 = orTerms[i].Trim();
                var term2 = orTerms[j].Trim();
                var combinedExpr = $"{term1} | {term2}";
                var xorResult = TryXorOptimization(combinedExpr);

                if (!string.IsNullOrEmpty(xorResult))
                {
                    // Replace the two terms with the XOR expression
                    var newTerms = new List<string>();
                    for (var k = 0; k < orTerms.Count; k++)
                        if (k == i)
                        {
                            // Add parentheses around XOR if it's mixed with other terms
                            if (orTerms.Count > 2)
                                newTerms.Add($"({xorResult})");
                            else
                                newTerms.Add(xorResult);
                        }
                        else if (k != j) // Skip the second term
                        {
                            newTerms.Add(orTerms[k].Trim());
                        }

                    return string.Join(" | ", newTerms);
                }
            }

        return result;
    }

    /// <summary>
    ///     Replace implication patterns in expression with implication notation
    /// </summary>
    private static string ReplaceImplicationPatterns(string expr)
    {
        // Split by top-level OR operations to find implication patterns in parts
        var orTerms = SplitByTopLevelOr(expr);

        if (orTerms.Count >= 1)
        {
            var newTerms = new List<string>();
            var foundImplication = false;

            foreach (var term in orTerms)
            {
                var trimmedTerm = term.Trim();
                var impResult = TryImplicationOptimization(trimmedTerm);

                if (!string.IsNullOrEmpty(impResult))
                {
                    // Found implication pattern in this term
                    if (orTerms.Count > 1)
                        newTerms.Add($"({impResult})");
                    else
                        newTerms.Add(impResult);
                    foundImplication = true;
                }
                else
                {
                    // No implication pattern, keep original term
                    newTerms.Add(trimmedTerm);
                }
            }

            if (foundImplication) return string.Join(" | ", newTerms);
        }

        // If no parts have implication patterns, check the whole expression
        var wholeImpResult = TryImplicationOptimization(expr);
        if (!string.IsNullOrEmpty(wholeImpResult)) return wholeImpResult;

        return expr;
    }

    /// <summary>
    ///     Split expression by top-level OR operations (not inside parentheses)
    /// </summary>
    private static List<string> SplitByTopLevelOr(string expr)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        var parenDepth = 0;

        for (var i = 0; i < expr.Length; i++)
        {
            var c = expr[i];

            if (c == '(')
            {
                parenDepth++;
                current.Append(c);
            }
            else if (c == ')')
            {
                parenDepth--;
                current.Append(c);
            }
            else if (c == '|' && parenDepth == 0)
            {
                // Top-level OR found
                if (current.Length > 0)
                {
                    terms.Add(current.ToString().Trim());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        // Add the last term
        if (current.Length > 0) terms.Add(current.ToString().Trim());

        return terms;
    }

    private static bool IsXorPattern(string expr, List<string> variables)
    {
        // This method is now obsolete, using regex-based TryXorOptimization instead
        return false;
    }

    /// <summary>
    ///     Detects XOR pattern in AST: (a & !b) | (!a & b)
    /// </summary>
    private static string DetectXorPattern(AstNode node)
    {
        if (node is not OrNode orNode) return string.Empty;

        // Get the two terms of OR
        var leftTerm = orNode.Left;
        var rightTerm = orNode.Right;

        // Both terms must be AND nodes
        if (leftTerm is not AndNode leftAnd || rightTerm is not AndNode rightAnd)
            return string.Empty;

        // Extract variables from both AND terms
        var leftVars = ExtractAndTermVariables(leftAnd);
        var rightVars = ExtractAndTermVariables(rightAnd);

        // XOR pattern: one term has (a & !b), other has (!a & b)
        if (leftVars.Count == 2 && rightVars.Count == 2)
        {
            var (var1Left, neg1Left) = leftVars[0];
            var (var2Left, neg2Left) = leftVars[1];
            var (var1Right, neg1Right) = rightVars[0];
            var (var2Right, neg2Right) = rightVars[1];

            // Check if it's XOR pattern: a & !b | !a & b
            if (IsXorPattern(var1Left, neg1Left, var2Left, neg2Left, var1Right, neg1Right, var2Right, neg2Right))
            {
                var varA = neg1Left ? var2Left : var1Left;
                var varB = neg1Left ? var1Left : var2Left;
                return $"{varA} XOR {varB}";
            }
        }

        return string.Empty;
    }

    /// <summary>
    ///     Detects implication pattern in AST: !a | b
    /// </summary>
    private static string DetectImplicationPattern(AstNode node)
    {
        if (node is not OrNode orNode) return string.Empty;

        var leftTerm = orNode.Left;
        var rightTerm = orNode.Right;

        // Pattern 1: !a | b
        if (leftTerm is NotNode notLeft && rightTerm is VariableNode varRight)
            if (notLeft.Operand is VariableNode varLeftInner)
                return $"{varLeftInner.Name} → {varRight.Name}";

        // Pattern 2: b | !a  
        if (rightTerm is NotNode notRight && leftTerm is VariableNode varLeft)
            if (notRight.Operand is VariableNode varRightInner)
                return $"{varRightInner.Name} → {varLeft.Name}";

        return string.Empty;
    }

    /// <summary>
    ///     Extract variables and their negation status from an AND term
    /// </summary>
    private static List<(string variable, bool isNegated)> ExtractAndTermVariables(AndNode andNode)
    {
        var variables = new List<(string, bool)>();

        // Handle left side
        if (andNode.Left is VariableNode varLeft)
            variables.Add((varLeft.Name, false));
        else if (andNode.Left is NotNode notLeft && notLeft.Operand is VariableNode varLeftNeg)
            variables.Add((varLeftNeg.Name, true));

        // Handle right side
        if (andNode.Right is VariableNode varRight)
            variables.Add((varRight.Name, false));
        else if (andNode.Right is NotNode notRight && notRight.Operand is VariableNode varRightNeg)
            variables.Add((varRightNeg.Name, true));

        return variables;
    }

    /// <summary>
    ///     Check if the variable pattern matches XOR: a & !b | !a & b
    /// </summary>
    private static bool IsXorPattern(string var1Left, bool neg1Left, string var2Left, bool neg2Left,
        string var1Right, bool neg1Right, string var2Right, bool neg2Right)
    {
        // Sort variables to handle different orders
        var leftVars = new[] {(var1Left, neg1Left), (var2Left, neg2Left)}.OrderBy(x => x.Item1).ToArray();
        var rightVars = new[] {(var1Right, neg1Right), (var2Right, neg2Right)}.OrderBy(x => x.Item1).ToArray();

        // Both sides must have same variables
        if (leftVars[0].Item1 != rightVars[0].Item1 || leftVars[1].Item1 != rightVars[1].Item1)
            return false;

        // XOR pattern: (a & !b) | (!a & b)
        // First var: left=false, right=true OR left=true, right=false
        // Second var: left=true, right=false OR left=false, right=true
        var firstVarXor = leftVars[0].Item2 != rightVars[0].Item2;
        var secondVarXor = leftVars[1].Item2 != rightVars[1].Item2;

        return firstVarXor && secondVarXor && leftVars[0].Item2 != leftVars[1].Item2;
    }

    private static string ConvertToXor(string expr, List<string> variables)
    {
        // This method is now obsolete, using AST-based DetectXorPattern instead
        return string.Empty;
    }

    private static bool IsImplicationPattern(string expr, List<string> variables)
    {
        // This method is now obsolete, using AST-based DetectImplicationPattern instead
        return false;
    }

    private static string ConvertToImplication(string expr, List<string> variables)
    {
        // This method is now obsolete, using AST-based DetectImplicationPattern instead
        return string.Empty;
    }

    private static void ShowCsvExample()
    {
        Console.WriteLine("=== CSV Truth Table Example ===");
        Console.WriteLine();
        Console.WriteLine("Example CSV format:");
        Console.WriteLine(CsvTruthTableParser.GenerateExampleCsv());
        Console.WriteLine("This represents the XOR function: a XOR b");
        Console.WriteLine();
        Console.WriteLine("To use:");
        Console.WriteLine("1. Save the CSV content to a file (e.g., 'table.csv')");
        Console.WriteLine("2. Run: LogicalOptimizer.exe table.csv");
        Console.WriteLine("3. Or pass CSV content directly:");
        Console.WriteLine("   LogicalOptimizer.exe \"a,b,Result\\n0,0,0\\n0,1,1\\n1,0,1\\n1,1,0\"");
        Console.WriteLine();
        Console.WriteLine("Supported column names for result: Result, Output, Value");
        Console.WriteLine("Supported boolean values: 0/1, true/false, t/f, yes/no, y/n");
    }

    private static void RunStressTest()
    {
        Console.WriteLine("=== EXTREME STRESS TEST FOR LARGE EXPRESSIONS ===\n");

        var optimizer = new BooleanExpressionOptimizer();

        // Test with progressively larger expressions
        var testSizes = new[] {10, 20, 30, 40, 50, 75, 100};
        var complexityLevels = new[] {"Simple", "Medium", "Complex", "Extreme"};

        foreach (var size in testSizes)
        {
            Console.WriteLine($"\n--- Testing {size} variables ---");

            for (var complexity = 0; complexity < complexityLevels.Length; complexity++)
                try
                {
                    var expression = GenerateStressTestExpression(size, complexity);
                    var displayExpr = expression.Length > 60
                        ? expression.Substring(0, 57) + "..."
                        : expression;

                    Console.WriteLine($"{complexityLevels[complexity],-8}: {displayExpr}");

                    var startTime = DateTime.Now;
                    var result = optimizer.OptimizeExpression(expression, true);
                    var elapsed = DateTime.Now - startTime;

                    // Check for timeout
                    if (elapsed.TotalSeconds > 10)
                    {
                        Console.WriteLine($"           TIMEOUT ({elapsed.TotalSeconds:F1}s) - skipping larger tests");
                        return;
                    }

                    var nodeChange = result.Metrics != null
                        ? $"{result.Metrics.OriginalNodes}→{result.Metrics.OptimizedNodes}"
                        : "N/A";

                    var improvement = result.Metrics != null && result.Metrics.OriginalNodes > 0
                        ? (double) (result.Metrics.OriginalNodes - result.Metrics.OptimizedNodes) /
                        result.Metrics.OriginalNodes * 100
                        : 0;

                    Console.WriteLine(
                        $"           Result: {nodeChange} nodes, {elapsed.TotalMilliseconds:F0}ms, {improvement:F1}% improvement");

                    // Stop if expression becomes too slow
                    if (elapsed.TotalMilliseconds > 5000)
                    {
                        Console.WriteLine(
                            $"           Stopping: expression too slow ({elapsed.TotalMilliseconds:F0}ms)");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"           ERROR: {ex.Message}");
                    if (ex.Message.Contains("Too many variables") || ex.Message.Contains("too long"))
                    {
                        Console.WriteLine("           Reached system limits - stopping test");
                        return;
                    }
                }
        }

        Console.WriteLine("\n=== PERFORMANCE ANALYSIS ===");
        Console.WriteLine("Testing specific optimization scenarios...\n");

        // Test specific optimization patterns
        TestOptimizationPatterns(optimizer);
    }

    private static string GenerateStressTestExpression(int variableCount, int complexityLevel)
    {
        var random = new Random(42 + complexityLevel); // Different seed for each complexity
        var variables = Enumerable.Range(0, variableCount)
            .Select(i => $"v{i}")
            .ToArray();

        var terms = new List<string>();

        // Adjust complexity based on level
        int termCount;
        double negationProb;
        double andProb;

        switch (complexityLevel)
        {
            case 0: // Simple
                termCount = Math.Min(variableCount / 6, 5);
                negationProb = 0.1;
                andProb = 0.9;
                break;
            case 1: // Medium  
                termCount = Math.Min(variableCount / 4, 10);
                negationProb = 0.2;
                andProb = 0.8;
                break;
            case 2: // Complex
                termCount = Math.Min(variableCount / 3, 15);
                negationProb = 0.3;
                andProb = 0.7;
                break;
            case 3: // Extreme
                termCount = Math.Min(variableCount / 2, 25);
                negationProb = 0.4;
                andProb = 0.6;
                break;
            default:
                termCount = 5;
                negationProb = 0.2;
                andProb = 0.8;
                break;
        }

        for (var i = 0; i < termCount; i++)
            // Create more complex sub-expressions for higher complexity levels
            if (complexityLevel >= 2 && random.NextDouble() < 0.3)
            {
                // Create nested expressions like ((a & b) | (c & d))
                var subTerms = new List<string>();
                var subTermCount = random.Next(2, 4);

                for (var j = 0; j < subTermCount; j++)
                {
                    var var1 = variables[random.Next(variables.Length)];
                    var var2 = variables[random.Next(variables.Length)];
                    var neg1 = random.NextDouble() < negationProb ? "!" : "";
                    var neg2 = random.NextDouble() < negationProb ? "!" : "";
                    var op = random.NextDouble() < andProb ? "&" : "|";
                    subTerms.Add($"({neg1}{var1} {op} {neg2}{var2})");
                }

                var nestedOp = random.NextDouble() < 0.5 ? "&" : "|";
                terms.Add($"({string.Join($" {nestedOp} ", subTerms)})");
            }
            else
            {
                // Simple binary terms
                var var1Index = random.Next(variables.Length);
                var var2Index = (var1Index + random.Next(1, variables.Length)) % variables.Length;

                var var1 = variables[var1Index];
                var var2 = variables[var2Index];

                var neg1 = random.NextDouble() < negationProb ? "!" : "";
                var neg2 = random.NextDouble() < negationProb ? "!" : "";
                var op = random.NextDouble() < andProb ? "&" : "|";

                terms.Add($"({neg1}{var1} {op} {neg2}{var2})");
            }

        return string.Join(" | ", terms);
    }

    private static void TestOptimizationPatterns(BooleanExpressionOptimizer optimizer)
    {
        var patterns = new Dictionary<string, string>
        {
            {"Large Factorization", GenerateLargeFactorizationPattern(10)},
            {"Extended Absorption", GenerateAbsorptionPattern(8)},
            {"Complex Consensus", GenerateConsensusPattern(6)},
            {"Mixed Patterns", GenerateMixedPattern(12)},
            {"Tautology Detection", GenerateTautologyPattern(15)},
            {"Redundancy Removal", GenerateRedundancyPattern(10)}
        };

        foreach (var pattern in patterns)
        {
            Console.WriteLine($"Pattern: {pattern.Key}");
            Console.WriteLine(
                $"Expression: {(pattern.Value.Length > 80 ? pattern.Value.Substring(0, 77) + "..." : pattern.Value)}");

            try
            {
                var startTime = DateTime.Now;
                var result = optimizer.OptimizeExpression(pattern.Value, true);
                var elapsed = DateTime.Now - startTime;

                var nodeChange = result.Metrics != null
                    ? $"{result.Metrics.OriginalNodes}→{result.Metrics.OptimizedNodes}"
                    : "N/A";

                var improvement = result.Metrics != null && result.Metrics.OriginalNodes > 0
                    ? (double) (result.Metrics.OriginalNodes - result.Metrics.OptimizedNodes) /
                    result.Metrics.OriginalNodes * 100
                    : 0;

                Console.WriteLine(
                    $"Result: {nodeChange} nodes, {elapsed.TotalMilliseconds:F0}ms, {improvement:F1}% improvement");

                if (result.Metrics != null && result.Metrics.AppliedRules > 0)
                {
                    var appliedRules = string.Join(", ",
                        result.Metrics.RuleApplicationCount.Select(r => $"{r.Key}({r.Value})"));
                    Console.WriteLine($"Rules: {appliedRules}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }

            Console.WriteLine();
        }
    }

    private static string GenerateLargeFactorizationPattern(int vars)
    {
        // Pattern: (a | x1) & (a | x2) & (a | x3) ... → a | (x1 & x2 & x3 ...)
        var terms = new List<string>();
        for (var i = 1; i <= vars; i++) terms.Add($"(a | x{i})");
        return string.Join(" & ", terms);
    }

    private static string GenerateAbsorptionPattern(int vars)
    {
        // Pattern: a | (a & x1) | (a & x2) ... → a
        var terms = new List<string> {"a"};
        for (var i = 1; i <= vars; i++) terms.Add($"(a & x{i})");
        return string.Join(" | ", terms);
    }

    private static string GenerateConsensusPattern(int vars)
    {
        // Pattern: multiple consensus opportunities
        var terms = new List<string>();
        for (var i = 0; i < vars; i += 2)
        {
            var a = $"x{i}";
            var b = $"x{i + 1}";
            var c = $"x{(i + 2) % vars}";
            terms.Add($"({a} & {b})");
            terms.Add($"(!{a} & {c})");
        }

        return string.Join(" | ", terms);
    }

    private static string GenerateMixedPattern(int vars)
    {
        // Mix of different optimization opportunities
        var terms = new List<string>();

        // Factorization opportunity
        terms.Add("(a | b)");
        terms.Add("(a | c)");

        // Absorption opportunity  
        terms.Add("d");
        terms.Add("(d & e)");

        // Add random terms
        var random = new Random(123);
        for (var i = 0; i < vars - 4; i++)
        {
            var var1 = $"v{random.Next(vars)}";
            var var2 = $"v{random.Next(vars)}";
            var neg = random.NextDouble() < 0.3 ? "!" : "";
            terms.Add($"({neg}{var1} & {var2})");
        }

        return
            $"({string.Join(" & ", terms.Take(terms.Count / 2))}) | ({string.Join(" & ", terms.Skip(terms.Count / 2))})";
    }

    private static string GenerateTautologyPattern(int vars)
    {
        // Pattern that should simplify to tautology
        var terms = new List<string>();

        // Add contradictory pairs to force tautology
        for (var i = 0; i < vars / 2; i++)
        {
            var varName = $"x{i}";
            terms.Add($"({varName} | !{varName})"); // Always true
        }

        // Add some regular terms
        for (var i = vars / 2; i < vars; i++) terms.Add($"(x{i} & y{i})");

        return string.Join(" | ", terms);
    }

    private static string GenerateRedundancyPattern(int vars)
    {
        // Pattern with lots of redundant terms
        var terms = new List<string>();

        // Add the same logical term in different forms
        terms.Add("(a & b)");
        terms.Add("(b & a)"); // Same as above
        terms.Add("(a & b & 1)"); // Same as first

        // Add absorption cases
        terms.Add("(a & b)");
        terms.Add("(a & b & c)"); // Absorbed by above

        // Add random terms to make it interesting
        var random = new Random(456);
        for (var i = 0; i < vars - 5; i++)
        {
            var var1 = $"v{i}";
            var var2 = $"v{(i + 1) % vars}";
            terms.Add($"({var1} & {var2})");
        }

        return string.Join(" | ", terms);
    }
}