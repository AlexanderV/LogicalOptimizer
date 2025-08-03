namespace LogicalOptimizer;

public class BooleanExpressionOptimizer
{
    public OptimizationResult OptimizeExpression(string expression, bool includeMetrics = false,
        bool includeDebugInfo = false)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be empty", nameof(expression));

        // Performance constraint validation
        PerformanceValidator.ValidateExpression(expression);

        try
        {
            var lexer = new Lexer(expression);
            var tokens = lexer.Tokenize();

            var parser = new Parser(tokens);
            var ast = parser.Parse();

            var metrics = includeMetrics ? new OptimizationMetrics() : null;
            var optimizer = new ExpressionOptimizer();
            var optimized = optimizer.Optimize(ast, metrics);

            var converter = new NormalFormConverter();
            // Avoid unnecessary cloning - use original optimized node directly
            var cnf = converter.ConvertToCNF(optimized);
            var dnf = converter.ConvertToDNF(optimized);

            // Apply pattern recognition only if needed (for small expressions or when requested)
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            var advancedForms = "";

            if (variables.Count <= 5) // Only for small expressions to avoid performance issues
            {
                var patternRecognizer = new PatternRecognizer();
                advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(optimized.ToString());
            }

            var result = new OptimizationResult
            {
                Original = expression,
                Optimized = optimized.ToString(),
                CNF = cnf.ToString(),
                DNF = dnf.ToString(),
                Advanced = advancedForms.StartsWith("Optimized:") ? "" : advancedForms,
                Variables = variables,
                Metrics = metrics,
                // Only generate truth tables for small expressions (≤6 variables) to avoid performance issues
                OriginalTruthTable = includeMetrics && variables.Count <= 6 ? TruthTable.Generate(ast) : null,
                OptimizedTruthTable = includeMetrics && variables.Count <= 6 ? TruthTable.Generate(optimized) : null,
                CompiledOriginalTruthTable = includeMetrics && variables.Count <= 6
                    ? CompiledTruthTable.Generate(ast, expression)
                    : null,
                CompiledOptimizedTruthTable = includeMetrics && variables.Count <= 6
                    ? CompiledTruthTable.Generate(optimized, optimized.ToString())
                    : null
            };

            if (includeDebugInfo && metrics != null)
            {
                Console.WriteLine("\n=== Debug Information ===");
                Console.WriteLine($"Original AST:\n{AstVisualizer.VisualizeTree(ast)}");
                Console.WriteLine($"Optimized AST:\n{AstVisualizer.VisualizeTree(optimized)}");
                Console.WriteLine(metrics.ToString());
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Error processing expression '{expression}': {ex.Message}", ex);
        }
    }
}