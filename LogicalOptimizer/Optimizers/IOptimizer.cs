namespace LogicalOptimizer.Optimizers;

/// <summary>
/// Interface for specific optimization algorithms
/// </summary>
public interface IOptimizer
{
    /// <summary>
    /// Apply specific optimization to the AST node
    /// </summary>
    AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null);
}
