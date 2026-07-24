using Xunit;

namespace LogicalOptimizer.Tests;

public class SatSolverTests
{
    [Fact]
    public void Solve_EmptyProblem_Satisfiable()
    {
        Assert.Equal(SatResult.Satisfiable, new SatSolver(0).Solve());
    }

    [Fact]
    public void Solve_SingleUnit_SatisfiableWithCorrectModel()
    {
        var solver = new SatSolver(1);
        solver.AddClause(-1);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        Assert.False(solver.GetValue(1));
    }

    [Fact]
    public void Solve_ContradictoryUnits_Unsatisfiable()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1);
        solver.AddClause(-1);

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_EmptyClause_Unsatisfiable()
    {
        var solver = new SatSolver(2);
        solver.AddClause(Array.Empty<int>());

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_TautologicalClause_Ignored()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1, -1);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_ChainedImplications_PropagatesToModel()
    {
        // 1 → 2 → 3 → 4, with unit 1: all must be true
        var solver = new SatSolver(4);
        solver.AddClause(1);
        solver.AddClause(-1, 2);
        solver.AddClause(-2, 3);
        solver.AddClause(-3, 4);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        for (var v = 1; v <= 4; v++) Assert.True(solver.GetValue(v));
    }

    [Fact]
    public void Solve_PigeonholeThreeIntoTwo_Unsatisfiable()
    {
        // Variables p_{i,j}: pigeon i (1..3) in hole j (1..2); var index = 2*(i-1)+j
        var solver = new SatSolver(6);
        for (var pigeon = 0; pigeon < 3; pigeon++)
            solver.AddClause(2 * pigeon + 1, 2 * pigeon + 2);
        for (var hole = 1; hole <= 2; hole++)
            for (var a = 0; a < 3; a++)
                for (var b = a + 1; b < 3; b++)
                    solver.AddClause(-(2 * a + hole), -(2 * b + hole));

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
    }

    [Fact]
    public void Solve_RandomThreeSat_MatchesBruteForce()
    {
        var random = new Random(20260724);
        for (var instance = 0; instance < 150; instance++)
        {
            var variableCount = random.Next(3, 11);
            var clauseCount = random.Next(2, variableCount * 5);
            var clauses = new List<int[]>();
            for (var c = 0; c < clauseCount; c++)
            {
                var length = random.Next(1, 4);
                var clause = new int[length];
                for (var k = 0; k < length; k++)
                {
                    var variable = random.Next(1, variableCount + 1);
                    clause[k] = random.Next(2) == 0 ? variable : -variable;
                }

                clauses.Add(clause);
            }

            var solver = new SatSolver(variableCount);
            foreach (var clause in clauses) solver.AddClause(clause);
            var verdict = solver.Solve();

            var expected = SatTestOracles.BruteForceSatisfiable(variableCount, clauses);
            Assert.True(verdict == (expected ? SatResult.Satisfiable : SatResult.Unsatisfiable),
                $"Instance {instance}: solver={verdict}, bruteforce={expected}");

            if (verdict == SatResult.Satisfiable)
                foreach (var clause in clauses)
                    Assert.True(clause.Any(l => solver.GetValue(Math.Abs(l)) == l > 0),
                        $"Instance {instance}: model does not satisfy clause [{string.Join(",", clause)}]");
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Solve_PhaseTransitionInstances_ResolveWithinBudget()
    {
        // Random 3-SAT at clause ratio ~4.2 (near the hardness peak): exercises the heap,
        // Luby restarts and learnt-database reduction at a scale the small tests never reach
        var random = new Random(60606);
        for (var instance = 0; instance < 5; instance++)
        {
            const int variableCount = 60;
            var solver = new SatSolver(variableCount);
            var clauses = new List<int[]>();
            for (var c = 0; c < 252; c++)
            {
                var vars = new HashSet<int>();
                while (vars.Count < 3) vars.Add(random.Next(1, variableCount + 1));
                var clause = vars.Select(v => random.Next(2) == 0 ? v : -v).ToArray();
                clauses.Add(clause);
                solver.AddClause(clause);
            }

            var verdict = solver.Solve(500_000);
            Assert.NotEqual(SatResult.Unknown, verdict);
            if (verdict == SatResult.Satisfiable)
                Assert.All(clauses, clause =>
                    Assert.Contains(clause, l => solver.GetValue(Math.Abs(l)) == l > 0));
        }
    }

    [Fact]
    public void Luby_ReluctantDoublingPrefix()
    {
        var expected = new[] { 1, 1, 2, 1, 1, 2, 4, 1, 1, 2, 1, 1, 2, 4, 8 };
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], SatSolver.Luby(i));
    }

    [Fact]
    public void FromCnf_TseitinPipeline_SolvesFormula()
    {
        var ast = SatTestOracles.Parse("(a | b) & (!a | c) & !c");
        var cnf = TseitinConverter.Convert(ast);
        var solver = SatSolver.FromCnf(cnf);

        Assert.Equal(SatResult.Satisfiable, solver.Solve());
        // Model restricted to inputs must satisfy the original formula: b=1, a=0, c=0
        Assert.False(solver.GetValue(1)); // a
        Assert.True(solver.GetValue(2)); // b
        Assert.False(solver.GetValue(3)); // c
    }
}

public class EquivalenceCheckerTests
{
    [Theory]
    [InlineData("a & b | a & !b", "a", true)]
    [InlineData("!(a & b)", "!a | !b", true)]
    [InlineData("a | b", "a & b", false)]
    [InlineData("a", "b", false)]
    public void Check_SmallExpressions_TruthTablePath(string left, string right, bool equivalent)
    {
        var result = EquivalenceChecker.Check(left, right);

        Assert.Equal(equivalent, result.AreEquivalent);
        if (!equivalent) Assert.NotNull(result.Counterexample);
    }

    [Fact]
    public void Check_DeMorganOverTwentyVariables_SatPathProvesEquivalence()
    {
        // 20 variables is beyond the truth-table guard range: this exercises the SAT miter
        var names = Enumerable.Range(1, 20).Select(i => $"v{i:D2}").ToList();
        var conjunction = string.Join(" & ", names);
        var negatedDisjunction = string.Join(" | ", names.Select(n => $"!{n}"));

        var result = EquivalenceChecker.Check($"!({conjunction})", negatedDisjunction);

        Assert.True(result.AreEquivalent);
    }

    [Fact]
    public void Check_LargeNonEquivalent_ProducesValidCounterexample()
    {
        var names = Enumerable.Range(1, 18).Select(i => $"v{i:D2}").ToList();
        var left = string.Join(" | ", names.Select((n, i) => i % 2 == 0 ? n : $"!{n}"));
        var right = string.Join(" | ", names); // differs when all odd-indexed are true, rest false

        var result = EquivalenceChecker.Check(left, right);

        Assert.False(result.AreEquivalent);
        Assert.NotNull(result.Counterexample);

        var leftAst = new Parser(new Lexer(left).Tokenize()).Parse();
        var rightAst = new Parser(new Lexer(right).Tokenize()).Parse();
        var assignment = result.Counterexample!.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.NotEqual(TruthTable.Evaluate(leftAst, assignment), TruthTable.Evaluate(rightAst, assignment));
    }

    [Fact]
    public void CheckWithSat_RandomSmallExpressions_AgreesWithTruthTable()
    {
        var generator = new Random(424242);
        var variables = new[] { "a", "b", "c", "d", "e" };

        for (var i = 0; i < 200; i++)
        {
            var left = RandomExpression(generator, variables, 4);
            var right = generator.Next(3) == 0 ? left : RandomExpression(generator, variables, 4);

            var leftAst = new Parser(new Lexer(left).Tokenize()).Parse();
            var rightAst = new Parser(new Lexer(right).Tokenize()).Parse();

            var expected = TruthTable.AreEquivalent(leftAst, rightAst);
            var satVerdict = EquivalenceChecker.CheckWithSat(leftAst, rightAst, 100_000);

            Assert.True(expected == satVerdict.AreEquivalent,
                $"'{left}' vs '{right}': table={expected}, sat={satVerdict.AreEquivalent}");
        }
    }

    private static string RandomExpression(Random random, string[] variables, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            var name = variables[random.Next(variables.Length)];
            return random.Next(2) == 0 ? name : $"!{name}";
        }

        var left = RandomExpression(random, variables, depth - 1);
        var right = RandomExpression(random, variables, depth - 1);
        return random.Next(2) == 0 ? $"({left} & {right})" : $"({left} | {right})";
    }

    [Fact]
    public void Optimizer_BeyondTruthTableRange_OutputSatVerifiedEquivalent()
    {
        // 16 variables: soundness guard runs on the SAT path inside Optimize; verify
        // the final result independently here as well
        var terms = Enumerable.Range(1, 8).Select(i => $"p{i:D2} & q{i:D2} | p{i:D2} & !q{i:D2}");
        var expression = string.Join(" | ", terms);

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false });

        var check = EquivalenceChecker.Check(expression, result.Optimized);
        Assert.True(check.AreEquivalent);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Optimizer_ThirtyVariableXorPairs_OutputEquivalent()
    {
        // Scale smoke test (salvaged from the deleted AST-advanced-forms stress test):
        // a 30-variable disjunction of XOR pairs must survive the full pipeline
        // semantically intact — verified by the SAT-based equivalence checker
        var terms = Enumerable.Range(0, 15)
            .Select(i => $"(v{2 * i + 1:D2} & !v{2 * i + 2:D2}) | (!v{2 * i + 1:D2} & v{2 * i + 2:D2})");
        var expression = string.Join(" | ", terms);

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression);

        Assert.True(EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent);
    }
}
