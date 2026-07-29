using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicalOptimizer;

namespace LogicalOptimizer.Benchmarks;

/// <summary>
///     Roadmap P0.2 — the reproducible, artifact-backed OUR side of the controlled
///     cross-library comparison. A single deterministic command runs the committed corpus
///     (<c>tools/comparison_corpus.txt</c>) through the four result sets the roadmap lists
///     and emits three small, human-readable artifacts under <c>doc/comparison/</c>:
///     <list type="bullet">
///         <item><c>our-results.json</c> — machine-readable raw OUR results;</item>
///         <item><c>summary.md</c> — a Markdown summary (competitor columns marked
///         <c>pending (run …)</c>, never fabricated);</item>
///         <item><c>manifest.json</c> — the environment manifest (dotnet, OS, CPU policy).</item>
///     </list>
///
///     The four result sets (roadmap §3):
///     <list type="number">
///         <item><b>Symbolic optimization</b> — input/output literal count, AST node count,
///         equivalence verdict, time, allocations.</item>
///         <item><b>Two-level minimization</b> — terms, literals, proof status, timeout.</item>
///         <item><b>SAT</b> — verdict, conflicts, time, proof availability (measured on the
///         equivalence miter <c>Original XOR Optimized</c>: UNSAT ⇒ equivalent, with a
///         DRAT proof).</item>
///         <item><b>BDD / d-DNNF</b> — compile time, node count, exact model count,
///         repeated-query time (the two engines' model counts must agree — an exact
///         correctness cross-check independent of any timing).</item>
///     </list>
///
///     Determinism: every non-timing field (sizes, counts, statuses, verdicts, model
///     counts) is deterministic given the corpus. Timing / allocation fields
///     (<c>…Ms…</c>, <c>allocatedBytes</c>) are machine-dependent and indicative; they are
///     the only fields that change run-to-run.
///
///     Run: <c>dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite</c>
///     Options: <c>--out &lt;dir&gt;</c> (default <c>doc/comparison</c>),
///     <c>--emit-sat-dimacs &lt;dir&gt;</c> (write the miter DIMACS files a SAT competitor
///     — CaDiCaL / Kissat — would consume), <c>--emit-function-dimacs &lt;dir&gt;</c> (write the
///     count-preserving per-function DIMACS a #SAT counter — d4 / c2d — consumes for a
///     head-to-head model-count comparison), <c>&lt;corpusPath&gt;</c>.
/// </summary>
internal static class CrossLibraryComparisonHarness
{
    private const int SchemaVersion = 1;
    private const int WarmupRuns = 1;
    private const int MeasuredRuns = 7;
    private const int CompileMeasuredRuns = 5;

    // Kept generous but bounded so a pathological compile is reported as budget-exceeded
    // rather than hanging the run (correctness of the reported number is never at stake:
    // an exceeded budget is recorded as a status, never as a fabricated count).
    private const int EngineNodeBudget = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static int Run(string[] args)
    {
        var outDir = OptionValue(args, "--out");
        var dimacsDir = OptionValue(args, "--emit-sat-dimacs");
        var functionDimacsDir = OptionValue(args, "--emit-function-dimacs");
        var corpusPath = PositionalCorpus(args) ?? FindCorpus();

        if (corpusPath is null || !File.Exists(corpusPath))
        {
            Console.Error.WriteLine(
                "Corpus not found. Expected tools/comparison_corpus.txt (pass an explicit path as an argument).");
            return 1;
        }

        var repoRoot = FindRepoRoot(corpusPath);
        outDir ??= Path.Combine(repoRoot, "doc", "comparison");
        Directory.CreateDirectory(outDir);

        var functions = ParseCorpus(corpusPath);
        if (functions.Count == 0)
        {
            Console.Error.WriteLine($"Corpus at '{corpusPath}' contained no functions.");
            return 1;
        }

        var corpus = new Corpus(
            ToRepoRelative(repoRoot, corpusPath),
            Sha256OfFile(corpusPath),
            functions.Count);

        Console.Error.WriteLine($"comparison-suite: {functions.Count} functions from {corpus.path} (sha256 {corpus.sha256[..12]}…)");

        var symbolic = new List<SymbolicRow>();
        var twoLevel = new List<TwoLevelRow>();
        var sat = new List<SatRow>();
        var bddDnnf = new List<BddDnnfRow>();

        foreach (var (zone, name, expression) in functions)
        {
            symbolic.Add(MeasureSymbolic(zone, name, expression));
            twoLevel.Add(MeasureTwoLevel(zone, name, expression));
            sat.Add(MeasureSat(zone, name, expression, dimacsDir));
            bddDnnf.Add(MeasureBddDnnf(zone, name, expression));
            if (functionDimacsDir is not null)
                EmitFunctionCnf(functionDimacsDir, name, expression);
            Console.Error.Write('.');
        }

        Console.Error.WriteLine();

        var results = new Results(SchemaVersion, corpus, symbolic, twoLevel, sat, bddDnnf);
        var manifest = BuildManifest(corpus);

        var jsonPath = Path.Combine(outDir, "our-results.json");
        var summaryPath = Path.Combine(outDir, "summary.md");
        var manifestPath = Path.Combine(outDir, "manifest.json");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(results, JsonOptions) + "\n");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + "\n");
        File.WriteAllText(summaryPath, BuildSummary(results, manifest));

        Console.Out.WriteLine($"Wrote {ToRepoRelative(repoRoot, jsonPath)}");
        Console.Out.WriteLine($"Wrote {ToRepoRelative(repoRoot, summaryPath)}");
        Console.Out.WriteLine($"Wrote {ToRepoRelative(repoRoot, manifestPath)}");
        if (dimacsDir is not null)
            Console.Out.WriteLine($"Wrote SAT miter DIMACS to {dimacsDir}");
        if (functionDimacsDir is not null)
            Console.Out.WriteLine($"Wrote per-function (count-preserving Tseitin) DIMACS to {functionDimacsDir}");

        var nonEquivalent = symbolic.Count(r => r.equivalence != "equivalent")
                            + twoLevel.Count(r => r.equivalence is not ("equivalent" or "n/a"));
        if (nonEquivalent > 0)
        {
            Console.Error.WriteLine(
                $"WARNING: {nonEquivalent} OUR row(s) are not proven equivalent — correctness regression, investigate before publishing.");
            return 2;
        }

        Console.Out.WriteLine("All OUR optimization/minimization rows carry an equivalence verdict = equivalent.");
        return 0;
    }

    // --- Result set 1: symbolic optimization ---------------------------------------
    private static SymbolicRow MeasureSymbolic(string zone, string name, string expression)
    {
        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions
        { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

        var input = Parse(expression);
        var inputLiterals = AstMetrics.CountLiterals(input);
        var inputNodes = AstMetrics.CountNodes(input);

        for (var i = 0; i < WarmupRuns; i++) optimizer.OptimizeExpression(expression, options);

        var samples = new double[MeasuredRuns];
        long allocated = 0;
        OptimizationResult result = null!;
        for (var i = 0; i < MeasuredRuns; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            result = optimizer.OptimizeExpression(expression, options);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var optimized = Parse(result.Optimized);
        var equivalence = EquivalenceLabel(EquivalenceChecker.Check(expression, result.Optimized));

        return new SymbolicRow(
            zone, name, result.Variables.Count,
            inputLiterals, AstMetrics.CountLiterals(optimized),
            inputNodes, AstMetrics.CountNodes(optimized),
            StatusLabel(result.MinimizationStatus), equivalence,
            Median(samples), allocated);
    }

    // --- Result set 2: two-level (SOP) minimization --------------------------------
    private static TwoLevelRow MeasureTwoLevel(string zone, string name, string expression)
    {
        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions
        { ComputeCnf = false, ComputeDnf = true, ComputeAdvancedForms = false };

        var vars = Parse(expression).GetVariables().Count;

        for (var i = 0; i < WarmupRuns; i++) optimizer.OptimizeExpression(expression, options);

        var samples = new double[MeasuredRuns];
        OptimizationResult result = null!;
        for (var i = 0; i < MeasuredRuns; i++)
        {
            var sw = Stopwatch.StartNew();
            result = optimizer.OptimizeExpression(expression, options);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        var dnf = result.DNF;
        var abandoned = dnf is "" or "-";
        int terms;
        int literals;
        string equivalence;
        if (abandoned)
        {
            terms = 0;
            literals = 0;
            equivalence = "n/a";
        }
        else
        {
            var node = Parse(dnf);
            terms = CountTerms(node);
            literals = AstMetrics.CountLiterals(node);
            equivalence = EquivalenceLabel(EquivalenceChecker.Check(expression, dnf));
        }

        return new TwoLevelRow(
            zone, name, vars, terms, literals,
            StatusLabel(result.MinimizationStatus), equivalence, abandoned,
            Median(samples));
    }

    // --- Result set 3: SAT (equivalence miter: Original XOR Optimized) --------------
    private static SatRow MeasureSat(string zone, string name, string expression, string? dimacsDir)
    {
        var optimizer = new BooleanExpressionOptimizer();
        var result = optimizer.OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false });

        // Miter: SAT ⇔ the two forms differ somewhere; UNSAT ⇔ they are equivalent.
        var original = expression;
        var optimized = result.Optimized;
        var miter = $"({original}) & !({optimized}) | !({original}) & ({optimized})";
        var cnf = optimizer.ToEquisatisfiableCnf(miter);
        var inputVars = Parse(expression).GetVariables().Count;

        if (dimacsDir is not null)
        {
            Directory.CreateDirectory(dimacsDir);
            File.WriteAllText(Path.Combine(dimacsDir, $"{name}.miter.cnf"), cnf.ToDimacs());
        }

        for (var i = 0; i < WarmupRuns; i++)
        {
            var warm = SatSolver.FromCnf(cnf);
            warm.Solve();
        }

        var samples = new double[MeasuredRuns];
        SatResult verdict = SatResult.Unknown;
        var conflicts = 0;
        var proofLines = 0;
        var proofAvailable = false;
        for (var i = 0; i < MeasuredRuns; i++)
        {
            var solver = SatSolver.FromCnf(cnf);
            solver.EnableProofLogging();
            var sw = Stopwatch.StartNew();
            verdict = solver.Solve();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
            conflicts = solver.ConflictCount;
            if (verdict == SatResult.Unsatisfiable)
            {
                var drat = solver.ToDrat();
                proofAvailable = !string.IsNullOrEmpty(drat);
                proofLines = proofAvailable ? CountLines(drat) : 0;
            }
        }

        return new SatRow(
            zone, name, inputVars, cnf.TotalVariableCount, cnf.Clauses.Count,
            VerdictLabel(verdict), conflicts, proofAvailable, proofLines, Median(samples));
    }

    // --- Result set 4: BDD / d-DNNF ------------------------------------------------
    private static BddDnnfRow MeasureBddDnnf(string zone, string name, string expression)
    {
        var ast = Parse(expression);
        var vars = ast.GetVariables().Count;

        var (bddStatus, bddNodes, bddCount, bddCompile, bddQuery) = MeasureBdd(ast);
        var (dnnfStatus, dnnfNodes, dnnfCount, dnnfCompile, dnnfQuery) = MeasureDnnf(ast);

        var agree = bddCount is not null && dnnfCount is not null && bddCount == dnnfCount;
        var modelCount = (bddCount ?? dnnfCount)?.ToString(CultureInfo.InvariantCulture);

        return new BddDnnfRow(
            zone, name, vars,
            bddStatus, bddNodes, dnnfStatus, dnnfNodes,
            modelCount, agree,
            bddCompile, dnnfCompile, bddQuery, dnnfQuery);
    }

    private static (string status, int? nodes, BigInteger? count, double compileMs, double queryMs) MeasureBdd(AstNode ast)
    {
        try
        {
            BinaryDecisionDiagram bdd = null!;
            var compile = new double[CompileMeasuredRuns];
            for (var i = 0; i < CompileMeasuredRuns; i++)
            {
                var sw = Stopwatch.StartNew();
                bdd = BinaryDecisionDiagram.Build(ast, EngineNodeBudget);
                sw.Stop();
                compile[i] = sw.Elapsed.TotalMilliseconds;
            }

            var count = bdd.CountSatisfyingAssignments();
            var query = new double[CompileMeasuredRuns];
            for (var i = 0; i < CompileMeasuredRuns; i++)
            {
                var sw = Stopwatch.StartNew();
                bdd.CountSatisfyingAssignments();
                sw.Stop();
                query[i] = sw.Elapsed.TotalMilliseconds;
            }

            return ("ok", bdd.NodeCount, count, Median(compile), Median(query));
        }
        catch (Exception ex) when (ex is NodeBudgetExceededException or ComputationBudgetExceededException)
        {
            return ("budget-exceeded", null, null, 0, 0);
        }
    }

    private static (string status, int? nodes, BigInteger? count, double compileMs, double queryMs) MeasureDnnf(AstNode ast)
    {
        try
        {
            DnnfCircuit circuit = null!;
            var compile = new double[CompileMeasuredRuns];
            for (var i = 0; i < CompileMeasuredRuns; i++)
            {
                var sw = Stopwatch.StartNew();
                circuit = KnowledgeCompilation.CompileToDnnf(ast, EngineNodeBudget);
                sw.Stop();
                compile[i] = sw.Elapsed.TotalMilliseconds;
            }

            var count = circuit.CountModels();
            var query = new double[CompileMeasuredRuns];
            for (var i = 0; i < CompileMeasuredRuns; i++)
            {
                var sw = Stopwatch.StartNew();
                circuit.CountModels();
                sw.Stop();
                query[i] = sw.Elapsed.TotalMilliseconds;
            }

            return ("ok", circuit.NodeCount, count, Median(compile), Median(query));
        }
        catch (Exception ex) when (ex is NodeBudgetExceededException or ComputationBudgetExceededException)
        {
            return ("budget-exceeded", null, null, 0, 0);
        }
    }

    // --- Markdown summary ----------------------------------------------------------
    private static string BuildSummary(Results r, Manifest m)
    {
        const string sympyCmd = "python tools/compare_sympy_pyeda.py";
        const string satCmd = "tools/comparison/run_sat_competitors.sh";
        const string mcCmd = "tools/comparison/run_modelcount_competitors.sh";

        var sb = new StringBuilder();
        sb.AppendLine("# Cross-library comparison — OUR-side artifacts (roadmap P0.2)");
        sb.AppendLine();
        sb.AppendLine("Generated by `LogicalOptimizer.Benchmarks -- comparison-suite`. Every OUR number");
        sb.AppendLine("below comes from the committed artifact [`our-results.json`](our-results.json);");
        sb.AppendLine("the run environment is in [`manifest.json`](manifest.json). Competitor columns are");
        sb.AppendLine("left `pending (run …)` — they are **never** filled in here from memory; run the");
        sb.AppendLine("named command where the external tool is installed and merge (see");
        sb.AppendLine("[`doc/COMPARISON_METHODOLOGY.md`](../COMPARISON_METHODOLOGY.md) and");
        sb.AppendLine("[`tools/comparison/`](../../tools/comparison/)).");
        sb.AppendLine();
        sb.AppendLine("**Reproduce (OUR side, single command):**");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"Corpus: `{r.corpus.path}` (sha256 `{r.corpus.sha256}`, {r.corpus.functionCount} functions). ");
        sb.AppendLine($"Environment: {m.runtimeIdentifier}, {m.osDescription}, {m.architecture}. ");
        sb.AppendLine("Timings are machine-dependent and indicative (median of 7 after 1 warm-up); ");
        sb.AppendLine("they are **never** asserted. Sizes, counts, statuses, verdicts and model counts ");
        sb.AppendLine("are deterministic given the corpus.");
        sb.AppendLine();

        // Table 1: symbolic optimization
        sb.AppendLine("## 1. Symbolic optimization");
        sb.AppendLine();
        sb.AppendLine("OUR default multi-level (factored) output. `equivalence` is computed independently");
        sb.AppendLine("of timing by `EquivalenceChecker` (truth-table ≤ 12 vars, SAT-miter beyond).");
        sb.AppendLine();
        sb.AppendLine("| Zone | Function | Vars | In lits | Out lits | In nodes | Out nodes | Status | Equivalence | Time (ms) | Alloc (B) | SymPy lits | PyEDA lits |");
        sb.AppendLine("|------|----------|-----:|--------:|---------:|---------:|----------:|--------|-------------|----------:|----------:|:----------:|:----------:|");
        foreach (var x in r.symbolicOptimization)
            sb.AppendLine(
                $"| {x.zone} | {x.name} | {x.vars} | {x.inputLiterals} | {x.outputLiterals} | {x.inputNodes} | {x.outputNodes} | {x.minimizationStatus} | {x.equivalence} | {F(x.timeMsMedian)} | {x.allocatedBytes} | `pending` | `pending` |");
        sb.AppendLine();
        sb.AppendLine($"Competitor columns: `pending (run {sympyCmd})`.");
        sb.AppendLine();

        // Table 2: two-level minimization
        sb.AppendLine("## 2. Two-level (SOP) minimization");
        sb.AppendLine();
        sb.AppendLine("OUR `result.DNF` (exact QM / SAT-cover / espresso-lite cover — the same two-level");
        sb.AppendLine("form SymPy `simplify_logic(form=\"dnf\")` and PyEDA `espresso_tts` emit). `abandoned`");
        sb.AppendLine("marks a DNF the converter declined to expand (reported, never guessed).");
        sb.AppendLine();
        sb.AppendLine("| Zone | Function | Vars | Terms | Literals | Proof status | Equivalence | Abandoned | Time (ms) | SymPy lits | PyEDA lits |");
        sb.AppendLine("|------|----------|-----:|------:|---------:|--------------|-------------|:---------:|----------:|:----------:|:----------:|");
        foreach (var x in r.twoLevelMinimization)
            sb.AppendLine(
                $"| {x.zone} | {x.name} | {x.vars} | {x.terms} | {x.literals} | {x.minimizationStatus} | {x.equivalence} | {(x.abandoned ? "yes" : "no")} | {F(x.timeMsMedian)} | `pending` | `pending` |");
        sb.AppendLine();
        sb.AppendLine($"Competitor columns: `pending (run {sympyCmd})`.");
        sb.AppendLine();

        // Table 3: SAT
        sb.AppendLine("## 3. SAT (equivalence miter: `Original XOR Optimized`)");
        sb.AppendLine();
        sb.AppendLine("The SAT instance is the miter of the original vs the optimized form: **UNSAT ⇒ the");
        sb.AppendLine("optimization is equivalence-preserving**, and a DRAT proof is emitted (checkable by");
        sb.AppendLine("drat-trim). This doubles as SAT-based correctness that is independent of the");
        sb.AppendLine("optimizer's own timing. Regenerate the DIMACS a SAT competitor consumes with");
        sb.AppendLine("`comparison-suite --emit-sat-dimacs <dir>`.");
        sb.AppendLine();
        sb.AppendLine("| Zone | Function | Miter vars | Clauses | Verdict | Conflicts | Proof | Proof lines | Time (ms) | CaDiCaL | Kissat |");
        sb.AppendLine("|------|----------|-----------:|--------:|:-------:|----------:|:-----:|------------:|----------:|:-------:|:------:|");
        foreach (var x in r.sat)
            sb.AppendLine(
                $"| {x.zone} | {x.name} | {x.miterVars} | {x.miterClauses} | {x.verdict} | {x.conflicts} | {(x.proofAvailable ? "yes" : "no")} | {x.proofLines} | {F(x.satSolveMsMedian)} | `pending` | `pending` |");
        sb.AppendLine();
        sb.AppendLine($"Competitor columns: `pending (run {satCmd})`.");
        sb.AppendLine();

        // Table 4: BDD / d-DNNF
        sb.AppendLine("## 4. BDD / d-DNNF");
        sb.AppendLine();
        sb.AppendLine("Compile time, node count, exact model count and repeated-query time for both engines.");
        sb.AppendLine("**`agree`** is the exact correctness cross-check: the independent BDD and d-DNNF");
        sb.AppendLine("model counts must be identical (both are exact #SAT), independent of any timing.");
        sb.AppendLine();
        sb.AppendLine("| Zone | Function | Vars | BDD nodes | d-DNNF nodes | Model count | Agree | BDD compile (ms) | d-DNNF compile (ms) | BDD query (ms) | d-DNNF query (ms) | LogicNG BDD | c2d/d4 #SAT |");
        sb.AppendLine("|------|----------|-----:|----------:|-------------:|------------:|:-----:|-----------------:|--------------------:|---------------:|------------------:|:-----------:|:-----------:|");
        foreach (var x in r.bddDnnf)
            sb.AppendLine(
                $"| {x.zone} | {x.name} | {x.vars} | {NodeCell(x.bddStatus, x.bddNodes)} | {NodeCell(x.dnnfStatus, x.dnnfNodes)} | {x.modelCount ?? "-"} | {(x.modelCountsAgree ? "yes" : "no")} | {F(x.bddCompileMsMedian)} | {F(x.dnnfCompileMsMedian)} | {F(x.bddQueryMsMedian)} | {F(x.dnnfQueryMsMedian)} | `pending` | `pending` |");
        sb.AppendLine();
        sb.AppendLine($"Competitor columns: `pending (run {mcCmd})`.");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("_All four tables share one committed corpus and one run. Competitor cells stay");
        sb.AppendLine("`pending` until the external tool is run on the same corpus; a competitor timeout is");
        sb.AppendLine("recorded as `timeout`, never as a failure (see the methodology doc)._");
        return sb.ToString();
    }

    private static Manifest BuildManifest(Corpus corpus)
    {
        return new Manifest(
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            "LogicalOptimizer.Benchmarks -- comparison-suite",
            Environment.Version.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            "single-runner, single-thread; median-of-7 after 1 warm-up; wall-clock is indicative and machine-dependent (never asserted)",
            corpus);
    }

    // --- helpers -------------------------------------------------------------------
    private static readonly string[] ValueOptions = { "--out", "--emit-sat-dimacs", "--emit-function-dimacs" };

    /// <summary>
    ///     Emit the per-function CNF a #SAT counter (d4 / c2d) consumes for a head-to-head
    ///     comparison against the BDD / d-DNNF <c>modelCount</c>. Unlike the equivalence miter
    ///     (all UNSAT ⇒ count 0), this is the function's own CNF. It is the FULL Tseitin encoding
    ///     (biconditional per gate), so every auxiliary variable is functionally determined by the
    ///     inputs and the model count over all DIMACS variables equals the input-variable model
    ///     count — i.e. the counter's result is directly comparable to <c>modelCount</c>.
    /// </summary>
    private static void EmitFunctionCnf(string dir, string name, string expression)
    {
        Directory.CreateDirectory(dir);
        var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf(expression);
        File.WriteAllText(Path.Combine(dir, $"{name}.cnf"), cnf.ToDimacs());
    }

    private static string OptionValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null!;
    }

    /// <summary>
    ///     First positional (non-flag) token after the verb, skipping option flags AND the
    ///     values they consume — so `--out &lt;dir&gt;` is not mistaken for the corpus path.
    /// </summary>
    private static string? PositionalCorpus(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                if (ValueOptions.Contains(token, StringComparer.OrdinalIgnoreCase)) i++; // skip its value
                continue;
            }

            return token;
        }

        return null;
    }

    private static double Median(double[] samples)
    {
        var copy = (double[])samples.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static string F(double ms)
    {
        return ms.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string NodeCell(string status, int? nodes)
    {
        return status == "ok" ? nodes!.Value.ToString(CultureInfo.InvariantCulture) : "`" + status + "`";
    }

    private static int CountTerms(AstNode dnf)
    {
        return dnf is OrNode or ? or.Operands.Count : 1;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 0;
        foreach (var ch in text)
            if (ch == '\n')
                count++;
        if (text[^1] != '\n') count++;
        return count;
    }

    private static string StatusLabel(MinimizationStatus status)
    {
        return status switch
        {
            MinimizationStatus.MinimalProven => "MinimalProven",
            MinimizationStatus.BudgetExceeded => "BudgetExceeded",
            _ => "Heuristic"
        };
    }

    private static string EquivalenceLabel(EquivalenceCheckResult result)
    {
        return result.AreEquivalent switch
        {
            true => "equivalent",
            false => "NOT-equivalent",
            _ => "unknown"
        };
    }

    private static string VerdictLabel(SatResult verdict)
    {
        return verdict switch
        {
            SatResult.Satisfiable => "sat",
            SatResult.Unsatisfiable => "unsat",
            _ => "unknown"
        };
    }

    private static List<(string Zone, string Name, string Expression)> ParseCorpus(string path)
    {
        var functions = new List<(string, string, string)>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', 3);
            if (parts.Length != 3) continue;
            functions.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return functions;
    }

    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static string Sha256OfFile(string path)
    {
        using var sha = SHA256.Create();
        // Hash the normalized (LF) text so the digest is identical regardless of the
        // checkout's line-ending policy.
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FindRepoRoot(string corpusPath)
    {
        // corpus lives at <root>/tools/comparison_corpus.txt
        var toolsDir = Path.GetDirectoryName(Path.GetFullPath(corpusPath))!;
        return Path.GetDirectoryName(toolsDir)!;
    }

    private static string ToRepoRelative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string? FindCorpus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "comparison_corpus.txt");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    // --- serializable model (property order == JSON order; camelCase names as written) ---
    private sealed record Corpus(string path, string sha256, int functionCount);

    private sealed record SymbolicRow(
        string zone, string name, int vars,
        int inputLiterals, int outputLiterals, int inputNodes, int outputNodes,
        string minimizationStatus, string equivalence,
        double timeMsMedian, long allocatedBytes);

    private sealed record TwoLevelRow(
        string zone, string name, int vars,
        int terms, int literals, string minimizationStatus, string equivalence, bool abandoned,
        double timeMsMedian);

    private sealed record SatRow(
        string zone, string name, int inputVars, int miterVars, int miterClauses,
        string verdict, int conflicts, bool proofAvailable, int proofLines,
        double satSolveMsMedian);

    private sealed record BddDnnfRow(
        string zone, string name, int vars,
        string bddStatus, int? bddNodes, string dnnfStatus, int? dnnfNodes,
        string? modelCount, bool modelCountsAgree,
        double bddCompileMsMedian, double dnnfCompileMsMedian,
        double bddQueryMsMedian, double dnnfQueryMsMedian);

    private sealed record Results(
        int schemaVersion, Corpus corpus,
        IReadOnlyList<SymbolicRow> symbolicOptimization,
        IReadOnlyList<TwoLevelRow> twoLevelMinimization,
        IReadOnlyList<SatRow> sat,
        IReadOnlyList<BddDnnfRow> bddDnnf);

    private sealed record Manifest(
        string generatedAtUtc, string tool, string dotnetVersion, string runtimeIdentifier,
        string osDescription, string architecture, int processorCount, string cpuPolicy,
        Corpus corpus);
}
