using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for the exact Quine–McCluskey minimizer. Known-minimal reference sizes:
///     majority(a,b,c) = 3 terms / 6 literals, mux = 2/4, parity3 = 4/12.
/// </summary>
public class TruthTableMinimizerTests
{
    private static HashSet<int> OnSetOf(string expression, List<string> variables)
    {
        var ast = new Parser(new Lexer(expression).Tokenize()).Parse();
        var onSet = new HashSet<int>();
        var assignment = new Dictionary<string, bool>();

        for (var minterm = 0; minterm < 1 << variables.Count; minterm++)
        {
            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (minterm & (1 << j)) != 0;
            if (TruthTable.Evaluate(ast, assignment)) onSet.Add(minterm);
        }

        return onSet;
    }

    private static void AssertMinimalSop(string expression, int expectedLiterals)
    {
        var variables = new Parser(new Lexer(expression).Tokenize()).Parse()
            .GetVariables().OrderBy(v => v).ToList();
        var onSet = OnSetOf(expression, variables);

        var minimal = TruthTableMinimizer.MinimalSop(variables, onSet);

        Assert.True(TruthTable.AreEquivalent(Parse(expression), minimal),
            $"MinimalSop of '{expression}' is not equivalent: '{minimal}'");
        Assert.Equal(expectedLiterals, AstMetrics.CountLiterals(minimal));
    }

    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    [Fact]
    public void MinimalSop_Majority_SixLiterals()
    {
        AssertMinimalSop("a & b | a & c | b & c", 6);
    }

    [Fact]
    public void MinimalSop_Mux_FourLiterals()
    {
        AssertMinimalSop("s & a | !s & b", 4);
    }

    // --- Apples-to-apples two-level (--dnf) comparison-harness path (roadmap Track 4) ---
    // These pin the two-level SOP literal counts the `compare --dnf` harness reports for
    // corpus functions, and assert result.DNF (the facade's two-level form) agrees with
    // the standalone TruthTableMinimizer.MinimalSop. Known-correct sizes match SymPy/PyEDA
    // cube-for-cube: maj3 = 6, xor3 = 12, maj4 = 12, pos6 = 24.
    [Theory]
    [InlineData("a & b | a & c | b & c", 6)] // maj3
    [InlineData("a & !b & !c | !a & b & !c | !a & !b & c | a & b & c", 12)] // xor3
    [InlineData("a & b & c | a & b & d | a & c & d | b & c & d", 12)] // maj4
    [InlineData("(a | b) & (c | d) & (e | f)", 24)] // pos6 (two-level SOP expansion)
    public void TwoLevelSop_CorpusFunctions_MatchKnownLiteralCounts(string expression, int expectedLiterals)
    {
        AssertMinimalSop(expression, expectedLiterals);
    }

    [Theory]
    [InlineData("a & b | a & c | b & c", 6)] // maj3
    [InlineData("a & !b & !c | !a & b & !c | !a & !b & c | a & b & c", 12)] // xor3
    [InlineData("a & b & c | a & b & d | a & c & d | b & c & d", 12)] // maj4
    public void FacadeDnf_TwoLevelForm_MatchesKnownLiteralCount(string expression, int expectedLiterals)
    {
        // The `--dnf` harness counts literals of result.DNF; assert the facade's two-level
        // SOP is equivalent and matches the exact minimizer's size on the guarantee zone.
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeDnf = true, ComputeAdvancedForms = false });

        Assert.Equal(MinimizationStatus.MinimalProven, result.MinimizationStatus);
        var dnf = Parse(result.DNF);
        Assert.True(TruthTable.AreEquivalent(Parse(expression), dnf),
            $"result.DNF of '{expression}' is not equivalent: '{result.DNF}'");
        Assert.Equal(expectedLiterals, AstMetrics.CountLiterals(dnf));
    }

    [Fact]
    public void OptimizeExpression_EquivalentCnf_GuaranteeZone_ReportsProvenMinimalPos()
    {
        // The equivalent-CNF (POS) carries its own proof status, separate from the SOP-scoped
        // MinimizationStatus; in the ≤10 guarantee zone it is provably minimal.
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c | b & c",
            new OptimizationOptions
            { ComputeCnf = true, CnfMode = CnfMode.Equivalent, ComputeDnf = false, ComputeAdvancedForms = false });

        Assert.Equal(MinimizationStatus.MinimalProven, result.MinimizationStatus);
        Assert.Equal(MinimizationStatus.MinimalProven, result.CnfMinimizationStatus);
    }

    [Fact]
    public void OptimizeExpression_TseitinCnf_CnfMinimizationStatusStaysHeuristic()
    {
        // Tseitin CNF is equisatisfiable, not a minimal POS — two-level minimality does not
        // apply, so the CNF minimality status must not claim MinimalProven.
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c | b & c",
            new OptimizationOptions
            { ComputeCnf = true, CnfMode = CnfMode.Tseitin, ComputeDnf = false, ComputeAdvancedForms = false });

        Assert.Equal(MinimizationStatus.Heuristic, result.CnfMinimizationStatus);
    }

    [Fact]
    public void MinimalSop_Parity3_TwelveLiterals()
    {
        AssertMinimalSop(
            "a & !b & !c | !a & b & !c | !a & !b & c | a & b & c", 12);
    }

    [Fact]
    public void MinimalSop_DenseFunction_CollapsesToTwoLiterals()
    {
        // !c | !d shape: 12 of 16 minterms; used to defeat the rewrite pipeline
        AssertMinimalSop("!c & !d | c & !d | !c & d", 2);
    }

    [Fact]
    public void MinimalSop_Constants()
    {
        var variables = new List<string> { "a" };
        Assert.Equal("0", TruthTableMinimizer.MinimalSop(variables, new HashSet<int>()).ToString());
        Assert.Equal("1", TruthTableMinimizer.MinimalSop(variables, new HashSet<int> { 0, 1 }).ToString());
    }

    [Fact]
    public void MinimalSop_DontCares_ReduceCover()
    {
        // XOR on specified rows {01, 10}; row 11 is don't-care => cover is a | b (2 literals)
        var variables = new List<string> { "a", "b" };
        var onSet = new HashSet<int> { 1, 2 };
        var dontCare = new HashSet<int> { 3 };

        var minimal = TruthTableMinimizer.MinimalSop(variables, onSet, dontCare);

        Assert.Equal(2, AstMetrics.CountLiterals(minimal));
        // Must still be correct on the specified rows
        Assert.False(TruthTable.Evaluate(minimal, new Dictionary<string, bool> { ["a"] = false, ["b"] = false }));
        Assert.True(TruthTable.Evaluate(minimal, new Dictionary<string, bool> { ["a"] = true, ["b"] = false }));
        Assert.True(TruthTable.Evaluate(minimal, new Dictionary<string, bool> { ["a"] = false, ["b"] = true }));
    }

    [Fact]
    public void MinimalPos_RemovesRedundantConsensusClause()
    {
        // (a | b) & (!a | c) & (b | c): the clause (b | c) is a redundant resolvent
        var expression = "(a | b) & (!a | c) & (b | c)";
        var variables = new List<string> { "a", "b", "c" };
        var onSet = OnSetOf(expression, variables);

        var minimalPos = TruthTableMinimizer.MinimalPos(variables, onSet);

        Assert.True(TruthTable.AreEquivalent(Parse(expression), minimalPos));
        Assert.Equal(4, AstMetrics.CountLiterals(minimalPos));
    }

    // --- Mutation-testing killers (QM survivors that were real coverage gaps) ---

    [Fact]
    public void MinimalPos_DontCares_ReduceCover()
    {
        // Kills the MinimalPos don't-care mutants (ignored DC set / Concat->Except):
        // XOR specified on {01,10}, row 11 don't-care => single clause (a | b)
        var variables = new List<string> { "a", "b" };
        var minimalPos = TruthTableMinimizer.MinimalPos(variables,
            new HashSet<int> { 1, 2 }, new HashSet<int> { 3 });

        Assert.Equal(2, AstMetrics.CountLiterals(minimalPos));
        Assert.False(TruthTable.Evaluate(minimalPos, new Dictionary<string, bool> { ["a"] = false, ["b"] = false }));
        Assert.True(TruthTable.Evaluate(minimalPos, new Dictionary<string, bool> { ["a"] = true, ["b"] = false }));
        Assert.True(TruthTable.Evaluate(minimalPos, new Dictionary<string, bool> { ["a"] = false, ["b"] = true }));
    }

    [Fact]
    public void MinimalSopWithStatus_ProvenClaimUnderAnyBudget_ImpliesTrueMinimum()
    {
        // The HONESTY contract under mutation: whatever the step budget, a result
        // reported proven must equal the reference optimum. Kills the survivor class
        // around the branch-and-bound (LimitHit suppression, best-update condition,
        // lower-bound arithmetic) where a mutant keeps results sound but lets a
        // suboptimal cover claim proven minimality.
        var rng = new Random(24680);
        for (var trial = 0; trial < 20; trial++)
        {
            var variableCount = rng.Next(4, 9);
            var variables = Enumerable.Range(0, variableCount).Select(i => $"v{i}").ToList();
            var onSet = new HashSet<int>();
            for (var m = 0; m < 1 << variableCount; m++)
                if (rng.Next(2) == 0)
                    onSet.Add(m);
            if (onSet.Count == 0) continue;

            var (reference, referenceProven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
                coverStepLimit: PerformanceValidator.GUARANTEE_COVER_STEP_LIMIT);
            Assert.True(referenceProven, $"Trial {trial}: reference run not proven");
            var optimum = AstMetrics.CountLiterals(reference);

            foreach (var stepLimit in new[] { 1, 5, 25, 125, 625 })
            {
                var (expression, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
                    coverStepLimit: stepLimit);
                Assert.True(TruthTable.AreEquivalent(reference, expression),
                    $"Trial {trial}, limit {stepLimit}: budgeted result unsound");
                if (proven)
                    Assert.True(AstMetrics.CountLiterals(expression) == optimum,
                        $"Trial {trial}, limit {stepLimit}: proven claimed for " +
                        $"{AstMetrics.CountLiterals(expression)} literals vs optimum {optimum}");
            }
        }
    }

    [Fact]
    public void MinimalPos_Constants()
    {
        var variables = new List<string> { "a" };
        Assert.Equal("1", TruthTableMinimizer.MinimalPos(variables, new HashSet<int> { 0, 1 }).ToString());
        Assert.Equal("0", TruthTableMinimizer.MinimalPos(variables, new HashSet<int>()).ToString());
    }

    [Fact]
    public void MinimalSopWithStatus_CyclicCore_ProvenMinimal()
    {
        // Sum m(0,1,2,5,6,7) over {a,b,c}: the textbook cyclic covering table with no
        // essential primes; the exact search must still prove a 6-literal minimum
        var variables = new List<string> { "a", "b", "c" };
        var onSet = new HashSet<int> { 0, 1, 2, 5, 6, 7 };

        var (expression, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);

        Assert.True(proven);
        Assert.Equal(6, AstMetrics.CountLiterals(expression));
    }

    [Fact]
    public void MinimalSopWithStatus_TinyStepLimit_ReportsUnproven()
    {
        // Same cyclic core with a strangled search budget: result stays sound but the
        // status must say so instead of silently claiming minimality
        var variables = new List<string> { "a", "b", "c" };
        var onSet = new HashSet<int> { 0, 1, 2, 5, 6, 7 };

        var (expression, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
            coverStepLimit: 0);

        Assert.False(proven);
        // Union of all six primes of the hexagonal core == the function itself
        var check = new Parser(new Lexer(
            "!b & !c | !a & !c | a & !b | !a & b | a & c | b & c").Tokenize()).Parse();
        Assert.True(TruthTable.AreEquivalent(check, expression));
    }

    [Fact]
    public void MinimalSopWithStatus_RandomFunctions_AlwaysProvenInGuaranteeZone()
    {
        var random = new Random(31337);
        for (var trial = 0; trial < 120; trial++)
        {
            var variableCount = random.Next(3, 9);
            var variables = Enumerable.Range(0, variableCount).Select(i => $"v{i}").ToList();
            var onSet = new HashSet<int>();
            for (var m = 0; m < 1 << variableCount; m++)
                if (random.Next(2) == 0)
                    onSet.Add(m);

            var (_, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
                coverStepLimit: PerformanceValidator.GUARANTEE_COVER_STEP_LIMIT);

            Assert.True(proven, $"Trial {trial}: minimality not proven for a {variableCount}-variable function");
        }
    }

    [Fact]
    public void OptimizeExpression_AllThreeVariableFunctions_MinimalProven()
    {
        // The guarantee must hold for EVERY function, cyclic cores included
        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions
        { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

        for (var truthBits = 1; truthBits < 255; truthBits++)
        {
            var terms = new List<string>();
            for (var minterm = 0; minterm < 8; minterm++)
            {
                if ((truthBits & (1 << minterm)) == 0) continue;
                terms.Add(string.Join(" & ", new[] { "a", "b", "c" }.Select((v, j) =>
                    (minterm & (1 << j)) != 0 ? v : "!" + v)));
            }

            var result = optimizer.OptimizeExpression(string.Join(" | ", terms), options);
            Assert.True(MinimizationStatus.MinimalProven == result.MinimizationStatus,
                $"Function {truthBits}: status {result.MinimizationStatus}");
        }
    }

    [Fact]
    public void OptimizeExpression_GuaranteeZone_ReportsMinimalProven()
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c | b & c");
        Assert.Equal(MinimizationStatus.MinimalProven, result.MinimizationStatus);
    }

    [Fact]
    public void OptimizeExpression_BeyondExactRange_ReportsHeuristic()
    {
        var expression = string.Join(" | ", Enumerable.Range(1, 7).Select(i => $"x{i} & y{i}"));
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false });

        Assert.Equal(MinimizationStatus.Heuristic, result.MinimizationStatus);
    }

    [Fact]
    public void MinimalSop_WorkBudgetExceeded_Throws()
    {
        // Dense random 11-variable ON-set with a tiny budget must give up loudly
        var random = new Random(7);
        var variables = Enumerable.Range(0, 11).Select(i => $"v{i:D2}").ToList();
        var onSet = new HashSet<int>();
        for (var m = 0; m < 1 << 11; m++)
            if (random.Next(2) == 0)
                onSet.Add(m);

        // The budget trip is reported with a dedicated exception type (still an
        // InvalidOperationException by inheritance) so the facade can tell it apart from a
        // genuine invariant violation.
        Assert.Throws<ComputationBudgetExceededException>(() =>
            TruthTableMinimizer.MinimalSop(variables, onSet, pairComparisonLimit: 1_000));
    }

    [Fact]
    public void OptimizeExpression_TwelveVariables_UsesExactBackend()
    {
        // 12 distinct variables: the raised gate must yield the provably minimal result
        // (consensus term x & y is redundant) and a computed minimal DNF
        var expression = "v01 & v02 | !v01 & v03 | v02 & v03 | " +
                         "v04 & v05 | v06 & v07 | v08 & v09 | v10 & v11 | v12 & v01";

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { IncludeMetrics = true });

        Assert.Equal(12, result.Variables.Count);
        Assert.Equal(ComputationStatus.Computed, result.DnfStatus);
        Assert.NotEqual("-", result.DNF);
        // The redundant consensus term v02 & v03 must be gone from the minimal DNF
        Assert.DoesNotContain("v02 & v03", result.DNF);
    }

    [Fact]
    public void OptimizeExpression_DenseTwelveVariables_FallsBackGracefully()
    {
        // Parity-flavored dense structure: QM budget may trip, but the result must stay
        // sound and the call must return quickly instead of hanging for seconds
        var terms = Enumerable.Range(1, 11)
            .Select(i => $"v{i:D2} & !v{i + 1:D2} | !v{i:D2} & v{i + 1:D2}");
        var expression = string.Join(" | ", terms);

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false });

        Assert.True(EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent);
    }

    [Fact]
    public void CsvPartialTable_UnspecifiedRowsAreDontCare()
    {
        var table = CsvTruthTableParser.ParseCsvToPartialTable("a,b,Result\n0,0,0\n0,1,1\n1,0,1");

        Assert.Equal(new List<string> { "a", "b" }, table.Variables);
        Assert.Equal(new[] { 1, 2 }, table.OnSet.OrderBy(m => m));
        Assert.Equal(new[] { 3 }, table.DontCareSet.OrderBy(m => m));
    }

    [Fact]
    public void CsvProcessor_PartialTable_UsesDontCaresForMinimalCover()
    {
        // XOR rows with 1,1 unspecified: don't-care makes the minimal cover a | b
        var expression = CsvProcessor.ProcessCsvInput("a,b,Result\n0,0,0\n0,1,1\n1,0,1");
        Assert.Equal("a | b", expression);
    }
}
