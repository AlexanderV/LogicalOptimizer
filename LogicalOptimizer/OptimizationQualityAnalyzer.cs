using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Boolean expression optimization quality analyzer
/// </summary>
public class OptimizationQualityAnalyzer
{
    /// <summary>
    ///     Analyzes optimization quality
    /// </summary>
    public static QualityMetrics AnalyzeOptimization(OptimizationResult result)
    {
        var metrics = new QualityMetrics();

        // Parse original and optimized expressions
        var originalAst = ParseExpression(result.Original);
        var optimizedAst = ParseExpression(result.Optimized);

        // Basic metrics
        var originalStats = CalculateExpressionStats(originalAst);
        var optimizedStats = CalculateExpressionStats(optimizedAst);

        metrics.CompressionRatio = (double)optimizedStats.NodeCount / originalStats.NodeCount;
        metrics.LiteralCount = optimizedStats.LiteralCount;
        metrics.OperatorCount = optimizedStats.OperatorCount;
        metrics.MaxDepth = optimizedStats.MaxDepth;

        // Complexity (combined metric)
        metrics.Complexity = CalculateComplexity(optimizedStats);

        // Analysis of applied optimizations
        if (result.Metrics != null) metrics.AppliedOptimizations = result.Metrics.RuleApplicationCount.Keys.ToList();

        // Heuristic quality score (0-100): a weighted rating of the transformation, NOT a
        // proof of optimality.
        metrics.OptimalityScore = CalculateOptimalityScore(originalStats, optimizedStats, metrics.AppliedOptimizations);

        // IsOptimal is a real, provable property, not a score threshold: it is true exactly
        // when the exact minimizer proved the two-level minimum (MinimizationStatus.
        // MinimalProven), under the documented (total literals, then term count) cost model.
        // A high heuristic score alone never makes a result "optimal".
        metrics.IsOptimal = result.MinimizationStatus == MinimizationStatus.MinimalProven;

        // Possible improvements
        metrics.PossibleImprovements = AnalyzePossibleImprovements(optimizedAst, metrics);

        return metrics;
    }

    private static AstNode ParseExpression(string expression)
    {
        var tokens = new Lexer(expression).Tokenize();
        return new Parser(tokens).Parse();
    }

    private static ExpressionStats CalculateExpressionStats(AstNode ast)
    {
        var stats = new ExpressionStats();
        CalculateStatsRecursive(ast, stats, 0);
        return stats;
    }

    private static void CalculateStatsRecursive(AstNode node, ExpressionStats stats, int currentDepth)
    {
        stats.NodeCount++;
        stats.MaxDepth = Math.Max(stats.MaxDepth, currentDepth);

        switch (node)
        {
            case VariableNode:
                stats.LiteralCount++;
                break;

            case NotNode notNode:
                stats.OperatorCount++;
                CalculateStatsRecursive(notNode.Operand, stats, currentDepth + 1);
                break;

            case NaryNode naryNode:
                // Cost model: an n-ary connective counts as one operator/node
                stats.OperatorCount++;
                foreach (var operand in naryNode.Operands)
                    CalculateStatsRecursive(operand, stats, currentDepth + 1);
                break;

            case BinaryNode binaryNode:
                stats.OperatorCount++;
                CalculateStatsRecursive(binaryNode.Left, stats, currentDepth + 1);
                CalculateStatsRecursive(binaryNode.Right, stats, currentDepth + 1);
                break;
        }
    }

    private static double CalculateComplexity(ExpressionStats stats)
    {
        // Complexity formula: considers operators, literals and depth count
        return stats.OperatorCount * 1.0 + stats.LiteralCount * 0.5 + stats.MaxDepth * 0.8;
    }

    private static int CalculateOptimalityScore(ExpressionStats original, ExpressionStats optimized,
        List<string> appliedRules)
    {
        var score = 50; // Base score

        // Compression bonuses
        var compressionRatio = (double)optimized.NodeCount / original.NodeCount;
        if (compressionRatio < 0.5) score += 30; // Excellent compression
        else if (compressionRatio < 0.7) score += 20; // Good compression
        else if (compressionRatio < 0.9) score += 10; // Moderate compression

        // Depth reduction bonuses
        if (optimized.MaxDepth < original.MaxDepth) score += (original.MaxDepth - optimized.MaxDepth) * 5;

        // Advanced rules application bonuses
        var advancedRules = new[] { "Factorization", "Consensus", "ExtendedAbsorption" };
        score += appliedRules.Count(r => advancedRules.Contains(r)) * 5;

        // Penalties for non-optimality
        if (optimized.NodeCount > original.NodeCount) score -= 20; // Expansion instead of compression
        if (appliedRules.Count == 0) score -= 10; // No rules applied

        return Math.Max(0, Math.Min(100, score));
    }

    private static List<string> AnalyzePossibleImprovements(AstNode ast, QualityMetrics metrics)
    {
        var improvements = new List<string>();

        // Analysis of patterns that can be improved
        if (ContainsPattern(ast, node => node is AndNode and && and.Operands.All(o => o is VariableNode)))
            improvements.Add("Possible additional variable factorization");

        if (ContainsPattern(ast, node => node is NotNode { Operand: NotNode }))
            improvements.Add("Double negation detected, can be simplified");

        if (metrics.MaxDepth > 5) improvements.Add("High nesting depth - consider regrouping");

        if (metrics.LiteralCount > 10) improvements.Add("Large number of literals - possible additional optimization");

        if (metrics.CompressionRatio > 0.9)
            improvements.Add("Low compression ratio - check for additional transformations");

        return improvements;
    }

    private static bool ContainsPattern(AstNode ast, Func<AstNode, bool> pattern)
    {
        if (pattern(ast)) return true;

        switch (ast)
        {
            case NotNode notNode:
                return ContainsPattern(notNode.Operand, pattern);

            case NaryNode naryNode:
                return naryNode.Operands.Any(operand => ContainsPattern(operand, pattern));

            case BinaryNode binaryNode:
                return ContainsPattern(binaryNode.Left, pattern) ||
                       ContainsPattern(binaryNode.Right, pattern);

            default:
                return false;
        }
    }

    /// <summary>
    ///     Generates optimization quality report
    /// </summary>
    public static string GenerateQualityReport(OptimizationResult result)
    {
        var metrics = AnalyzeOptimization(result);
        var sb = new StringBuilder();

        sb.AppendLine("=== OPTIMIZATION QUALITY REPORT ===");
        sb.AppendLine($"Original expression: {result.Original}");
        sb.AppendLine($"Optimized: {result.Optimized}");
        sb.AppendLine();

        sb.AppendLine("--- KEY METRICS ---");
        sb.AppendLine($"Compression ratio: {metrics.CompressionRatio:P1}");
        sb.AppendLine($"Literal count: {metrics.LiteralCount}");
        sb.AppendLine($"Operator count: {metrics.OperatorCount}");
        sb.AppendLine($"Maximum depth: {metrics.MaxDepth}");
        sb.AppendLine($"Complexity: {metrics.Complexity:F1}");
        sb.AppendLine();

        sb.AppendLine("--- QUALITY ASSESSMENT ---");
        sb.AppendLine($"Heuristic quality score: {metrics.OptimalityScore}/100");
        sb.AppendLine($"Proven minimal (two-level): {(metrics.IsOptimal ? "Yes" : "No")}");
        sb.AppendLine();

        // Convergence and memory telemetry, when the underlying run captured metrics
        if (result.Metrics is { } runMetrics)
        {
            sb.AppendLine("--- CONVERGENCE & MEMORY ---");
            sb.AppendLine($"Fixpoint iterations: {runMetrics.Iterations}");
            if (runMetrics.OptimizationSteps.Count > 0)
                sb.AppendLine($"Convergence trace: {string.Join(" -> ", runMetrics.OptimizationSteps)}");
            if (runMetrics.AllocatedBytes > 0)
                sb.AppendLine($"Allocated memory: {runMetrics.AllocatedBytes:N0} bytes");
            sb.AppendLine();
        }

        if (metrics.AppliedOptimizations.Any())
        {
            sb.AppendLine("--- APPLIED OPTIMIZATIONS ---");
            foreach (var optimization in metrics.AppliedOptimizations) sb.AppendLine($"• {optimization}");
            sb.AppendLine();
        }

        if (metrics.PossibleImprovements.Any())
        {
            sb.AppendLine("--- POSSIBLE IMPROVEMENTS ---");
            foreach (var improvement in metrics.PossibleImprovements) sb.AppendLine($"• {improvement}");
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("--- RECOMMENDATIONS ---");
        if (metrics.OptimalityScore >= 90)
            sb.AppendLine("• Excellent optimization! Expression is close to optimal state.");
        else if (metrics.OptimalityScore >= 70)
            sb.AppendLine("• Good optimization with possibility for minor improvements.");
        else
            sb.AppendLine("• Additional optimization required to achieve better results.");

        if (metrics.CompressionRatio > 1.0)
            sb.AppendLine("• Warning: expression size increased. Check optimization logic.");

        return sb.ToString();
    }

    /// <summary>
    ///     Optimization quality metrics
    /// </summary>
    public class QualityMetrics
    {
        public double CompressionRatio { get; set; }
        public int LiteralCount { get; set; }
        public int OperatorCount { get; set; }
        public int MaxDepth { get; set; }
        public double Complexity { get; set; }

        /// <summary>
        ///     True only when the result is a proven two-level minimum
        ///     (<see cref="MinimizationStatus.MinimalProven" />) under the (total literals,
        ///     then term count) cost model — a formal property, not a heuristic verdict.
        /// </summary>
        public bool IsOptimal { get; set; }

        /// <summary>
        ///     Heuristic 0-100 rating of the transformation (compression, depth reduction,
        ///     advanced rules). A quality signal only; it does NOT prove optimality — see
        ///     <see cref="IsOptimal" /> for the provable property.
        /// </summary>
        public int OptimalityScore { get; set; }
        public List<string> AppliedOptimizations { get; set; } = new();
        public List<string> PossibleImprovements { get; set; } = new();
    }

    private class ExpressionStats
    {
        public int NodeCount { get; set; }
        public int LiteralCount { get; set; }
        public int OperatorCount { get; set; }
        public int MaxDepth { get; set; }
    }
}
