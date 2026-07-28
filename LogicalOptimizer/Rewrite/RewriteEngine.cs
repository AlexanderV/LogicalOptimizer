using System.Diagnostics;
using System.Linq;

namespace LogicalOptimizer.Rewrite;

/// <summary>
/// Unified canonical rewrite engine: a single bottom-up traversal drives the local
/// <see cref="IRewriteRule" />s in a fixed order (DeMorgan → Absorption → Consensus →
/// Redundancy → Factorization). Flattening, commutativity, constant/complement folding
/// and double negation — the former Constants/Associativity/Commutativity/Complement
/// optimizers — happen at construction time inside the <see cref="FormulaFactory" />,
/// so no rules exist for them. The engine owns the fixpoint loop with iteration limit
/// and cycle detection (reference equality of interned nodes), metrics recording, the
/// bounded expand-reduce strategy and the truth-table/SAT soundness guard with rollback.
/// </summary>
internal sealed class RewriteEngine
{
    /// <summary>Expand-reduce is attempted only on expressions up to this many nodes.</summary>
    private const int MaxExpandReduceInputNodes = 80;

    /// <summary>Expand-reduce is abandoned when distribution grows past this many nodes.</summary>
    private const int MaxExpandedNodes = 400;

    private readonly int _maxIterations = PerformanceValidator.MAX_OPTIMIZATION_ITERATIONS;

    private readonly FormulaFactory _factory;

    /// <summary>Rules in application order; the order is a behavioral contract.</summary>
    private readonly IRewriteRule[] _rules;

    /// <summary>Creates an engine with a private factory (one per optimization run).</summary>
    public RewriteEngine() : this(new FormulaFactory())
    {
    }

    /// <summary>
    /// Creates an engine that builds every rewrite through the given factory. Callers
    /// that post-process the result (e.g. importing minimizer output for comparison)
    /// should share one factory per optimization run so interning spans the whole run.
    /// </summary>
    public RewriteEngine(FormulaFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _rules = new IRewriteRule[]
        {
            new DeMorganRule(),
            new AbsorptionRule(),
            new ConsensusRule(),
            new RedundancyRule(),
            new FactorizationRule()
        };
    }

    /// <summary>The factory this engine builds rewrites through.</summary>
    public FormulaFactory Factory => _factory;

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

        // Construction-time canonicalization: flatten, dedup, fold constants/complements,
        // sort, intern. From here on structurally equal nodes are reference-equal.
        node = _factory.Import(node);

        AstNode previous;
        var iterations = 0;
        var seenStates = new HashSet<AstNode>(ReferenceEqualityComparer.Instance) { node };

        // Convergence trace: the node count at each fixpoint iteration. Recorded only when
        // metrics are requested; lets callers see how (and whether) the rewrite converged.
        metrics?.AddStep($"iter 0: {AstMetrics.CountNodes(node)} nodes");

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            PerformanceValidator.ValidateProcessingTime(stopwatch.Elapsed);

            previous = node;
            node = RewritePass(node, metrics);
            iterations++;
            metrics?.AddStep($"iter {iterations}: {AstMetrics.CountNodes(node)} nodes");

            // Cycle detection: revisiting a state means the rules oscillate — stop
            if (!seenStates.Add(node)) break;
        } while (!ReferenceEquals(previous, node) && iterations < _maxIterations);

        // Expand-reduce: for expressions beyond the exact-minimizer gate, try a bounded
        // distribute-then-simplify pass — some minima require temporary growth that the
        // greedy loop cannot cross (e.g. (a|b)&c | a&!c → a | b&c)
        if (variableCount > PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES &&
            AstMetrics.CountNodes(node) <= MaxExpandReduceInputNodes)
            node = TryExpandReduce(node);

        // Soundness guard: a rewrite must never change the function, and it is accepted only
        // when equivalence is positively PROVEN. A refutation means a rule bug; an Unknown
        // (SAT budget exhausted before proving equivalence) is treated the same as a failure —
        // we fall back to the untouched input rather than ship an unverified result. Small
        // expressions are verified by truth table, larger ones by the SAT-based miter.
        var guardVerdict = variableCount <= PerformanceValidator.MAX_EQUIVALENCE_CHECK_VARIABLES
            ? TruthTable.AreEquivalent(input, node)
            : EquivalenceChecker.CheckWithSat(input, node,
                budget.SoundnessGuardConflictLimit, cancellationToken).AreEquivalent == true;
        if (!guardVerdict)
        {
            RecordRuleApplication(metrics, "SoundnessRollback", countAsApplied: false);
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

    /// <summary>
    /// One bottom-up traversal: children are rebuilt (and rewritten) first through the
    /// factory, then the rules run at the node in the fixed order until it stabilizes.
    /// </summary>
    private AstNode RewritePass(AstNode node, OptimizationMetrics? metrics)
    {
        node = node switch
        {
            AndNode and => _factory.And(and.Operands.Select(o => RewritePass(o, metrics))),
            OrNode or => _factory.Or(or.Operands.Select(o => RewritePass(o, metrics))),
            NotNode not => _factory.Not(RewritePass(not.Operand, metrics)),
            _ => node
        };

        return ApplyRulesAtNode(node, metrics);
    }

    private AstNode ApplyRulesAtNode(AstNode node, OptimizationMetrics? metrics)
    {
        var localIterations = 0;
        var changed = true;
        while (changed && localIterations++ < _maxIterations)
        {
            changed = false;
            foreach (var rule in _rules)
            {
                var rewritten = rule.TryRewrite(node, _factory);
                if (rewritten is null || rewritten.Equals(node)) continue;

                // Growth guard cost order matches TryExpandReduce and the facade's
                // SelectCheapest: literals first, then nodes. Factoring legitimately
                // trades node count for literal count (a & X | a & Y → a & (X | Y) can
                // add a node under the n-ary cost model while removing a literal), so a
                // nodes-only comparison would wrongly roll such rewrites back.
                if (rule.GuardAgainstGrowth &&
                    (AstMetrics.CountLiterals(rewritten), AstMetrics.CountNodes(rewritten))
                    .CompareTo((AstMetrics.CountLiterals(node), AstMetrics.CountNodes(node))) > 0)
                {
                    RecordRuleApplication(metrics, rule.Name + "_Rollback", countAsApplied: false);
                    continue;
                }

                RecordRuleApplication(metrics, rule.Name, countAsApplied: true);
                node = rewritten;
                changed = true;
            }
        }

        return node;
    }

    /// <summary>
    /// Bounded expand-reduce: distribute once, re-simplify to fixpoint, keep the result
    /// only if it is strictly cheaper (literals, then nodes) than the current expression.
    /// </summary>
    private AstNode TryExpandReduce(AstNode node)
    {
        var expanded = new DistributiveExpander(_factory).ExpandOnce(node);
        if (AstMetrics.CountNodes(expanded) > MaxExpandedNodes) return node;

        var reduced = expanded;
        AstNode previous;
        var iterations = 0;
        do
        {
            previous = reduced;
            reduced = RewritePass(reduced, null);
            iterations++;
        } while (!ReferenceEquals(previous, reduced) && iterations < _maxIterations);

        var currentCost = (AstMetrics.CountLiterals(node), AstMetrics.CountNodes(node));
        var reducedCost = (AstMetrics.CountLiterals(reduced), AstMetrics.CountNodes(reduced));
        return reducedCost.CompareTo(currentCost) < 0 ? reduced : node;
    }

    private static void RecordRuleApplication(OptimizationMetrics? metrics, string key, bool countAsApplied)
    {
        if (metrics == null) return;

        metrics.RuleApplicationCount.TryAdd(key, 0);
        metrics.RuleApplicationCount[key]++;
        if (countAsApplied) metrics.AppliedRules++;
    }
}
