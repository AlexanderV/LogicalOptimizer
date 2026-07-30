using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests.Documentation;

/// <summary>
///     Every code snippet shown in the README and the docs-site articles is mirrored here as a
///     real, executed test that asserts the exact output the documentation shows. This makes the
///     documented examples both compile-verified and output-verified, and keeps them from drifting:
///     if a documented value ever changes, the matching assertion below fails in the normal gate.
///     When editing a doc example, update the twin test here (and vice versa).
/// </summary>
public class DocExamplesTests
{
    private static AstNode Parse(string expression)
    {
        return new FormulaFactory().Parse(expression);
    }

    // ----- Formula construction: FormulaFactory, AST, AstFormatter, AstMetrics -----

    [Fact]
    public void FormulaFactory_ParsesToCanonicalOrder()
    {
        var f = new FormulaFactory();
        Assert.Equal("a & b & c", f.Parse("c & a & b").ToString());
    }

    [Fact]
    public void FormulaFactory_FoldsDegenerateFormulaToConstantAtParseTime()
    {
        Assert.Equal("1", new FormulaFactory().Parse("a | !a").ToString());
    }

    [Fact]
    public void FormulaFactory_InternsStructurallyEqualTrees()
    {
        var f = new FormulaFactory();
        var parsed = f.Parse("c & a & b");
        var built = f.And(f.Variable("a"), f.Variable("b"), f.Variable("c"));
        Assert.True(ReferenceEquals(parsed, built));
        Assert.Equal(3, ((AndNode)parsed).Operands.Count); // n-ary, flattened
    }

    [Fact]
    public void AstFormatter_RendersPrecedenceParentheses()
    {
        Assert.Equal("c | !(a & b)", AstFormatter.Format(Parse("!(a & b) | c")));
    }

    [Fact]
    public void AstMetrics_CountsNodesLiteralsOperatorsDepth()
    {
        var ast = Parse("a & (b | c)");
        Assert.Equal(5, AstMetrics.CountNodes(ast));
        Assert.Equal(3, AstMetrics.CountLiterals(ast));
        Assert.Equal(2, AstMetrics.CountOperators(ast));
        Assert.Equal(3, AstMetrics.GetDepth(ast));
    }

    [Fact]
    public void FormulaFactory_TryParse_ReportsStructuredDiagnostics()
    {
        var f = new FormulaFactory();

        Assert.True(f.TryParse("a & b", out var formula, out _));
        Assert.Equal("a & b", formula!.ToString());

        Assert.False(f.TryParse("a & & b", out _, out var diagnostic));
        Assert.Equal(ParseErrorCode.UnexpectedToken, diagnostic!.Code);
        Assert.Equal(4, diagnostic.Position);
    }

    // ----- Optimizer, options, result, quality analyzer -----

    [Fact]
    public void Optimizer_FactorsCommonTerm()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");
        Assert.Equal("a & (b | c)", result.Optimized);
        Assert.Equal("a & (b | c)", result.CNF);
        Assert.Equal("a & b | a & c", result.DNF);
        Assert.Equal(new List<string> { "a", "b", "c" }, result.Variables);
        Assert.Equal(MinimizationStatus.MinimalProven, result.MinimizationStatus);
        Assert.True(result.IsEquivalent());
    }

    [Fact]
    public void Optimizer_DistributesOverIntersectionOfClauses()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("(a | b) & (a | c)");
        Assert.Equal("a | b & c", result.Optimized);
    }

    [Fact]
    public void Optimizer_AppliesDeMorgan()
    {
        Assert.Equal("!a | !b", new BooleanExpressionOptimizer().OptimizeExpression("!(a & b)").Optimized);
    }

    [Fact]
    public void Optimizer_AppliesConsensus()
    {
        Assert.Equal("a & b | c & !a",
            new BooleanExpressionOptimizer().OptimizeExpression("a & b | !a & c | b & c").Optimized);
    }

    [Fact]
    public void Optimizer_SimplifiesConstants()
    {
        Assert.Equal("a", new BooleanExpressionOptimizer().OptimizeExpression("a & 1 | b & 0").Optimized);
    }

    [Fact]
    public void OptimizationOptions_AigRewritingIsOnByDefaultInV3()
    {
        Assert.True(OptimizationOptions.Default.EnableAigRewriting);
        Assert.True(OptimizationOptions.Everything.EnableAigRewriting);
        Assert.True(new OptimizationOptions().EnableAigRewriting);
    }

    [Fact]
    public void OptimizationOptions_DisablingAigRewritingStaysEquivalent()
    {
        var optimizer = new BooleanExpressionOptimizer();
        const string expression = "(a & b) | (a & c) | (a & d)";

        var withAig = optimizer.OptimizeExpression(expression,
            new OptimizationOptions { EnableAigRewriting = true });
        var withoutAig = optimizer.OptimizeExpression(expression,
            new OptimizationOptions { EnableAigRewriting = false });

        // Both paths are verified equivalent to the input; the AIG candidate is adopted only
        // when it is strictly cheaper, so turning the flag off never produces a worse result.
        Assert.True(TruthTable.AreEquivalent(expression, withAig.Optimized));
        Assert.True(TruthTable.AreEquivalent(expression, withoutAig.Optimized));
        Assert.Equal("a & (b | c | d)", withoutAig.Optimized);
    }

    [Fact]
    public void OptimizationOptions_EverythingComputesAdvancedAndTruthTables()
    {
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & !b | !a & b", OptimizationOptions.Everything);
        Assert.Equal("a XOR b", result.Advanced);
        Assert.NotNull(result.OptimizedTruthTable);
        Assert.NotNull(result.OriginalTruthTable);
    }

    [Fact]
    public void OptimizationResult_ExposesMetrics()
    {
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & b | a & c", includeMetrics: true);
        Assert.NotNull(result.Metrics);
        Assert.True(result.Metrics!.OriginalNodes >= result.Metrics.OptimizedNodes);
    }

    [Fact]
    public void OptimizationTrace_ExplainsEngineChoiceAndProofPath()
    {
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & b | a & c", new OptimizationOptions { IncludeTrace = true });

        var zone = result.Trace!.Entries.Single(e => e.Step == "ZoneSelection");
        Assert.Equal("exact-qm", zone.Data["engine"]);

        var guard = result.Trace.Entries.Single(e => e.Step == "SoundnessGuard");
        Assert.Equal("truth-table", guard.Data["method"]);
    }

    [Fact]
    public void OptimizationQualityAnalyzer_ProducesReportAndMetrics()
    {
        var result = new BooleanExpressionOptimizer()
            .OptimizeExpression("a & b | a & c", new OptimizationOptions { IncludeMetrics = true });

        var metrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);
        Assert.Equal(3, metrics.LiteralCount);
        Assert.Equal(2, metrics.OperatorCount);

        var report = OptimizationQualityAnalyzer.GenerateQualityReport(result);
        Assert.Contains("OPTIMIZATION QUALITY REPORT", report);
    }

    // ----- Normal forms & transformations -----

    [Fact]
    public void Transformations_ToAlgebraicNormalForm_Xor()
    {
        Assert.Equal("a XOR b", Transformations.ToAlgebraicNormalForm(Parse("a & !b | !a & b")).ToString());
    }

    [Fact]
    public void Transformations_ToAlgebraicNormalForm_AndAndOr()
    {
        Assert.Equal("a & b", Transformations.ToAlgebraicNormalForm(Parse("a & b")).ToString());
        // a | b == (a XOR b) XOR (a & b) as a Zhegalkin (Reed-Muller) polynomial.
        var anfOr = Transformations.ToAlgebraicNormalForm(Parse("a | b"));
        Assert.Equal("(a XOR b) XOR (a & b)", anfOr.ToString());
        Assert.True(TruthTable.AreEquivalent(Parse("a | b"), anfOr));
    }

    [Fact]
    public void Transformations_SubsumeDnfAndCnf()
    {
        Assert.Equal("a", Transformations.SubsumeDnf(Parse("a | a & b")).ToString());
        Assert.Equal("a", Transformations.SubsumeCnf(Parse("a & (a | b)")).ToString());
    }

    [Fact]
    public void Transformations_MinimizeDnfHeuristic_ShrinksCover()
    {
        var input = Parse("a & b | a & !b | !a & b");
        var minimized = Transformations.MinimizeDnfHeuristic(input);
        Assert.True(TruthTable.AreEquivalent(input, minimized));
        Assert.True(AstMetrics.CountLiterals(minimized) <= AstMetrics.CountLiterals(input));
        // The 3-cube cover collapses to a | b (2 literals).
        Assert.True(TruthTable.AreEquivalent(Parse("a | b"), minimized));
    }

    [Fact]
    public void ToEquisatisfiableCnf_ProducesLinearTseitinCnf()
    {
        var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf("(a | b) & (b | c)");
        Assert.Equal(new[] { "a", "b", "c" }, cnf.InputVariables);
        Assert.Equal(6, cnf.TotalVariableCount);
        Assert.Equal(3, cnf.AuxiliaryVariableCount);
        Assert.Equal(10, cnf.Clauses.Count);
        Assert.Contains("p cnf 6 10", cnf.ToDimacs());
        // The clause set is equisatisfiable with the original formula: feeding it to the SAT
        // solver reports SATISFIABLE, and the input-variable names round-trip.
        Assert.Equal(SatResult.Satisfiable, SatSolver.FromCnf(cnf).Solve());
        Assert.Equal("a", cnf.VariableName(1));
    }

    [Fact]
    public void TruthTable_GenerateAndQuery()
    {
        var table = TruthTable.Generate("a & b");
        Assert.Equal("0001", table.GetResultsString());
        Assert.False(table.IsTautology());
        Assert.True(table.IsSatisfiable());
        Assert.False(table.IsContradiction());

        Assert.True(TruthTable.Generate("a | !a").IsTautology());
        Assert.True(TruthTable.AreEquivalent("a & b | a & c", "a & (b | c)"));
    }

    // ----- Minimization package -----

    [Fact]
    public void TruthTableMinimizer_MinimalSopAndPos_Majority()
    {
        var variables = new[] { "a", "b", "c" };
        var onSet = new[] { 3, 5, 6, 7 }; // majority of 3 inputs

        Assert.Equal("a & b | a & c | b & c",
            TruthTableMinimizer.MinimalSop(variables, onSet).ToString());
        Assert.Equal("(b | c) & (a | c) & (a | b)",
            TruthTableMinimizer.MinimalPos(variables, onSet).ToString());

        var (expression, provenMinimal) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);
        Assert.True(provenMinimal);
        Assert.Equal("a & b | a & c | b & c", expression.ToString());
    }

    [Fact]
    public void TruthTableMinimizer_DontCaresShrinkTheCover()
    {
        // onset {1}, don't-care {3}: the minimal cover is just "a".
        var result = TruthTableMinimizer.MinimalSop(new[] { "a", "b" }, new[] { 1 }, new[] { 3 });
        Assert.Equal("a", result.ToString());
    }

    [Fact]
    public void CsvTruthTableParser_ParsesExpressionAndPartialTable()
    {
        Assert.Equal("(!a & b) | (a & !b)",
            CsvTruthTableParser.ParseCsvToExpression("a,b,Result\n0,0,0\n0,1,1\n1,0,1\n1,1,0"));

        // Rows absent from the CSV become don't-care minterms.
        var partial = CsvTruthTableParser.ParseCsvToPartialTable("a,b,Result\n0,0,0\n0,1,1\n1,1,1");
        Assert.Equal(new[] { "a", "b" }, partial.Variables);
        Assert.Equal(new[] { 2, 3 }, partial.OnSet.OrderBy(x => x));
        Assert.Equal(new[] { 1 }, partial.DontCareSet.OrderBy(x => x));
    }

    [Fact]
    public void CsvTruthTableParser_ParsesMultiOutputTable()
    {
        var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(
            "a,b,Sum,Carry\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1", new[] { "Sum", "Carry" });

        Assert.Equal(new[] { "a", "b" }, table.Variables);
        Assert.Equal(2, table.Outputs.Count);

        var sum = table.Outputs.Single(o => o.Name == "Sum");
        var carry = table.Outputs.Single(o => o.Name == "Carry");
        Assert.Equal(new[] { 1, 2 }, sum.OnSet.OrderBy(x => x));
        Assert.Equal(new[] { 3 }, carry.OnSet.OrderBy(x => x));
    }

    // ----- SAT stack -----

    [Fact]
    public void SatSolver_SolvesAndReadsModel()
    {
        var solver = new SatSolver(3);
        solver.AddClause(1, 2);   // a | b
        solver.AddClause(-1, 3);  // !a | c
        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        // The recovered assignment satisfies both clauses.
        Assert.True(solver.GetValue(1) || solver.GetValue(2));
        Assert.True(!solver.GetValue(1) || solver.GetValue(3));
    }

    [Fact]
    public void SatSolver_ReportsUnsatWithCore()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1);
        solver.AddClause(-1);
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void SatSolver_SolvesUnderAssumptions()
    {
        var solver = new SatSolver(2);
        solver.AddClause(1, 2); // a | b
        Assert.Equal(SatResult.Satisfiable, solver.Solve(new[] { -1 })); // assume !a
        Assert.True(solver.GetValue(2)); // then b must hold
    }

    [Fact]
    public void SatSolver_EmitsDratProofForUnsat()
    {
        var solver = new SatSolver(1);
        solver.EnableProofLogging();
        solver.AddClause(1);
        solver.AddClause(-1);
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
        Assert.Contains("0", solver.ToDrat()); // DRAT derives the empty clause
    }

    [Fact]
    public void SatSolver_FromTseitinCnf()
    {
        var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf("a & b");
        var solver = SatSolver.FromCnf(cnf);
        Assert.Equal(SatResult.Satisfiable, solver.Solve());
    }

    [Fact]
    public void CardinalityEncoder_AtMostAndExactly()
    {
        // AtMost(1) over {x1..x4} together with x1 and x2 forced true is UNSAT.
        var atMost = new CnfBuilder(4);
        CardinalityEncoder.AtMostK(atMost, new[] { 1, 2, 3, 4 }, 1);
        var solver = atMost.ToSolver();
        solver.AddClause(1);
        solver.AddClause(2);
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());

        // ExactlyK(2) over three variables is satisfiable.
        var exactly = new CnfBuilder(3);
        CardinalityEncoder.ExactlyK(exactly, new[] { 1, 2, 3 }, 2);
        Assert.Equal(SatResult.Satisfiable, exactly.ToSolver().Solve());
    }

    [Fact]
    public void PseudoBooleanEncoder_WeightedAtMost()
    {
        var builder = new CnfBuilder(3);
        PseudoBooleanEncoder.AtMost(builder, new[] { 1, 2, 3 }, new long[] { 2, 3, 4 }, 5);
        Assert.Equal(SatResult.Satisfiable, builder.ToSolver().Solve());
    }

    [Fact]
    public void EncodingPortfolio_SelectsAndReportsSize()
    {
        var builder = new CnfBuilder(10);
        var atMostOne = Enumerable.Range(1, 10).ToList();

        // Pick an encoding explicitly and read back its size.
        var stats = CardinalityEncoder.AtMostK(builder, atMostOne, 1, CardinalityEncoding.Product);
        Assert.Equal("29 clauses, 7 aux vars", stats.ToString());

        // Auto measures the applicable encodings and keeps the smallest (never larger than the default).
        var auto = CardinalityEncoder.AtMostK(new CnfBuilder(10), atMostOne, 1, CardinalityEncoding.Auto);
        var seq = CardinalityEncoder.AtMostK(new CnfBuilder(10), atMostOne, 1, CardinalityEncoding.SequentialCounter);
        Assert.True(auto.Cost <= seq.Cost);
    }

    [Fact]
    public void MaxSatSolver_FindsOptimalCost()
    {
        var solver = new MaxSatSolver(2);
        solver.AddHard(1, 2);   // a | b must hold
        solver.AddSoft(3, -1);  // prefer a = false (weight 3)
        solver.AddSoft(4, -2);  // prefer b = false (weight 4)

        var result = solver.Solve();
        Assert.Equal(MaxSatStatus.Optimal, result.Status);
        Assert.Equal(3, result.Cost); // violate the cheaper soft clause
        Assert.True(result.GetValue(1));
        Assert.False(result.GetValue(2));
    }

    // ----- BDD -----

    [Fact]
    public void Bdd_CountsAndEvaluatesAndFindsModels()
    {
        var bdd = BinaryDecisionDiagram.BuildWithBestOrder(Parse("a & b | c"));
        Assert.Equal(new BigInteger(5), bdd.CountSatisfyingAssignments());
        Assert.Equal(5, bdd.EnumerateSatisfyingAssignments().Count());
        Assert.True(bdd.Evaluate(new Dictionary<string, bool> { ["a"] = true, ["b"] = true, ["c"] = false }));

        var model = bdd.FindSatisfyingAssignment();
        Assert.True(bdd.Evaluate(model));
    }

    [Fact]
    public void Bdd_TautologyAndContradiction()
    {
        var tautology = BinaryDecisionDiagram.BuildWithBestOrder(
            new OrNode(new VariableNode("a"), new NotNode(new VariableNode("a"))));
        Assert.True(tautology.IsTautology());

        var contradiction = BinaryDecisionDiagram.BuildWithBestOrder(
            new AndNode(new VariableNode("a"), new NotNode(new VariableNode("a"))));
        Assert.True(contradiction.IsContradiction());
    }

    [Fact]
    public void Bdd_EquivalenceAndSifting()
    {
        Assert.True(BinaryDecisionDiagram.AreEquivalent(Parse("a & b | a & c"), Parse("a & (b | c)")));

        var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(Parse("a & b | c & d"));
        Assert.Equal(new BigInteger(7), sifted.CountSatisfyingAssignments());
    }

    // ----- DNNF knowledge compilation -----

    [Fact]
    public void Dnnf_CountsWeightsAndEnumerates()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("(a | b) & (b | c)"));

        Assert.True(circuit.IsSatisfiable);
        Assert.Equal(new BigInteger(5), circuit.CountModels());
        Assert.Equal(new[] { "a", "b", "c" }, circuit.Variables);
        Assert.True(circuit.NodeCount > 0);
        Assert.Equal(5, circuit.EnumerateModels().Count());

        var weighted = circuit.WeightedModelCount(new Dictionary<string, (double, double)>
        {
            ["a"] = (0.5, 0.5),
            ["b"] = (0.5, 0.5),
            ["c"] = (0.5, 0.5)
        });
        Assert.Equal(0.625, weighted, 9); // 5 models * 0.5^3
    }

    // ----- Analysis & equivalence -----

    [Fact]
    public void FormulaAnalysis_BackboneModelsAndSimplification()
    {
        var backbone = FormulaAnalysis.ComputeBackbone(Parse("a & (b | c)"));
        Assert.True(backbone.IsSatisfiable);
        Assert.Equal(new Dictionary<string, bool> { ["a"] = true }, backbone.ForcedVariables);

        Assert.Equal(3, FormulaAnalysis.EnumerateModels(Parse("a | b")).Count());

        var input = Parse("(a | b) & !a & (c | d)");
        var simplified = FormulaAnalysis.SimplifyWithBackbone(input);
        Assert.True(EquivalenceChecker.Check(input, simplified).AreEquivalent);
    }

    [Fact]
    public void EquivalenceChecker_ChecksAndReturnsCounterexample()
    {
        Assert.True(EquivalenceChecker.Check("a & b | a & c", "a & (b | c)").AreEquivalent);

        var result = EquivalenceChecker.Check("a & b", "a | b");
        Assert.False(result.AreEquivalent);
        Assert.NotNull(result.Counterexample);
        // The counterexample distinguishes the two formulas.
        Assert.NotEqual(
            TruthTable.Evaluate(Parse("a & b"), new Dictionary<string, bool>(result.Counterexample!)),
            TruthTable.Evaluate(Parse("a | b"), new Dictionary<string, bool>(result.Counterexample!)));
    }

    [Fact]
    public void EquivalenceChecker_CheckWithProof_EmitsDratForEquivalent()
    {
        var (result, _, drat) = EquivalenceChecker.CheckWithProof(Parse("a & b"), Parse("b & a"));
        Assert.True(result.AreEquivalent);
        Assert.NotNull(drat); // UNSAT miter -> externally checkable DRAT certificate
    }

    [Fact]
    public void EquivalenceChecker_BddAndHybridImplementations()
    {
        Assert.True(new BddEquivalenceChecker().Check(Parse("a & b"), Parse("b & a")).AreEquivalent);
        Assert.True(new HybridEquivalenceChecker().Check(Parse("a | b"), Parse("b | a")).AreEquivalent);
    }

    // ----- Exporters -----

    [Fact]
    public void BooleanExpressionExporter_AllFormats()
    {
        const string expression = "a & b | c";

        Assert.Contains("p cnf 3 2", BooleanExpressionExporter.ToDimacs(expression));
        Assert.Contains(".model my_circuit", BooleanExpressionExporter.ToBlif(expression, "my_circuit"));
        Assert.Contains("module my_module", BooleanExpressionExporter.ToVerilog(expression, "my_module"));
        Assert.Equal("c ∨ a ∧ b", BooleanExpressionExporter.ToMathematicalNotation(expression));
        Assert.Equal("c \\lor a \\land b", BooleanExpressionExporter.ToLatex(expression));
        Assert.Contains("1,1,1", BooleanExpressionExporter.TruthTableToCsv("a & b"));
    }

    [Fact]
    public void CSharpExpressionExporter_GeneratesCode()
    {
        var ast = Parse("a & b | c");
        Assert.Equal("(c || (a && b))", CSharpExpressionExporter.ToExpression(ast));
        Assert.Contains("=>", CSharpExpressionExporter.GenerateLambda(ast));
        Assert.Contains("public static bool", CSharpExpressionExporter.GenerateMethod(ast));
        Assert.Contains("class BooleanEvaluator", CSharpExpressionExporter.GenerateClass(ast));
    }

    // ----- CLI flags (locks the documented flag list in cli-usage.md) -----

    [Fact]
    public void Cli_RecognizesEveryDocumentedFlag()
    {
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--cnf", "a & b | c" }).CnfOnly);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--dnf", "(a | b) & c" }).DnfOnly);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--anf", "a & !b | !a & b" }).AnfOnly);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--advanced", "a & !b | !a & b" }).Advanced);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--truth-table", "a & b" }).TruthTableOnly);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--verbose", "a & b" }).Verbose);
        Assert.Equal(CnfMode.Tseitin,
            CommandLineProcessor.ParseArguments(new[] { "--cnf-mode=tseitin", "a & b" }).CnfMode);
        Assert.Equal(CliOutputFormat.Json,
            CommandLineProcessor.ParseArguments(new[] { "--format=json", "a & b" }).Format);
        Assert.Equal(CliOutputFormat.Json,
            CommandLineProcessor.ParseArguments(new[] { "--json", "a & b" }).Format);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--trace", "a & b" }).Trace);

        var multi = CommandLineProcessor.ParseArguments(new[] { "--outputs=Sum,Carry", "a,b,Sum,Carry" });
        Assert.Equal(new[] { "Sum", "Carry" }, multi.OutputColumns);

        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--help" }).ShowHelp);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--demo" }).RunDemo);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--benchmark" }).RunBenchmark);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--stress" }).RunStressTest);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--csv-example" }).ShowCsvExample);
        Assert.True(CommandLineProcessor.ParseArguments(new[] { "--csv", "a,b,Result" }).CsvInput);
    }

    /// <summary>
    ///     The standard-format verbs are a whole capability area that lived only in
    ///     <c>--help</c> for a while: they appeared in no README and on no documentation page.
    ///     This compares the verbs the CLI recognizes against the ones the documentation lists,
    ///     in BOTH directions — a new verb that nobody documents fails here just as loudly as a
    ///     documented verb that was renamed or removed.
    /// </summary>
    [Fact]
    public void Cli_RecognizesEveryDocumentedStandardFormatVerb()
    {
        foreach (var verb in FormatCommands.Verbs)
            Assert.True(FormatCommands.IsFormatVerb(verb), $"'{verb}' is listed but not recognized.");

        Assert.False(FormatCommands.IsFormatVerb("a & b"));
        Assert.False(FormatCommands.IsFormatVerb("--cnf"));

        // Each document must name every verb as a command, i.e. followed by its input file.
        foreach (var relative in new[]
                 {
                     "README.md",
                     Path.Combine("LogicalOptimizer.Cli", "README.md"),
                     Path.Combine("docs-site", "articles", "cli-usage.md")
                 })
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), relative));
            foreach (var verb in FormatCommands.Verbs)
                Assert.True(text.Contains($"logical-optimizer {verb} ", StringComparison.Ordinal),
                    $"{relative} does not document the '{verb}' verb. Every standard-format verb " +
                    "must be documented where the others are, or it is reachable only via --help.");
        }
    }

    /// <summary>Walks up from the test binary to the directory holding the solution.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }
}
