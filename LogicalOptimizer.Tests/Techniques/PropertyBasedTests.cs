using CsCheck;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Property-based testing with CsCheck: universally quantified properties over
///     generated ASTs. CsCheck shrinks every failure to a minimal counterexample and
///     prints a reproducing seed.
/// </summary>
public class PropertyBasedTests
{
    private static readonly OptimizationOptions ResultOnly = new()
    { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

    private static readonly Gen<AstNode> Leaf = Gen.OneOf(
        Gen.Int[0, 5].Select(i => (AstNode)new VariableNode($"v{i}")),
        Gen.Int[0, 5].Select(i => (AstNode)new NotNode(new VariableNode($"v{i}"))),
        Gen.Bool.Select(b => (AstNode)new ConstantNode(b)));

    private static Gen<AstNode> Ast(int depth)
    {
        if (depth <= 0) return Leaf;
        var inner = Ast(depth - 1);
        return Gen.OneOf(
            Leaf,
            Gen.Select(inner, inner, (l, r) => (AstNode)new AndNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new OrNode(l, r)),
            inner.Select(x => (AstNode)new NotNode(x)));
    }

    private static readonly Gen<AstNode> AnyAst = Ast(4);

    /// <summary>
    ///     ASTs over the FULL operator set including Xor/Eqv/Imp/Nand/Nor. The text
    ///     grammar cannot express these, so their properties run engine-to-engine
    ///     (TruthTable vs AIG vs BDD vs SAT) instead of through the parser.
    /// </summary>
    private static Gen<AstNode> ExtendedAst(int depth)
    {
        if (depth <= 0) return Leaf;
        var inner = ExtendedAst(depth - 1);
        return Gen.OneOf(
            Leaf,
            Gen.Select(inner, inner, (l, r) => (AstNode)new AndNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new OrNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new XorNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new EqvNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new ImpNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new NandNode(l, r)),
            Gen.Select(inner, inner, (l, r) => (AstNode)new NorNode(l, r)),
            inner.Select(x => (AstNode)new NotNode(x)));
    }

    private static readonly Gen<AstNode> AnyExtendedAst = ExtendedAst(3);

    [Fact]
    public void Property_OptimizationPreservesSemanticsAndNeverWorsensLiterals()
    {
        AnyAst.Sample(ast =>
        {
            var result = new BooleanExpressionOptimizer()
                .OptimizeExpression(ast.ToString(), ResultOnly);
            Assert.True(TruthTable.AreEquivalent(ast.ToString(), result.Optimized),
                $"Semantics broken for '{ast}' -> '{result.Optimized}'");

            // Quality floor: the result never has more literals than the canonical
            // (factory-imported) form of the input — every pipeline stage either
            // improves the (literals, nodes) cost or is rolled back
            var canonicalInput = new FormulaFactory().Import(ast);
            var optimized = RandomExpressions.Parse(result.Optimized);
            Assert.True(AstMetrics.CountLiterals(optimized) <= AstMetrics.CountLiterals(canonicalInput),
                $"Optimizer worsened '{canonicalInput}' ({AstMetrics.CountLiterals(canonicalInput)} literals) " +
                $"to '{result.Optimized}' ({AstMetrics.CountLiterals(optimized)} literals)");
        }, iter: 200);
    }

    [Fact]
    public void Property_OptimizationNeverIncreasesLiteralsOnReRun()
    {
        AnyAst.Sample(ast =>
        {
            var optimizer = new BooleanExpressionOptimizer();
            var once = optimizer.OptimizeExpression(ast.ToString(), ResultOnly).Optimized;
            var twice = optimizer.OptimizeExpression(once, ResultOnly).Optimized;
            Assert.True(
                AstMetrics.CountLiterals(RandomExpressions.Parse(twice)) <=
                AstMetrics.CountLiterals(RandomExpressions.Parse(once)),
                $"Second pass worsened '{once}' to '{twice}'");
        }, iter: 100);
    }

    [Fact]
    public void Property_PrintParseRoundTripIsSemanticIdentityAndPrintFixpoint()
    {
        // Parsing canonicalizes (flatten, sort, fold), so structural equality with a
        // raw AST is NOT promised. What IS promised: reparsing loses no semantics, and
        // once canonical, the printed form is a fixpoint (print ∘ parse ∘ print = print)
        AnyAst.Sample(ast =>
        {
            var printed = ast.ToString();
            var reparsed = RandomExpressions.Parse(printed);
            Assert.True(TruthTable.AreEquivalent(ast, reparsed),
                $"Round trip changed the semantics of '{printed}'");
            var canonical = reparsed.ToString();
            Assert.True(canonical == RandomExpressions.Parse(canonical).ToString(),
                $"Printing is not a fixpoint: '{canonical}' reprints as '{RandomExpressions.Parse(canonical)}'");
        }, iter: 300);
    }

    [Fact]
    public void Property_WhitespaceIsInsignificant()
    {
        Gen.Select(AnyAst, Gen.Int[0, int.MaxValue], (ast, seed) => (ast, seed)).Sample(t =>
        {
            // Pad only at token boundaries — whitespace INSIDE an identifier is a
            // different token stream, not insignificant whitespace. Compare canonical
            // parses: the raw AST's print order is not canonical, its parse is.
            var text = RandomExpressions.Parse(t.ast.ToString()).ToString();
            var rng = new Random(t.seed);
            var padded = new System.Text.StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                padded.Append(text[i]);
                var insideIdentifier = i + 1 < text.Length &&
                                       char.IsLetterOrDigit(text[i]) && char.IsLetterOrDigit(text[i + 1]);
                if (!insideIdentifier && rng.Next(3) == 0) padded.Append(rng.Next(2) == 0 ? " " : "\t ");
            }

            Assert.True(text == RandomExpressions.Parse(padded.ToString()).ToString(),
                $"Whitespace changed the parse of '{text}' (padded: '{padded}')");
        }, iter: 200);
    }

    [Fact]
    public void Property_TseitinStaysLinearInFormulaSize()
    {
        // The Tseitin contract: clause count is linear in AST node count — this is the
        // whole reason the transformation exists, so it is pinned as a property
        AnyAst.Sample(ast =>
        {
            var cnf = TseitinConverter.Convert(ast);
            var nodes = AstMetrics.CountNodes(ast);
            Assert.True(cnf.Clauses.Count <= 4 * nodes + 4,
                $"Tseitin produced {cnf.Clauses.Count} clauses for {nodes} nodes: '{ast}'");
        }, iter: 200);
    }

    [Fact]
    public void Property_BddAndTruthTableAgreeOnEquivalence()
    {
        // Differential property: for ANY pair of formulas the BDD engine and the
        // brute-force oracle must return the same equivalence verdict
        Gen.Select(Ast(3), Ast(3), (l, r) => (l, r)).Sample(pair =>
        {
            var byTable = TruthTable.AreEquivalent(pair.l, pair.r);
            var byBdd = BinaryDecisionDiagram.AreEquivalent(pair.l, pair.r);
            Assert.True(byBdd == byTable,
                $"BDD={byBdd}, oracle={byTable} for '{pair.l}' vs '{pair.r}'");
        }, iter: 200);
    }

    [Fact]
    public void Property_SatMiterAndTruthTableAgreeOnEquivalence()
    {
        Gen.Select(Ast(3), Ast(3), (l, r) => (l, r)).Sample(pair =>
        {
            var byTable = TruthTable.AreEquivalent(pair.l, pair.r);
            // Force the SAT/miter/Tseitin stack (the generator makes <=6-var formulas, which
            // EquivalenceChecker.Check would route to the truth table — the SAME oracle).
            // CheckWithSat is internal but reachable via InternalsVisibleTo.
            var bySat = EquivalenceChecker
                .CheckWithSat(pair.l, pair.r, EquivalenceChecker.DefaultMaxConflicts).AreEquivalent;
            Assert.True(bySat == byTable,
                $"SAT={bySat}, oracle={byTable} for '{pair.l}' vs '{pair.r}'");
        }, iter: 150);
    }

    [Fact]
    public void Property_CnfAndDnfArtifactsAreEquivalentToInput()
    {
        AnyAst.Sample(ast =>
        {
            var result = new BooleanExpressionOptimizer().OptimizeExpression(ast.ToString(),
                new OptimizationOptions { ComputeAdvancedForms = false });
            if (result.CnfStatus == ComputationStatus.Computed)
                Assert.True(TruthTable.AreEquivalent(ast.ToString(), result.CNF),
                    $"CNF artifact wrong for '{ast}': '{result.CNF}'");
            if (result.DnfStatus == ComputationStatus.Computed)
                Assert.True(TruthTable.AreEquivalent(ast.ToString(), result.DNF),
                    $"DNF artifact wrong for '{ast}': '{result.DNF}'");
        }, iter: 100);
    }

    [Fact]
    public void Property_ProvenMinimalDnfIsNeverBeatenByAnyEquivalentCover()
    {
        // For proven-minimal results, spot-check the claim: drop any single term from
        // the minimal DNF and it must stop being equivalent (irredundancy)
        AnyAst.Sample(ast =>
        {
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count is 0 or > 6) return;

            var result = new BooleanExpressionOptimizer().OptimizeExpression(ast.ToString(),
                new OptimizationOptions { ComputeCnf = false, ComputeAdvancedForms = false });
            if (result.MinimizationStatus != MinimizationStatus.MinimalProven) return;
            if (result.DnfStatus != ComputationStatus.Computed) return;

            var terms = SplitTopLevelOr(RandomExpressions.Parse(result.DNF));
            if (terms.Count <= 1) return;

            for (var drop = 0; drop < terms.Count; drop++)
            {
                var kept = terms.Where((_, i) => i != drop).ToList();
                var weakened = kept.Aggregate((l, r) => (AstNode)new OrNode(l, r));
                Assert.False(TruthTable.AreEquivalent(ast, weakened),
                    $"Term {terms[drop]} of the PROVEN minimal DNF of '{ast}' is redundant");
            }
        }, iter: 100);
    }

    [Fact]
    public void Property_ExtendedOperators_AllEnginesAgreeOnEveryAssignment()
    {
        // Debt closed: generators now cover Xor/Eqv/Imp/Nand/Nor. Four independent
        // engines must agree pointwise: TruthTable (oracle), AIG, BDD, FormulaFactory
        AnyExtendedAst.Sample(ast =>
        {
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count > 6) return;

            var aig = AndInverterGraph.FromAst(ast);
            var bdd = BinaryDecisionDiagram.Build(ast);
            var imported = new FormulaFactory().Import(ast);

            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                var oracle = TruthTable.Evaluate(ast, assignment);
                Assert.True(aig.Evaluate(aig.Root, assignment) == oracle,
                    $"AIG disagrees at {m} for '{ast}'");
                Assert.True(bdd.Evaluate(bdd.Root, assignment) == oracle,
                    $"BDD disagrees at {m} for '{ast}'");
                Assert.True(TruthTable.Evaluate(imported, assignment) == oracle,
                    $"FormulaFactory.Import disagrees at {m} for '{ast}'");
            }
        }, iter: 150);
    }

    [Fact]
    public void Property_ExtendedOperators_BothCnfEncodingsEquisatisfiable()
    {
        AnyExtendedAst.Sample(ast =>
        {
            var variables = ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count > 6) return;

            var satisfiable = false;
            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count && !satisfiable; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                satisfiable = TruthTable.Evaluate(ast, assignment);
            }

            foreach (var style in new[] { CnfEncodingStyle.Tseitin, CnfEncodingStyle.PlaistedGreenbaum })
            {
                var verdict = SatSolver.FromCnf(TseitinConverter.Convert(ast, style)).Solve();
                Assert.True((verdict == SatResult.Satisfiable) == satisfiable,
                    $"{style} broke equisatisfiability for '{ast}'");
            }
        }, iter: 120);
    }

    [Fact]
    public void Property_EspressoNeverWorsensAndPreservesSemantics()
    {
        // Random flat SOPs straight into the cube-list minimizer
        var termGen = Gen.Select(Gen.Int[0, 4], Gen.Int[0, 2], Gen.Bool,
            (v1, extra, neg) => $"{(neg ? "!" : "")}v{v1} & v{(v1 + extra + 1) % 6}");
        var sopGen = termGen.List[1, 8].Select(terms => string.Join(" | ", terms));

        sopGen.Sample(sop =>
        {
            var ast = RandomExpressions.Parse(sop);
            var minimized = Transformations.MinimizeDnfHeuristic(ast);
            Assert.True(TruthTable.AreEquivalent(ast, minimized),
                $"Espresso changed semantics of '{sop}'");
            Assert.True(AstMetrics.CountLiterals(minimized) <= AstMetrics.CountLiterals(ast),
                $"Espresso worsened '{sop}' to '{minimized}'");
        }, iter: 200);
    }

    [Fact]
    public void Property_FormulaFactoryImport_IsIdempotentAndInterned()
    {
        AnyAst.Sample(ast =>
        {
            var factory = new FormulaFactory();
            var once = factory.Import(ast);
            var twice = factory.Import(once);
            // Interning promises full reference equality, not just structural stability
            Assert.Same(once, twice);
            Assert.Same(factory.Import(ast), once);
        }, iter: 200);
    }

    [Fact]
    public void Property_BddQuantification_MatchesBruteForce()
    {
        Gen.Select(Ast(3), Gen.Int[0, 5], (ast, pick) => (ast, pick)).Sample(t =>
        {
            var variables = t.ast.GetVariables().OrderBy(v => v).ToList();
            if (variables.Count == 0) return;
            var x = variables[t.pick % variables.Count];

            var bdd = BinaryDecisionDiagram.Build(t.ast);
            var exists = bdd.Exists(bdd.Root, x);
            var forAll = bdd.ForAll(bdd.Root, x);

            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                var low = TruthTable.Evaluate(t.ast, new Dictionary<string, bool>(assignment) { [x] = false });
                var high = TruthTable.Evaluate(t.ast, new Dictionary<string, bool>(assignment) { [x] = true });
                Assert.True(bdd.Evaluate(exists, assignment) == (low || high),
                    $"Exists({x}) wrong for '{t.ast}'");
                Assert.True(bdd.Evaluate(forAll, assignment) == (low && high),
                    $"ForAll({x}) wrong for '{t.ast}'");
            }
        }, iter: 150);
    }

    private static List<AstNode> SplitTopLevelOr(AstNode node)
    {
        if (node is not OrNode or) return new List<AstNode> { node };
        return or.Operands.SelectMany(SplitTopLevelOr).ToList();
    }
}
