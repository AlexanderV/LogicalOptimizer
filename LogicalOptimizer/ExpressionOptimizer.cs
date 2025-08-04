using System;
using System.Diagnostics;
using LogicalOptimizer.Optimizers;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer;

/// <summary>
/// Main expression optimizer that coordinates specialized optimization algorithms
/// </summary>
public class ExpressionOptimizer
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
        var initialNodes = AstMetrics.CountNodes(node);
        
        // Apply optimizations in logical order using specialized optimizers
        node = ProfileOptimizer("DeMorgan", _deMorganOptimizer, node, metrics);
        node = ProfileOptimizer("Constants", _constantsOptimizer, node, metrics);
        node = ProfileOptimizer("Absorption", _absorptionOptimizer, node, metrics);
        node = ProfileOptimizer("Complement", _complementOptimizer, node, metrics);
        node = ProfileOptimizer("Associativity", _associativityOptimizer, node, metrics);
        
        // Apply consensus with rollback protection
        node = ApplyOptimizationRuleWithRollback(node, 
            (n, m) => _consensusOptimizer.Optimize(n, m), metrics, "Consensus");
        
        node = ProfileOptimizer("Redundancy", _redundancyOptimizer, node, metrics);
        node = ProfileOptimizer("Commutativity", _commutativityOptimizer, node, metrics);
        
        // Apply factorization with rollback protection
        node = ApplyOptimizationRuleWithRollback(node, 
            (n, m) => _factorizationOptimizer.Optimize(n, m), metrics, "Factorization");

        return node;
    }
    
    private AstNode ProfileOptimizer(string name, IOptimizer optimizer, AstNode node, OptimizationMetrics? metrics)
    {
        return optimizer.Optimize(node, metrics);
    }
}
