using LogicalOptimizer.Rewrite;

namespace LogicalOptimizer;

public class BooleanExpressionOptimizer
{
    /// <summary>Backward-compatible overload: computes every artifact.</summary>
    public OptimizationResult OptimizeExpression(string expression, bool includeMetrics = false,
        bool includeDebugInfo = false)
    {
        return OptimizeExpression(expression, new OptimizationOptions
        {
            IncludeMetrics = includeMetrics || includeDebugInfo,
            IncludeTruthTables = includeMetrics,
            IncludeDebugInfo = includeDebugInfo
        });
    }

    public OptimizationResult OptimizeExpression(string expression, OptimizationOptions options)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be empty", nameof(expression));

        // Performance constraint validation
        PerformanceValidator.ValidateExpression(expression);

        try
        {
            // One factory per optimization run: every canonical node of this call is
            // built and interned through it
            var factory = new FormulaFactory();

            var lexer = new Lexer(expression);
            var tokens = lexer.Tokenize();

            var parser = new Parser(tokens, factory);
            var ast = parser.Parse();

            var metrics = options.IncludeMetrics ? new OptimizationMetrics() : null;
            var optimizer = new RewriteEngine(factory);
            var optimized = optimizer.Optimize(ast, metrics, options.CancellationToken, options.Budget);

            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var cnfText = "";
            var dnfText = "";
            var cnfStatus = options.ComputeCnf ? ComputationStatus.Computed : ComputationStatus.NotRequested;
            var dnfStatus = options.ComputeDnf ? ComputationStatus.Computed : ComputationStatus.NotRequested;
            var computeEquivalentCnf = options.ComputeCnf && options.CnfMode == CnfMode.Equivalent;

            var exactCompleted = false;
            var minimizationStatus = MinimizationStatus.Heuristic;
            if (variables.Count <= PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES)
                try
                {
                    // Exact backend: compute the guaranteed-minimal two-level forms from the
                    // truth table and keep whichever candidate is cheapest. The rewrite
                    // pipeline still contributes factored (multi-level) forms that can beat
                    // the minimal SOP on literal count.
                    // Within the guarantee range QM runs unbounded (provably minimal, always);
                    // between it and the gate a work budget keeps dense functions from
                    // costing seconds — those fall back to the heuristic path below
                    long? qmBudget = variables.Count <= PerformanceValidator.EXACT_GUARANTEE_VARIABLES
                        ? null
                        : options.Budget.QmPairComparisonLimit;

                    var onSet = ComputeOnSet(ast, variables);
                    var (rawMinSop, provenMinimal) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
                        pairComparisonLimit: qmBudget, cancellationToken: options.CancellationToken,
                        coverStepLimit: variables.Count <= PerformanceValidator.EXACT_GUARANTEE_VARIABLES
                            ? PerformanceValidator.GUARANTEE_COVER_STEP_LIMIT
                            : options.Budget.CoverStepLimit);
                    minimizationStatus = provenMinimal
                        ? MinimizationStatus.MinimalProven
                        : MinimizationStatus.BudgetExceeded;
                    // Import canonicalizes the raw minimizer output (flatten/sort/intern)
                    var minSop = factory.Import(rawMinSop);
                    var factoredMinSop =
                        AstMetrics.CountNodes(minSop) <= PerformanceValidator.MAX_FACTORED_MIN_SOP_NODES
                            ? optimizer.Optimize(minSop, null, options.CancellationToken, options.Budget)
                            : minSop;

                    var selected = SelectCheapest(optimized, factoredMinSop, minSop);
                    if (!ReferenceEquals(selected, optimized) && metrics != null)
                    {
                        metrics.RuleApplicationCount.TryAdd("ExactMinimization", 0);
                        metrics.RuleApplicationCount["ExactMinimization"]++;
                    }

                    optimized = selected;
                    if (metrics != null) metrics.OptimizedNodes = AstMetrics.CountNodes(optimized);

                    if (options.ComputeDnf) dnfText = minSop.ToString();
                    if (computeEquivalentCnf)
                        cnfText = factory.Import(TruthTableMinimizer.MinimalPos(variables, onSet,
                            pairComparisonLimit: qmBudget, cancellationToken: options.CancellationToken)).ToString();
                    exactCompleted = true;
                }
                catch (InvalidOperationException)
                {
                    // Dense function beyond the QM work budget: fall back to the
                    // heuristic normal forms below (result stays sound, minimality
                    // guarantee is waived for this expression)
                    minimizationStatus = MinimizationStatus.Heuristic;
                }

            if (!exactCompleted)
            {
                // Local exact rewriting first: every AND/OR/NOT subtree over ≤3 distinct
                // variables drops to its provably minimal library form (sound by
                // construction — each entry is an exact-minimizer result)
                var locallyRewritten = factory.Import(SubcircuitLibrary.RewriteSubcircuits(optimized));
                if (AstMetrics.CountLiterals(locallyRewritten) < AstMetrics.CountLiterals(optimized))
                {
                    if (metrics != null)
                    {
                        metrics.RuleApplicationCount.TryAdd("SubcircuitRewrite", 0);
                        metrics.RuleApplicationCount["SubcircuitRewrite"]++;
                    }

                    optimized = locallyRewritten;
                    if (metrics != null) metrics.OptimizedNodes = AstMetrics.CountNodes(optimized);
                }

                // Mid range (13-24 variables, or a dense fallback): SAT-based prime cover.
                // No truth table involved; the candidate is adopted only after a SAT-miter
                // proof of equivalence, so soundness never rests on the search itself.
                if (variables.Count <= PerformanceValidator.MAX_SAT_MINIMIZATION_VARIABLES)
                {
                    var satSop = SatTwoLevelMinimizer.TryMinimize(optimized,
                        PerformanceValidator.SAT_MINIMIZATION_CUBE_LIMIT,
                        PerformanceValidator.SAT_MINIMIZATION_QUERY_CONFLICTS,
                        options.CancellationToken);

                    if (satSop != null &&
                        EquivalenceChecker.CheckWithSat(optimized, satSop,
                            options.Budget.SatConflictLimit, options.CancellationToken).AreEquivalent == true)
                    {
                        satSop = factory.Import(satSop);
                        var factoredSatSop =
                            AstMetrics.CountNodes(satSop) <= PerformanceValidator.MAX_FACTORED_MIN_SOP_NODES
                                ? optimizer.Optimize(satSop, null, options.CancellationToken, options.Budget)
                                : satSop;

                        var selected = SelectCheapest(optimized, factoredSatSop, satSop);
                        if (!ReferenceEquals(selected, optimized) && metrics != null)
                        {
                            metrics.RuleApplicationCount.TryAdd("SatCoverMinimization", 0);
                            metrics.RuleApplicationCount["SatCoverMinimization"]++;
                        }

                        optimized = selected;
                        if (metrics != null) metrics.OptimizedNodes = AstMetrics.CountNodes(optimized);

                        // The verified prime cover doubles as a computed DNF the
                        // distribution-based converter often cannot reach at this scale
                        if (options.ComputeDnf) dnfText = satSop.ToString();
                    }
                }

                var converter = new NormalFormConverter();
                // Normal forms can blow up exponentially; TooLarge (displayed as "-")
                // marks a form that was abandoned
                if (computeEquivalentCnf)
                    try
                    {
                        cnfText = Transformations.SubsumeCnf(converter.ConvertToCNF(optimized)).ToString();
                    }
                    catch (InvalidOperationException)
                    {
                        (cnfText, cnfStatus) = ("-", ComputationStatus.TooLarge);
                    }

                if (options.ComputeDnf && dnfText.Length == 0)
                    try
                    {
                        // Beyond the exact/SAT zones the distributed DNF is usually far
                        // from minimal; the Espresso-style cube-list pass shrinks it
                        // without any 2^n structure (sound by construction)
                        var dnf = Transformations.SubsumeDnf(converter.ConvertToDNF(optimized));
                        dnf = Transformations.MinimizeDnfHeuristic(dnf,
                            cancellationToken: options.CancellationToken);
                        dnfText = dnf.ToString();
                    }
                    catch (InvalidOperationException)
                    {
                        (dnfText, dnfStatus) = ("-", ComputationStatus.TooLarge);
                    }
            }

            // Tseitin CNF is linear in expression size, so it applies uniformly at any scale
            if (options.ComputeCnf && options.CnfMode == CnfMode.Tseitin)
                cnfText = TseitinConverter.Convert(optimized).ToString();

            // Experimental opt-in: DAG-aware AIG rewriting as one more multi-level candidate.
            // Adopted only when it is verified equivalent AND strictly cheaper, so it can only
            // ever improve the result and never regress it. Off by default -> no effect at all.
            if (options.EnableAigRewriting)
            {
                var aigCandidate = TryAigRewrite(optimized, factory, options);
                if (aigCandidate != null)
                {
                    var selected = SelectCheapest(optimized, aigCandidate);
                    if (!ReferenceEquals(selected, optimized))
                    {
                        if (metrics != null)
                        {
                            metrics.RuleApplicationCount.TryAdd("AigRewrite", 0);
                            metrics.RuleApplicationCount["AigRewrite"]++;
                        }

                        optimized = selected;
                        if (metrics != null) metrics.OptimizedNodes = AstMetrics.CountNodes(optimized);
                    }
                }
            }

            var advancedForms = "";
            if (options.ComputeAdvancedForms &&
                variables.Count <= PerformanceValidator.MAX_PATTERN_RECOGNITION_VARIABLES)
                advancedForms = new PatternRecognizer().ReplacePatterns(optimized).ToString();

            var includeTruthTables =
                options.IncludeTruthTables && variables.Count <= PerformanceValidator.MAX_TRUTH_TABLE_VARIABLES;

            var result = new OptimizationResult
            {
                Original = expression,
                Optimized = optimized.ToString(),
                CNF = cnfText,
                DNF = dnfText,
                CnfStatus = cnfStatus,
                DnfStatus = dnfStatus,
                Advanced = advancedForms,
                MinimizationStatus = minimizationStatus,
                Variables = variables,
                Metrics = metrics,
                OriginalTruthTable = includeTruthTables ? TruthTable.Generate(ast) : null,
                OptimizedTruthTable = includeTruthTables ? TruthTable.Generate(optimized) : null
            };

            if (options.IncludeDebugInfo && metrics != null)
                result.DebugInfo =
                    "=== Debug Information ===\n" +
                    $"Original AST:\n{AstVisualizer.VisualizeTree(ast)}\n" +
                    $"Optimized AST:\n{AstVisualizer.VisualizeTree(optimized)}\n" +
                    metrics;

            return result;
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Error processing expression '{expression}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Equisatisfiable CNF via Tseitin transformation: linear in expression size,
    ///     introduces auxiliary _tN variables, applicable at any scale (unlike the
    ///     equivalent CNF, which may be TooLarge beyond the exact threshold).
    /// </summary>
    public TseitinCnf ToEquisatisfiableCnf(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be empty", nameof(expression));

        PerformanceValidator.ValidateExpression(expression);
        return TseitinConverter.Convert(expression);
    }

    /// <summary>Rounds of AIG rewriting attempted before giving up on further gains.</summary>
    private const int AigRewriteMaxRounds = 32;

    /// <summary>
    ///     Ceiling on AIG size (AND nodes) for the experimental rewrite candidate: past it the
    ///     O(graph)-per-move rebuild is skipped so the opt-in path cannot dominate runtime.
    /// </summary>
    private const int AigRewriteMaxAndNodes = 4000;

    /// <summary>
    ///     Produce the AIG-rewritten form of <paramref name="expression" /> as an optimization
    ///     candidate, or null when it does not apply (too large, rewriting found nothing, or the
    ///     result fails the belt-and-suspenders equivalence check). Never throws on the SAT/AIG
    ///     path failing — a null just means "no extra candidate".
    /// </summary>
    private static AstNode? TryAigRewrite(AstNode expression, FormulaFactory factory, OptimizationOptions options)
    {
        var aig = AndInverterGraph.FromAst(expression);
        if (aig.AndNodeCount > AigRewriteMaxAndNodes) return null;

        var rewritten = aig.RewriteToFixpoint(AigRewriteMaxRounds, options.CancellationToken);
        if (rewritten.AndNodeCount >= aig.AndNodeCount) return null; // no structural gain

        var candidate = factory.Import(rewritten.ToAst(rewritten.Root));

        // Belt-and-suspenders: the rewrite is function-preserving by construction, but the
        // adopted candidate must independently pass the existing equivalence checker.
        var equivalent = EquivalenceChecker
            .Check(expression, candidate, options.Budget.SatConflictLimit, options.CancellationToken)
            .AreEquivalent;

        return equivalent == true ? candidate : null;
    }

    /// <summary>Minterm indices where the expression is true; bit j = value of variables[j].</summary>
    private static HashSet<int> ComputeOnSet(AstNode ast, List<string> variables)
    {
        var onSet = new HashSet<int>();
        var assignment = new Dictionary<string, bool>();
        var numRows = 1 << variables.Count;

        for (var minterm = 0; minterm < numRows; minterm++)
        {
            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (minterm & (1 << j)) != 0;

            if (TruthTable.Evaluate(ast, assignment))
                onSet.Add(minterm);
        }

        return onSet;
    }

    /// <summary>Cheapest candidate by literal count, then node count; earlier wins ties.</summary>
    private static AstNode SelectCheapest(params AstNode[] candidates)
    {
        var best = candidates[0];
        var bestCost = (AstMetrics.CountLiterals(best), AstMetrics.CountNodes(best));

        foreach (var candidate in candidates.Skip(1))
        {
            var cost = (AstMetrics.CountLiterals(candidate), AstMetrics.CountNodes(candidate));
            if (cost.CompareTo(bestCost) < 0)
            {
                best = candidate;
                bestCost = cost;
            }
        }

        return best;
    }
}
