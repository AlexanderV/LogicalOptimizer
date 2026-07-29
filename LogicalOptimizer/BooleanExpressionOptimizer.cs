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

        // Global processing deadline: a single linked token hands every phase below the same
        // wall-clock budget, so the documented maximum processing time bounds the whole call
        // (rewrite, truth-table build, QM, cover search, SAT, normal forms, AIG…), not just
        // the rewrite fixpoint loop. The caller's own token still cancels independently.
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(PerformanceValidator.MAX_PROCESSING_TIME_SECONDS));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            options.CancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        // Memory-usage measurement: bytes allocated on this thread across the whole call.
        var startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            // One factory per optimization run: every canonical node of this call is
            // built and interned through it
            var factory = new FormulaFactory();

            var lexer = new Lexer(expression);
            var tokens = lexer.Tokenize();

            var parser = new Parser(tokens, factory, expression);
            var ast = parser.Parse();

            var metrics = options.IncludeMetrics ? new OptimizationMetrics() : null;
            var trace = options.IncludeTrace ? new OptimizationTraceRecorder() : null;
            var optimizer = new RewriteEngine(factory);
            var optimized = optimizer.Optimize(ast, metrics, token, options.Budget, trace);

            var variables = ast.GetVariables().OrderBy(v => v).ToList();

            var cnfText = "";
            var dnfText = "";
            var cnfStatus = options.ComputeCnf ? ComputationStatus.Computed : ComputationStatus.NotRequested;
            var dnfStatus = options.ComputeDnf ? ComputationStatus.Computed : ComputationStatus.NotRequested;
            var computeEquivalentCnf = options.ComputeCnf && options.CnfMode == CnfMode.Equivalent;

            var exactCompleted = false;
            var minimizationStatus = MinimizationStatus.Heuristic;
            // POS (equivalent CNF) minimality is tracked separately: its cover search can hit
            // the budget independently of the SOP one, so a shared status would be dishonest.
            var cnfMinimizationStatus = MinimizationStatus.Heuristic;
            var inExactZone = variables.Count <= PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES;
            trace?.Add(OptimizationTraceCategory.EngineSelection, "ZoneSelection",
                inExactZone
                    ? $"{variables.Count} variable(s): exact Quine-McCluskey backend " +
                      (variables.Count <= PerformanceValidator.EXACT_GUARANTEE_VARIABLES
                          ? "in the unbounded guarantee zone (minimality provable)"
                          : "under a work budget (minimality may end BudgetExceeded)")
                    : $"{variables.Count} variable(s): beyond the exact gate — no 2^n table; " +
                      (variables.Count <= PerformanceValidator.MAX_SAT_MINIMIZATION_VARIABLES
                          ? "SAT prime-cover path, adopted only after a SAT-miter proof"
                          : "Espresso-style cube-list heuristics"),
                new Dictionary<string, string>
                {
                    ["variables"] = variables.Count.ToString(),
                    ["exactGate"] = PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES.ToString(),
                    ["guaranteeZone"] = PerformanceValidator.EXACT_GUARANTEE_VARIABLES.ToString(),
                    ["satZone"] = PerformanceValidator.MAX_SAT_MINIMIZATION_VARIABLES.ToString(),
                    ["engine"] = inExactZone
                        ? "exact-qm"
                        : variables.Count <= PerformanceValidator.MAX_SAT_MINIMIZATION_VARIABLES
                            ? "sat-prime-cover"
                            : "espresso-lite"
                });

            if (inExactZone)
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

                    // Same cover-search budget for SOP and POS: unbounded in the guarantee
                    // zone (≤10), the caller's budget above it. This keeps the equivalent-CNF
                    // (minimal POS) as provably minimal as the SOP wherever the docs promise it.
                    var coverStepLimit = variables.Count <= PerformanceValidator.EXACT_GUARANTEE_VARIABLES
                        ? PerformanceValidator.GUARANTEE_COVER_STEP_LIMIT
                        : options.Budget.CoverStepLimit;

                    trace?.Add(OptimizationTraceCategory.Budget, "ExactMinimization",
                        qmBudget == null
                            ? "QM pair comparisons unbounded (guarantee zone); cover search bounded"
                            : "QM pair comparisons and cover search bounded by the caller's budget",
                        new Dictionary<string, string>
                        {
                            ["qmPairComparisonLimit"] = qmBudget?.ToString() ?? "unbounded",
                            ["coverStepLimit"] = coverStepLimit.ToString()
                        });

                    var onSet = ComputeOnSet(ast, variables, token);
                    var (rawMinSop, provenMinimal) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
                        pairComparisonLimit: qmBudget, cancellationToken: token,
                        coverStepLimit: coverStepLimit);
                    minimizationStatus = provenMinimal
                        ? MinimizationStatus.MinimalProven
                        : MinimizationStatus.BudgetExceeded;

                    trace?.Add(provenMinimal ? OptimizationTraceCategory.Proof : OptimizationTraceCategory.Status,
                        "ExactMinimization",
                        provenMinimal
                            ? "minimum-cover search completed: the two-level cover is provably minimal"
                            : "cover search hit its step/comparison budget: sound but minimality NOT proven",
                        new Dictionary<string, string>
                        {
                            ["minimizationStatus"] = minimizationStatus.ToString(),
                            ["onSetSize"] = onSet.Count.ToString()
                        });
                    // Import canonicalizes the raw minimizer output (flatten/sort/intern)
                    var minSop = factory.Import(rawMinSop);
                    var factoredMinSop =
                        AstMetrics.CountNodes(minSop) <= PerformanceValidator.MAX_FACTORED_MIN_SOP_NODES
                            ? optimizer.Optimize(minSop, null, token, options.Budget)
                            : minSop;

                    var selected = SelectCheapestTraced(trace, "ExactMinimization",
                        ("rewritten", optimized), ("factored-min-sop", factoredMinSop), ("min-sop", minSop));
                    if (!ReferenceEquals(selected, optimized) && metrics != null)
                    {
                        metrics.RuleApplicationCount.TryAdd("ExactMinimization", 0);
                        metrics.RuleApplicationCount["ExactMinimization"]++;
                    }

                    optimized = selected;
                    if (metrics != null) metrics.OptimizedNodes = AstMetrics.CountNodes(optimized);

                    if (options.ComputeDnf) dnfText = minSop.ToString();
                    if (computeEquivalentCnf)
                    {
                        var (rawMinPos, posProven) = TruthTableMinimizer.MinimalPosWithStatus(variables, onSet,
                            pairComparisonLimit: qmBudget, cancellationToken: token,
                            coverStepLimit: coverStepLimit);
                        cnfText = factory.Import(rawMinPos).ToString();
                        cnfMinimizationStatus = posProven
                            ? MinimizationStatus.MinimalProven
                            : MinimizationStatus.BudgetExceeded;

                        trace?.Add(posProven ? OptimizationTraceCategory.Proof : OptimizationTraceCategory.Status,
                            "EquivalentCnf",
                            posProven
                                ? "minimum POS cover search completed: the equivalent CNF is provably minimal"
                                : "POS cover search hit its budget: the CNF is sound but not proven minimal",
                            new Dictionary<string, string>
                            {
                                ["cnfMinimizationStatus"] = cnfMinimizationStatus.ToString()
                            });
                    }

                    exactCompleted = true;
                }
                catch (ComputationBudgetExceededException)
                {
                    trace?.Add(OptimizationTraceCategory.Fallback, "ExactMinimization",
                        "exact backend exceeded its work budget — falling back to the heuristic path; " +
                        "the result stays sound, only the minimality proof is waived",
                        new Dictionary<string, string> { ["minimizationStatus"] = "BudgetExceeded" });
                    // Dense function beyond the QM work budget: report the exhaustion honestly
                    // (never disguised as an ordinary heuristic pass) and fall back to the
                    // heuristic normal forms below. The result stays sound; only the minimality
                    // proof is waived for this expression. A plain InvalidOperationException is
                    // NOT caught here — a real invariant violation must surface, not be masked.
                    minimizationStatus = MinimizationStatus.BudgetExceeded;
                }

            if (!exactCompleted)
            {
                // Local exact rewriting first: every AND/OR/NOT subtree over ≤3 distinct
                // variables drops to its provably minimal library form (sound by
                // construction — each entry is an exact-minimizer result)
                var locallyRewritten = factory.Import(SubcircuitLibrary.RewriteSubcircuits(optimized));
                var subcircuitImproved =
                    AstMetrics.CountLiterals(locallyRewritten) < AstMetrics.CountLiterals(optimized);
                trace?.Add(subcircuitImproved ? OptimizationTraceCategory.Adopted : OptimizationTraceCategory.Rejected,
                    "SubcircuitRewrite",
                    subcircuitImproved
                        ? "every ≤3-variable subtree replaced by its provably minimal library form"
                        : "library forms were not cheaper than the current expression",
                    new Dictionary<string, string>
                    {
                        ["literalsBefore"] = AstMetrics.CountLiterals(optimized).ToString(),
                        ["literalsAfter"] = AstMetrics.CountLiterals(locallyRewritten).ToString()
                    });

                if (subcircuitImproved)
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
                        token);

                    var satProven = satSop != null &&
                                    EquivalenceChecker.CheckWithSat(optimized, satSop,
                                        options.Budget.SatConflictLimit, token).AreEquivalent == true;

                    trace?.Add(satProven ? OptimizationTraceCategory.Proof : OptimizationTraceCategory.Rejected,
                        "SatPrimeCover",
                        satSop == null
                            ? "SAT prime-cover search produced no cover within its cube/conflict limits"
                            : satProven
                                ? "prime cover proven equivalent by SAT miter — eligible as a candidate"
                                : "prime cover NOT proven equivalent within the SAT budget — discarded",
                        new Dictionary<string, string>
                        {
                            ["coverFound"] = satSop != null ? "true" : "false",
                            ["proven"] = satProven ? "true" : "false",
                            ["cubeLimit"] = PerformanceValidator.SAT_MINIMIZATION_CUBE_LIMIT.ToString(),
                            ["satConflictLimit"] = options.Budget.SatConflictLimit.ToString()
                        });

                    if (satProven)
                    {
                        // satProven implies satSop != null (checked above).
                        satSop = factory.Import(satSop!);
                        var factoredSatSop =
                            AstMetrics.CountNodes(satSop) <= PerformanceValidator.MAX_FACTORED_MIN_SOP_NODES
                                ? optimizer.Optimize(satSop, null, token, options.Budget)
                                : satSop;

                        var selected = SelectCheapestTraced(trace, "SatPrimeCover",
                            ("rewritten", optimized), ("factored-sat-sop", factoredSatSop), ("sat-sop", satSop));
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

                // Cooperative deadline check between phases: NormalFormConverter runs a
                // synchronous distribution that does not poll the token itself, so bound it
                // at the phase boundary (its own distribution-step cap limits the interior).
                token.ThrowIfCancellationRequested();

                var converter = new NormalFormConverter();
                // Normal forms can blow up exponentially; TooLarge (displayed as "-")
                // marks a form that was abandoned
                if (computeEquivalentCnf)
                    try
                    {
                        cnfText = Transformations.SubsumeCnf(converter.ConvertToCNF(optimized)).ToString();
                    }
                    catch (NormalFormTooLargeException)
                    {
                        (cnfText, cnfStatus) = ("-", ComputationStatus.TooLarge);
                        trace?.Add(OptimizationTraceCategory.Fallback, "EquivalentCnf",
                            "distributive CNF conversion exceeded its size cap — reported as TooLarge " +
                            "instead of an unbounded blow-up (use --cnf-mode=tseitin for a linear CNF)",
                            new Dictionary<string, string> { ["cnfStatus"] = "TooLarge" });
                    }

                if (options.ComputeDnf && dnfText.Length == 0)
                    try
                    {
                        // Beyond the exact/SAT zones the distributed DNF is usually far
                        // from minimal; the Espresso-style cube-list pass shrinks it
                        // without any 2^n structure (sound by construction)
                        var dnf = Transformations.SubsumeDnf(converter.ConvertToDNF(optimized));
                        dnf = Transformations.MinimizeDnfHeuristic(dnf,
                            cancellationToken: token);
                        dnfText = dnf.ToString();
                    }
                    catch (NormalFormTooLargeException)
                    {
                        (dnfText, dnfStatus) = ("-", ComputationStatus.TooLarge);
                        trace?.Add(OptimizationTraceCategory.Fallback, "Dnf",
                            "distributive DNF conversion exceeded its size cap — reported as TooLarge",
                            new Dictionary<string, string> { ["dnfStatus"] = "TooLarge" });
                    }
            }

            // Tseitin CNF is linear in expression size, so it applies uniformly at any scale
            token.ThrowIfCancellationRequested();
            if (options.ComputeCnf && options.CnfMode == CnfMode.Tseitin)
                cnfText = TseitinConverter.Convert(optimized).ToString();

            // Experimental opt-in: DAG-aware AIG rewriting as one more multi-level candidate.
            // Adopted only when it is verified equivalent AND strictly cheaper, so it can only
            // ever improve the result and never regress it. Off by default -> no effect at all.
            if (options.EnableAigRewriting)
            {
                var aigCandidate = TryAigRewrite(optimized, factory, options, token, out var aigReason);
                if (aigCandidate == null)
                    trace?.Add(OptimizationTraceCategory.Rejected, "AigRewrite", aigReason);

                if (aigCandidate != null)
                {
                    trace?.Add(OptimizationTraceCategory.Proof, "AigRewrite", aigReason);
                    var selected = SelectCheapestTraced(trace, "AigRewrite",
                        ("current", optimized), ("aig-rewritten", aigCandidate));
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

            token.ThrowIfCancellationRequested();
            var advancedForms = "";
            if (options.ComputeAdvancedForms &&
                variables.Count <= PerformanceValidator.MAX_PATTERN_RECOGNITION_VARIABLES)
                advancedForms = new PatternRecognizer().ReplacePatterns(optimized).ToString();

            var includeTruthTables =
                options.IncludeTruthTables && variables.Count <= PerformanceValidator.MAX_TRUTH_TABLE_VARIABLES;

            trace?.Add(OptimizationTraceCategory.Status, "Result",
                $"final minimality provenance: {minimizationStatus}",
                new Dictionary<string, string>
                {
                    ["minimizationStatus"] = minimizationStatus.ToString(),
                    ["cnfMinimizationStatus"] = cnfMinimizationStatus.ToString(),
                    ["cnfStatus"] = cnfStatus.ToString(),
                    ["dnfStatus"] = dnfStatus.ToString(),
                    ["literalsIn"] = AstMetrics.CountLiterals(ast).ToString(),
                    ["literalsOut"] = AstMetrics.CountLiterals(optimized).ToString()
                });

            token.ThrowIfCancellationRequested();
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
                CnfMinimizationStatus = cnfMinimizationStatus,
                Variables = variables,
                Metrics = metrics,
                Trace = trace?.Build(),
                OriginalTruthTable = includeTruthTables ? TruthTable.Generate(ast) : null,
                OptimizedTruthTable = includeTruthTables ? TruthTable.Generate(optimized) : null
            };

            if (options.IncludeDebugInfo && metrics != null)
                result.DebugInfo =
                    "=== Debug Information ===\n" +
                    $"Original AST:\n{AstVisualizer.VisualizeTree(ast)}\n" +
                    $"Optimized AST:\n{AstVisualizer.VisualizeTree(optimized)}\n" +
                    metrics;

            // Allocation delta captured as late as possible: it now covers result-artifact
            // construction (ToString of every form, truth tables, debug dump) as well as the
            // core pipeline — matching the "across the run" wording.
            if (metrics != null)
                metrics.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes;

            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested &&
                                                 !options.CancellationToken.IsCancellationRequested)
        {
            // The global processing deadline fired (not the caller's own cancellation):
            // surface it as the documented TimeoutException regardless of which phase was
            // running when the time ran out.
            throw new TimeoutException(
                $"Maximum processing time exceeded ({PerformanceValidator.MAX_PROCESSING_TIME_SECONDS} sec). " +
                "Processing aborted.");
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
    private static AstNode? TryAigRewrite(AstNode expression, FormulaFactory factory, OptimizationOptions options,
        CancellationToken cancellationToken, out string reason)
    {
        var aig = AndInverterGraph.FromAst(expression);
        if (aig.AndNodeCount > AigRewriteMaxAndNodes)
        {
            reason = $"AIG too large ({aig.AndNodeCount} AND nodes > {AigRewriteMaxAndNodes} limit)";
            return null;
        }

        var rewritten = aig.RewriteToFixpoint(AigRewriteMaxRounds, cancellationToken);
        if (rewritten.AndNodeCount >= aig.AndNodeCount)
        {
            reason = $"no structural gain ({aig.AndNodeCount} -> {rewritten.AndNodeCount} AND nodes)";
            return null; // no structural gain
        }

        var candidate = factory.Import(rewritten.ToAst(rewritten.Root));

        // Belt-and-suspenders: the rewrite is function-preserving by construction, but the
        // adopted candidate must independently pass the existing equivalence checker.
        var equivalent = EquivalenceChecker
            .Check(expression, candidate, options.Budget.SatConflictLimit, cancellationToken)
            .AreEquivalent;

        if (equivalent == true)
        {
            reason = $"AIG rewriting shrank the graph ({aig.AndNodeCount} -> {rewritten.AndNodeCount} " +
                     "AND nodes) and the candidate was proven equivalent";
            return candidate;
        }

        reason = equivalent == false
            ? "candidate refuted by the equivalence checker (rewrite bug) — discarded"
            : "equivalence unproven within the SAT budget — discarded rather than shipped unverified";
        return null;
    }

    /// <summary>Minterm indices where the expression is true; bit j = value of variables[j].</summary>
    private static HashSet<int> ComputeOnSet(AstNode ast, List<string> variables,
        CancellationToken cancellationToken = default)
    {
        var onSet = new HashSet<int>();
        var assignment = new Dictionary<string, bool>();
        var numRows = 1 << variables.Count;

        for (var minterm = 0; minterm < numRows; minterm++)
        {
            // Up to 2^12 rows: observe the shared deadline so a full ON-set build counts
            // against the global processing time like every other phase.
            if ((minterm & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (minterm & (1 << j)) != 0;

            if (TruthTable.Evaluate(ast, assignment))
                onSet.Add(minterm);
        }

        return onSet;
    }

    /// <summary>
    ///     <see cref="SelectCheapest" /> with diagnostics: records every candidate's cost and
    ///     whether it displaced the incumbent (the first entry) or lost to it, so a trace explains
    ///     not just which form won but what it was compared against. Selection semantics are
    ///     identical — the first candidate still wins ties.
    /// </summary>
    private static AstNode SelectCheapestTraced(OptimizationTraceRecorder? trace, string step,
        params (string Name, AstNode Node)[] candidates)
    {
        var selected = SelectCheapest(candidates.Select(c => c.Node).ToArray());
        if (trace == null) return selected;

        foreach (var (name, node) in candidates)
            trace.Add(OptimizationTraceCategory.Candidate, step, $"candidate '{name}' costed",
                new Dictionary<string, string>
                {
                    ["candidate"] = name,
                    ["literals"] = AstMetrics.CountLiterals(node).ToString(),
                    ["nodes"] = AstMetrics.CountNodes(node).ToString()
                });

        var incumbent = candidates[0];
        var winner = candidates.First(c => ReferenceEquals(c.Node, selected));
        if (ReferenceEquals(selected, incumbent.Node))
            trace.Add(OptimizationTraceCategory.Rejected, step,
                $"no candidate beat '{incumbent.Name}' on literals, then nodes — keeping it",
                new Dictionary<string, string> { ["kept"] = incumbent.Name });
        else
            trace.Add(OptimizationTraceCategory.Adopted, step,
                $"adopted '{winner.Name}': cheaper than '{incumbent.Name}' on literals, then nodes",
                new Dictionary<string, string>
                {
                    ["adopted"] = winner.Name,
                    ["replaced"] = incumbent.Name,
                    ["literalsBefore"] = AstMetrics.CountLiterals(incumbent.Node).ToString(),
                    ["literalsAfter"] = AstMetrics.CountLiterals(selected).ToString()
                });

        return selected;
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
