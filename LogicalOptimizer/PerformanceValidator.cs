namespace LogicalOptimizer;

/// <summary>
///     Performance constraint validator according to specification
/// </summary>
public static class PerformanceValidator
{
    public const int MAX_EXPRESSION_LENGTH = 10000;
    public const int MAX_VARIABLES = 100;
    public const int MAX_PARENTHESES_DEPTH = 50;
    public const int MAX_OPTIMIZATION_ITERATIONS = 50;
    public const int MAX_PROCESSING_TIME_SECONDS = 30;

    /// <summary>
    ///     Validates expression for compliance with performance constraints
    /// </summary>
    public static void ValidateExpression(string expression)
    {
        if (string.IsNullOrEmpty(expression))
            throw new ArgumentException("Expression cannot be empty");

        // Check expression length
        if (expression.Length > MAX_EXPRESSION_LENGTH)
            throw new ArgumentException(
                $"Expression too long. Maximum {MAX_EXPRESSION_LENGTH} characters, got {expression.Length}");

        // Check parentheses nesting depth
        ValidateParenthesesDepth(expression);
    }

    /// <summary>
    ///     Validates AST for compliance with constraints
    /// </summary>
    public static void ValidateAst(AstNode ast)
    {
        // Check number of variables
        var variables = ast.GetVariables();
        if (variables.Count > MAX_VARIABLES)
            throw new ArgumentException($"Too many variables. Maximum {MAX_VARIABLES}, found {variables.Count}");
    }

    /// <summary>
    ///     Checks the number of optimization iterations
    /// </summary>
    public static void ValidateIterations(int iterations)
    {
        if (iterations > MAX_OPTIMIZATION_ITERATIONS)
            throw new InvalidOperationException(
                $"Maximum number of optimization iterations exceeded ({MAX_OPTIMIZATION_ITERATIONS}). Possible infinite loop.");
    }

    /// <summary>
    ///     Checks execution time
    /// </summary>
    public static void ValidateProcessingTime(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds > MAX_PROCESSING_TIME_SECONDS)
            throw new TimeoutException(
                $"Maximum processing time exceeded ({MAX_PROCESSING_TIME_SECONDS} sec). Processing aborted.");
    }

    private static void ValidateParenthesesDepth(string expression)
    {
        var depth = 0;
        var maxDepth = 0;

        foreach (var c in expression)
            if (c == '(')
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);

                if (maxDepth > MAX_PARENTHESES_DEPTH)
                    throw new ArgumentException(
                        $"Too deep nesting of parentheses. Maximum {MAX_PARENTHESES_DEPTH} levels, found {maxDepth}");
            }
            else if (c == ')')
            {
                depth--;
            }

        if (depth != 0)
            throw new ArgumentException("Unbalanced parentheses in expression");
    }
}