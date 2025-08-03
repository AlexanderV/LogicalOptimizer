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

            var result = new OptimizationResult
            {
                Original = expression,
                Optimized = optimized.ToString(),
                CNF = cnf.ToString(),
                DNF = dnf.ToString(),
                Variables = ast.GetVariables().OrderBy(v => v).ToList(),
                Metrics = metrics,
                OriginalTruthTable = includeMetrics ? TruthTable.Generate(ast) : null,
                OptimizedTruthTable = includeMetrics ? TruthTable.Generate(optimized) : null
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