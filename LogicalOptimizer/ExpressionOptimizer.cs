using System;
using System.Diagnostics;
using LogicalOptimizer.Optimizers;
using static LogicalOptimizer.Optimizers.AstUtilities;

namespace LogicalOptimizer;

/// <summary>
/// Main expression optimizer that coordinates specialized optimization algorithms
/// </summary>
internal class ExpressionOptimizer
{
    /// <summary>Expand-reduce is attempted only on expressions up to this many nodes.</summary>
    private const int MaxExpandReduceInputNodes = 80;

    /// <summary>Expand-reduce is abandoned when distribution grows past this many nodes.</summary>
    private const int MaxExpandedNodes = 400;

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
    private readonly DistributiveOptimizer _distributiveOptimizer = new();

    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null,
        CancellationToken cancellationToken = default, ResourceBudget? budget = null)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        budget ??= ResourceBudget.Default;

        var stopwatch = Stopwatch.StartNew();
        var input = node;
        var originalNodeCount = AstMetrics.CountNodes(node);
        var variableCount = node.GetVariables().Count;

        // Validate constraints
        PerformanceValidator.ValidateAst(node);

        if (metrics != null) metrics.OriginalNodes = originalNodeCount;

        AstNode optimized;
        var iterations = 0;
        var seenStates = new HashSet<string> { node.ToString() };

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            PerformanceValidator.ValidateProcessingTime(stopwatch.Elapsed);

            optimized = node;
            node = ApplyOptimizations(node, metrics);
            iterations++;

            // Cycle detection: revisiting a state means the rules oscillate — stop
            if (!seenStates.Add(node.ToString())) break;
        } while (!AreEqual(optimized, node) && iterations < _maxIterations);

        // Expand-reduce: for expressions beyond the exact-minimizer gate, try a bounded
        // distribute-then-simplify pass — some minima require temporary growth that the
        // greedy loop cannot cross (e.g. (a|b)&c | a&!c → a | b&c)
        if (variableCount > PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES &&
            AstMetrics.CountNodes(node) <= MaxExpandReduceInputNodes)
            node = TryExpandReduce(node);

        // Soundness guard: a rewrite must never change the function. A mismatch means
        // an optimizer bug — fall back to the untouched input rather than emit a wrong result.
        // Small expressions are verified by truth table, larger ones by the SAT-based miter
        // (a budget-exhausted Unknown keeps the result: the guard only acts on refutation).
        var guardVerdict = variableCount <= PerformanceValidator.MAX_EQUIVALENCE_CHECK_VARIABLES
            ? TruthTable.AreEquivalent(input, node)
            : EquivalenceChecker.CheckWithSat(input, node,
                budget.SoundnessGuardConflictLimit, cancellationToken).AreEquivalent != false;
        if (!guardVerdict)
        {
            if (metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("SoundnessRollback", 0);
                metrics.RuleApplicationCount["SoundnessRollback"]++;
            }

            node = input;
        }

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
        // Apply optimizations in logical order using specialized optimizers
        node = _deMorganOptimizer.Optimize(node, metrics);
        node = _constantsOptimizer.Optimize(node, metrics);
        node = _absorptionOptimizer.Optimize(node, metrics);
        node = _complementOptimizer.Optimize(node, metrics);
        node = _associativityOptimizer.Optimize(node, metrics);

        // Consensus enforces per-node acceptance internally
        node = _consensusOptimizer.Optimize(node, metrics);

        node = _redundancyOptimizer.Optimize(node, metrics);
        node = _commutativityOptimizer.Optimize(node, metrics);

        // Factorization can still grow the whole tree, so keep the global rollback
        node = ApplyOptimizationRuleWithRollback(node,
            (n, m) => _factorizationOptimizer.Optimize(n, m), metrics, "Factorization");

        return node;
    }

    /// <summary>
    ///     Bounded expand-reduce: distribute, re-simplify, keep the result only if it is
    ///     strictly cheaper (literals, then nodes) than the current expression.
    /// </summary>
    private AstNode TryExpandReduce(AstNode node)
    {
        var expanded = _distributiveOptimizer.Optimize(node);
        if (AstMetrics.CountNodes(expanded) > MaxExpandedNodes) return node;

        var reduced = expanded;
        AstNode previous;
        var iterations = 0;
        do
        {
            previous = reduced;
            reduced = ApplyOptimizations(reduced);
            iterations++;
        } while (!AreEqual(previous, reduced) && iterations < _maxIterations);

        var currentCost = (AstMetrics.CountLiterals(node), AstMetrics.CountNodes(node));
        var reducedCost = (AstMetrics.CountLiterals(reduced), AstMetrics.CountNodes(reduced));
        return reducedCost.CompareTo(currentCost) < 0 ? reduced : node;
    }
}
