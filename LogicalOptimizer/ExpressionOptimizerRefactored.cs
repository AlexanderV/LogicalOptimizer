using System.Diagnostics;
using LogicalOptimizer.Optimizers;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer;

/// <summary>
/// Main expression optimizer that coordinates specialized optimization algorithms
/// </summary>
public class ExpressionOptimizerRefactored
{
    private readonly int _maxIterations = PerformanceValidator.MAX_OPTIMIZATION_ITERATIONS;
    
    // Specialized optimizers
    private readonly DeMorganOptimizer _deMorganOptimizer = new();
    private readonly ConstantsOptimizer _constantsOptimizer = new();
    private readonly AbsorptionOptimizer _absorptionOptimizer = new();
    private readonly ComplementOptimizer _complementOptimizer = new();
    private readonly AssociativityOptimizer _associativityOptimizer = new();
    private readonly ConsensusOptimizer _consensusOptimizer = new();
    private readonly CommutativityOptimizer _commutativityOptimizer = new();
    private readonly FactorizationOptimizer _factorizationOptimizer = new();
    private readonly RedundancyOptimizer _redundancyOptimizer = new();

    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var stopwatch = Stopwatch.StartNew();
        var originalNodeCount = AstMetrics.CountNodes(node);

        // Validate constraints
        PerformanceValidator.ValidateAst(node);

        if (metrics != null) metrics.OriginalNodes = originalNodeCount;

        AstNode optimized;
        var iterations = 0;

        do
        {
            // Check execution time
            if (stopwatch.Elapsed.TotalSeconds > PerformanceValidator.MAX_PROCESSING_TIME_SECONDS)
                PerformanceValidator.ValidateProcessingTime(stopwatch.Elapsed);

            optimized = node;
            node = ApplyOptimizations(node, metrics);
            iterations++;

            // Check iteration count
            PerformanceValidator.ValidateIterations(iterations);

            // Debug information (can be enabled when needed)
            // Console.WriteLine($"Iteration {iterations}: {node}");
        } while (!AreEqual(optimized, node) && iterations < _maxIterations);

        stopwatch.Stop();

        if (metrics != null)
        {
            metrics.OptimizedNodes = AstMetrics.CountNodes(node);
            metrics.Iterations = iterations;
            metrics.ElapsedTime = stopwatch.Elapsed;
        }

        return node;
    }

    private AstNode ApplyOptimizations(AstNode node, OptimizationMetrics? metrics = null)
    {
        // Apply optimizations in logical order
        node = _deMorganOptimizer.Optimize(node, metrics);
        node = _constantsOptimizer.Optimize(node, metrics);
        node = _absorptionOptimizer.Optimize(node, metrics);
        node = _complementOptimizer.Optimize(node, metrics);
        node = _associativityOptimizer.Optimize(node, metrics);
        
        // Apply consensus with rollback protection
        node = ApplyOptimizationRuleWithRollback(node, 
            (n, m) => _consensusOptimizer.Optimize(n, m), metrics, "Consensus");
        
        node = _redundancyOptimizer.Optimize(node, metrics);
        node = _commutativityOptimizer.Optimize(node, metrics);
        
        // Apply factorization with rollback protection
        node = ApplyOptimizationRuleWithRollback(node, 
            (n, m) => _factorizationOptimizer.Optimize(n, m), metrics, "Factorization");

        return node;
    }
}
