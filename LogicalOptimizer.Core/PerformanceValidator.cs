namespace LogicalOptimizer;

/// <summary>
///     Performance constraint validator according to specification
/// </summary>
internal static class PerformanceValidator
{
    public const int MAX_EXPRESSION_LENGTH = 10000;
    public const int MAX_VARIABLES = 100;
    public const int MAX_PARENTHESES_DEPTH = 50;
    public const int MAX_OPTIMIZATION_ITERATIONS = 20; // Reduced from 50 for better performance
    public const int MAX_PROCESSING_TIME_SECONDS = 10; // Reduced from 30 for better responsiveness
    // Pattern recognition is a linear syntactic pass; the cap matches the overall
    // input limit so the facade's Advanced output agrees with the CLI's detector
    // path (the old cap of 5 silently returned "" for 6+ variables while the CLI
    // still displayed patterns — an audited inconsistency)
    public const int MAX_PATTERN_RECOGNITION_VARIABLES = MAX_VARIABLES;
    public const int MAX_TRUTH_TABLE_VARIABLES = 6;

    /// <summary>Up to this many variables every optimization is verified against the input's truth table.</summary>
    public const int MAX_EQUIVALENCE_CHECK_VARIABLES = 12;

    /// <summary>
    ///     Conflict budget for the SAT-based soundness guard applied beyond the truth-table
    ///     range. Rewrite/input miters are structurally close, so genuine bugs refute fast;
    ///     the budget only caps pathological UNSAT proofs.
    /// </summary>
    public const int SOUNDNESS_GUARD_MAX_CONFLICTS = 20_000;

    /// <summary>
    ///     Up to this many variables the exact Quine–McCluskey minimizer is applied.
    ///     Between EXACT_GUARANTEE_VARIABLES and this gate the attempt is budgeted
    ///     (dense functions fall back to heuristic simplification); the cyclic-core
    ///     search is additionally capped by the branch-and-bound step limit.
    /// </summary>
    public const int MAX_EXACT_MINIMIZATION_VARIABLES = 12;

    /// <summary>
    ///     Up to this many variables exact minimization runs without a prime-generation
    ///     budget. Minimality is proven whenever the cover search completes; any budget
    ///     exhaustion is reported as MinimizationStatus.BudgetExceeded, never silently.
    /// </summary>
    public const int EXACT_GUARANTEE_VARIABLES = 10;

    /// <summary>
    ///     Branch-and-bound step limit for the minimum-cover search in the guarantee zone
    ///     (≤ EXACT_GUARANTEE_VARIABLES). Set to the maximum so the search is effectively
    ///     unbounded there: the state space of ≤10 variables is finite, so branch-and-bound
    ///     with covering-table reductions and lower-bound pruning always terminates and the
    ///     result is provably minimal — matching the documented guarantee. (Real cyclic cores
    ///     resolve in thousands of steps; the ceiling only guards against integer overflow of
    ///     the step counter, never against a legitimate proof completing.)
    /// </summary>
    public const int GUARANTEE_COVER_STEP_LIMIT = int.MaxValue;

    /// <summary>
    ///     Work budget (cube pair comparisons) for Quine–McCluskey prime generation inside
    ///     OptimizeExpression. Dense functions at 11-12 variables can otherwise cost seconds;
    ///     exceeding the budget falls back to heuristic simplification for that expression.
    /// </summary>
    public const long QM_PAIR_COMPARISON_LIMIT = 5_000_000;

    /// <summary>
    ///     Above the exact gate and up to this many variables, a SAT-based prime-cover
    ///     minimization is attempted (no 2^n table involved). Result is heuristic and is
    ///     accepted only after a SAT-miter equivalence proof.
    /// </summary>
    public const int MAX_SAT_MINIMIZATION_VARIABLES = 24;

    /// <summary>Cube budget for the SAT-based prime cover.</summary>
    public const int SAT_MINIMIZATION_CUBE_LIMIT = 256;

    /// <summary>Conflict budget per SAT query inside the prime-cover search.</summary>
    public const int SAT_MINIMIZATION_QUERY_CONFLICTS = 20_000;

    /// <summary>
    ///     The factoring pipeline is re-run on the minimal SOP only below this node count:
    ///     its absorption/factorization passes are quadratic, and dense functions near the
    ///     exact threshold can produce SOPs with hundreds of terms.
    /// </summary>
    public const int MAX_FACTORED_MIN_SOP_NODES = 300;

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
