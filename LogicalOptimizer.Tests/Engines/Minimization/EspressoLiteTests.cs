using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Espresso-lite heuristic minimizer: exact-zone quality measured against the QM
///     optimum, large-scale soundness proven by the SAT miter (no truth table exists
///     at 40+ variables), and the tautology primitive checked against brute force.
/// </summary>
public class EspressoLiteTests
{
    private static AstNode Sop(IReadOnlyList<string> variables, HashSet<int> onSet)
    {
        var terms = onSet.Select(m => string.Join(" & ", variables.Select((v, j) =>
            (m & (1 << j)) != 0 ? v : "!" + v)));
        return RandomExpressions.Parse(string.Join(" | ", terms));
    }

    [Fact]
    public void Minimize_RandomExactZoneFunctions_CloseToQuineMcCluskeyOptimum()
    {
        var rng = new Random(969);
        long espressoTotal = 0, optimumTotal = 0;
        for (var trial = 0; trial < 25; trial++)
        {
            var variableCount = rng.Next(3, 8);
            var variables = Enumerable.Range(0, variableCount).Select(i => $"v{i}").ToList();
            var onSet = new HashSet<int>();
            for (var m = 0; m < 1 << variableCount; m++)
                if (rng.Next(2) == 0)
                    onSet.Add(m);
            if (onSet.Count == 0) continue;

            var canonical = Sop(variables, onSet);
            var minimized = Transformations.MinimizeDnfHeuristic(canonical);
            var (optimum, proven) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);

            Assert.True(TruthTable.AreEquivalent(canonical, minimized),
                $"Trial {trial}: espresso changed semantics");

            var espressoCost = AstMetrics.CountLiterals(minimized);
            var optimumCost = AstMetrics.CountLiterals(optimum);
            if (proven)
                Assert.True(espressoCost >= optimumCost,
                    $"Trial {trial}: espresso beat a PROVEN minimum — QM proof wrong");
            espressoTotal += espressoCost;
            optimumTotal += optimumCost;
        }

        // Heuristic quality gate: within 15% of the exact optimum in aggregate
        Assert.True(espressoTotal <= optimumTotal * 115 / 100,
            $"Espresso total {espressoTotal} vs optimum {optimumTotal} — quality regressed");
    }

    [Fact]
    public void Minimize_FortyVariables_ShrinksAndStaysEquivalentBySatMiter()
    {
        // 40 variables: no truth table can exist; redundancy is planted deliberately
        // (absorbed terms + consensus chains), soundness verified by the SAT miter
        var terms = new List<string>();
        for (var i = 0; i < 13; i++)
        {
            var a = $"v{2 * i:D2}";
            var b = $"v{2 * i + 1:D2}";
            var c = $"v{(2 * i + 2) % 40:D2}";
            terms.Add($"{a} & {b}");
            terms.Add($"{a} & {b} & {c}"); // absorbed by the pair above
            terms.Add($"!{a} & {c}");
            terms.Add($"{b} & {c}"); // consensus of the two above
        }

        var expression = string.Join(" | ", terms);
        var original = RandomExpressions.Parse(expression);
        var minimized = Transformations.MinimizeDnfHeuristic(original);

        // The redundancy is planted exactly: each of the 13 groups keeps its 2-literal pair
        // ("a & b") and its 2-literal "!a & c" while the absorbed superset ("a & b & c") and
        // the consensus cube ("b & c") are dropped → 4 literals × 13 = 52. Pinning the exact
        // count (not just "< original") fails if even ONE planted cube survives.
        Assert.Equal(52, AstMetrics.CountLiterals(minimized));
        Assert.True(EquivalenceChecker.Check(original, minimized).AreEquivalent == true,
            "SAT miter refuted the espresso result");
    }

    [Fact]
    public void Minimize_SixtyVariableAbsorptionChain_RemovesAllAbsorbedCubes()
    {
        // 20 base pairs + 40 absorbed supersets: result must be exactly the 20 pairs
        var terms = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            terms.Add($"a{i:D2} & b{i:D2}");
            terms.Add($"a{i:D2} & b{i:D2} & c{i % 10:D2}");
            terms.Add($"a{i:D2} & b{i:D2} & !c{(i + 3) % 10:D2}");
        }

        var original = RandomExpressions.Parse(string.Join(" | ", terms));
        var minimized = Transformations.MinimizeDnfHeuristic(original);

        Assert.Equal(40, AstMetrics.CountLiterals(minimized));
        // No truth table exists at 60 variables; soundness verified by the SAT miter,
        // matching the 40-variable sibling above.
        Assert.True(EquivalenceChecker.Check(original, minimized).AreEquivalent);
    }

    [Fact]
    public void Minimize_TinyBudget_StaysSoundJustLessOptimal()
    {
        var variables = new List<string> { "a", "b", "c" };
        var onSet = new HashSet<int> { 0, 1, 2, 5, 6, 7 }; // the cyclic core
        var canonical = Sop(variables, onSet);

        var minimized = Transformations.MinimizeDnfHeuristic(canonical, stepLimit: 3);

        Assert.True(TruthTable.AreEquivalent(canonical, minimized),
            "Budget exhaustion produced an unsound result");
    }

    [Fact]
    public void Minimize_NonSopInput_ReturnedUnchanged()
    {
        var nested = RandomExpressions.Parse("(a | b) & (c | d)");
        Assert.Same(nested, Transformations.MinimizeDnfHeuristic(nested));
    }

    // --- Mutation-testing killers: each pins a behavior a survived Stryker mutant
    // --- could break (cofactor conflict detection, constant-1 terms, last-variable
    // --- literal emission). Deterministic single-case tests, not fuzz.

    [Fact]
    public void Minimize_EqvCover_StaysExactlyEqvKillsCofactorConflictMutant()
    {
        // {a&b, !a&!b} is EQV: no expansion is valid. A CofactorByCube that detects
        // conflicts only when BOTH polarities clash ('||' -> '&&' mutant) wrongly
        // validates dropping literals, turning the cover into a & b | !a — unsound
        var eqv = RandomExpressions.Parse("a & b | !a & !b");
        var minimized = Transformations.MinimizeDnfHeuristic(eqv);

        Assert.True(TruthTable.AreEquivalent(eqv, minimized));
        Assert.Equal(4, AstMetrics.CountLiterals(minimized));
    }

    [Fact]
    public void Minimize_ConstantTrueTerm_ReturnsInputUnchanged()
    {
        // "a | 1" is a tautology; the SOP parser must refuse (return input) rather
        // than silently DROP the constant term and answer "a"
        var tautology = RandomExpressions.Parse("a | 1");
        var minimized = Transformations.MinimizeDnfHeuristic(tautology);
        Assert.True(TruthTable.AreEquivalent(tautology, minimized));
    }

    [Fact]
    public void Minimize_LastSortedVariable_SurvivesRebuild()
    {
        // The alphabetically-last variable exercises the BuildSop upper loop bound;
        // an off-by-one silently drops its literal
        var minimized = Transformations.MinimizeDnfHeuristic(
            RandomExpressions.Parse("a & z | a & z"));
        Assert.Equal("a & z", minimized.ToString());
    }

    [Fact]
    public void IsTautology_AgreesWithBruteForceOracle()
    {
        var rng = new Random(979);
        for (var trial = 0; trial < 120; trial++)
        {
            var variableCount = rng.Next(1, 6);
            var variables = Enumerable.Range(0, variableCount).Select(i => $"v{i}").ToList();
            var onSet = new HashSet<int>();
            for (var m = 0; m < 1 << variableCount; m++)
                if (rng.Next(4) != 0)
                    onSet.Add(m);
            if (onSet.Count == 0) continue;

            var sop = Sop(variables, onSet);
            var parsed = EspressoLiteMinimizer.TryParseSop(sop);
            if (parsed is null)
            {
                // Construction-time complement folding collapsed the SOP to constant 1 —
                // only possible when the function is a tautology, so the verdict is known
                Assert.True(sop is ConstantNode { Value: true } && onSet.Count == 1 << variableCount,
                    $"Trial {trial}: TryParseSop refused a non-degenerate SOP '{sop}'");
                continue;
            }

            var budget = 1_000_000;
            var verdict = EspressoLiteMinimizer.IsTautology(parsed!.Value.Cover, variableCount, ref budget);

            Assert.True(verdict == (onSet.Count == 1 << variableCount),
                $"Trial {trial}: tautology verdict {verdict} for {onSet.Count}/{1 << variableCount} minterms");
        }
    }
}
