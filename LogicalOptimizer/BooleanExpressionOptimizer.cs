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
            var cnf = converter.ConvertToCNF(optimized.Clone());
            var dnf = converter.ConvertToDNF(optimized.Clone());

            // Apply pattern recognition and replacement only for display
            var patternRecognizer = new PatternRecognizer();
            var optimizedWithPatterns = patternRecognizer.ReplacePatterns(optimized.Clone());
            
            // Generate advanced logical forms
            var advancedForms = patternRecognizer.GenerateAdvancedLogicalForms(optimized.ToString());
            
            var result = new OptimizationResult
            {
                Original = expression,
                Optimized = optimized.ToString(),
                CNF = cnf.ToString(),
                DNF = dnf.ToString(),
                Advanced = advancedForms.StartsWith("Optimized:") ? "" : advancedForms,
                Variables = ast.GetVariables().OrderBy(v => v).ToList(),
                Metrics = metrics,
                OriginalTruthTable = includeMetrics ? TruthTable.Generate(ast) : null,
                OptimizedTruthTable = includeMetrics ? TruthTable.Generate(optimized) : null,
                CompiledOriginalTruthTable = includeMetrics ? CompiledTruthTable.Generate(ast, expression) : null,
                CompiledOptimizedTruthTable = includeMetrics ? CompiledTruthTable.Generate(optimized, optimized.ToString()) : null
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