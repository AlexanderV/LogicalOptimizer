using Microsoft.Z3;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Differential fuzzing against Z3 as an external oracle: the optimizer, the built-in
///     CDCL SAT solver, the equivalence checker, and both Tseitin-style CNF encodings are
///     cross-checked against Z3 verdicts on seeded random inputs. Every test degrades
///     gracefully (early return) when the native z3 library cannot be loaded, so the
///     suite stays green on machines without win-x64 native support.
/// </summary>
[Trait("Category", "External")]
public class Z3DifferentialTests
{
    private static bool? _z3Available;

    /// <summary>
    ///     Probe once whether the managed Z3 API can load a COMPATIBLE native library.
    ///     Any initialization failure means "no oracle here": besides a missing library,
    ///     CI runners have been seen loading an older system-wide libz3 whose exports do
    ///     not match the bindings (EntryPointNotFoundException on ubuntu-latest), so the
    ///     probe treats every throw as unavailable rather than enumerating failure modes.
    /// </summary>
    private static bool Z3Available()
    {
        if (_z3Available.HasValue) return _z3Available.Value;
        try
        {
            using var probe = new Context();
            _ = probe.MkTrue();
            _z3Available = true;
        }
        catch (Exception)
        {
            _z3Available = false;
        }

        return _z3Available.Value;
    }

    /// <summary>Recursive AST → Z3 translation; variable consts are shared via <paramref name="variables" />.</summary>
    private static BoolExpr ToZ3(Context ctx, AstNode node, Dictionary<string, BoolExpr> variables)
    {
        return node switch
        {
            VariableNode v => variables.TryGetValue(v.Name, out var known)
                ? known
                : variables[v.Name] = ctx.MkBoolConst(v.Name),
            ConstantNode c => c.Value ? ctx.MkTrue() : ctx.MkFalse(),
            NotNode n => ctx.MkNot(ToZ3(ctx, n.Operand, variables)),
            AndNode a => ctx.MkAnd(ToZ3(ctx, a.Left, variables), ToZ3(ctx, a.Right, variables)),
            OrNode o => ctx.MkOr(ToZ3(ctx, o.Left, variables), ToZ3(ctx, o.Right, variables)),
            _ => throw new NotSupportedException($"Unexpected node type {node.GetType().Name}")
        };
    }

    /// <summary>Z3 verdict on whether two formulas are equivalent (Not(Iff) is UNSAT).</summary>
    private static Status CheckNonEquivalence(Context ctx, AstNode left, AstNode right)
    {
        var variables = new Dictionary<string, BoolExpr>();
        var solver = ctx.MkSolver();
        solver.Assert(ctx.MkNot(ctx.MkIff(ToZ3(ctx, left, variables), ToZ3(ctx, right, variables))));
        return solver.Check();
    }

    private static BoolExpr ClauseToZ3(Context ctx, int[] clause, BoolExpr[] variablesByIndex)
    {
        return ctx.MkOr(clause
            .Select(literal => literal > 0
                ? variablesByIndex[literal - 1]
                : ctx.MkNot(variablesByIndex[-literal - 1]))
            .ToArray());
    }

    [Fact]
    public void Optimizer_EquivalenceConfirmedByZ3()
    {
        if (!Z3Available()) return;

        var rng = new Random(20260724);
        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions
        {
            ComputeCnf = false,
            ComputeDnf = false,
            ComputeAdvancedForms = false
        };

        using var ctx = new Context();
        for (var trial = 0; trial < 60; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(1, 9), rng.Next(1, 6), allowConstants: true);
            var optimized = optimizer.OptimizeExpression(expression, options).Optimized;

            var verdict = CheckNonEquivalence(ctx, RandomExpressions.Parse(expression),
                RandomExpressions.Parse(optimized));
            Assert.True(Status.UNSATISFIABLE == verdict,
                $"Trial {trial}: Z3 refutes equivalence of '{expression}' and optimized '{optimized}' ({verdict})");
        }
    }

    [Fact]
    public void SatSolver_VerdictAgreesWithZ3()
    {
        if (!Z3Available()) return;

        var rng = new Random(1902);
        using var ctx = new Context();
        for (var trial = 0; trial < 100; trial++)
        {
            var variableCount = rng.Next(1, 13);
            var clauseCount = rng.Next(1, 41);
            var maxWidth = Math.Min(3, 2 * variableCount);
            var clauses = new List<int[]>();
            for (var i = 0; i < clauseCount; i++)
            {
                var width = rng.Next(1, maxWidth + 1);
                var clause = new int[width];
                for (var j = 0; j < width; j++)
                {
                    var variable = rng.Next(1, variableCount + 1);
                    clause[j] = rng.Next(2) == 0 ? variable : -variable;
                }

                clauses.Add(clause);
            }

            var ourSolver = new SatSolver(variableCount);
            foreach (var clause in clauses) ourSolver.AddClause(clause);
            var ourVerdict = ourSolver.Solve();

            var z3Variables = Enumerable.Range(1, variableCount)
                .Select(i => ctx.MkBoolConst($"x{i}"))
                .ToArray();
            var z3Solver = ctx.MkSolver();
            foreach (var clause in clauses) z3Solver.Assert(ClauseToZ3(ctx, clause, z3Variables));
            var z3Verdict = z3Solver.Check();

            Assert.NotEqual(SatResult.Unknown, ourVerdict);
            Assert.NotEqual(Status.UNKNOWN, z3Verdict);
            Assert.True((ourVerdict == SatResult.Satisfiable) == (z3Verdict == Status.SATISFIABLE),
                $"Trial {trial}: SatSolver says {ourVerdict}, Z3 says {z3Verdict} " +
                $"({variableCount} vars, {clauseCount} clauses)");
        }
    }

    [Fact]
    public void EquivalenceChecker_CounterexampleConfirmedByZ3()
    {
        if (!Z3Available()) return;

        var rng = new Random(424242);
        using var ctx = new Context();
        var confirmed = 0;
        for (var trial = 0; trial < 40; trial++)
        {
            var variableCount = rng.Next(1, 9);
            var left = RandomExpressions.Parse(RandomExpressions.Generate(rng, variableCount, rng.Next(1, 6)));
            var right = RandomExpressions.Parse(RandomExpressions.Generate(rng, variableCount, rng.Next(1, 6)));

            var result = EquivalenceChecker.Check(left, right);
            if (result.AreEquivalent != false) continue; // only counterexample verdicts are cross-checked

            Assert.NotNull(result.Counterexample);
            var verdict = CheckNonEquivalence(ctx, left, right);
            Assert.True(Status.SATISFIABLE == verdict,
                $"Trial {trial}: our checker refutes equivalence of '{left}' and '{right}' but Z3 says {verdict}");
            confirmed++;
        }

        Assert.True(confirmed > 0, "Every trial produced equivalent pairs; the seed no longer exercises the oracle");
    }

    [Fact]
    public void TseitinAndPlaistedGreenbaum_EquisatisfiableByZ3()
    {
        if (!Z3Available()) return;

        var rng = new Random(77_1024);
        using var ctx = new Context();
        for (var trial = 0; trial < 40; trial++)
        {
            var ast = RandomExpressions.Parse(
                RandomExpressions.Generate(rng, rng.Next(1, 9), rng.Next(1, 6), allowConstants: true));

            var originalSolver = ctx.MkSolver();
            originalSolver.Assert(ToZ3(ctx, ast, new Dictionary<string, BoolExpr>()));
            var originalVerdict = originalSolver.Check();
            Assert.NotEqual(Status.UNKNOWN, originalVerdict);

            foreach (var style in new[] { CnfEncodingStyle.Tseitin, CnfEncodingStyle.PlaistedGreenbaum })
            {
                var cnf = TseitinConverter.Convert(ast, style);
                var cnfVariables = Enumerable.Range(1, cnf.TotalVariableCount)
                    .Select(i => ctx.MkBoolConst($"z{trial}_{style}_{i}"))
                    .ToArray();
                var cnfSolver = ctx.MkSolver();
                foreach (var clause in cnf.Clauses) cnfSolver.Assert(ClauseToZ3(ctx, clause, cnfVariables));
                var cnfVerdict = cnfSolver.Check();

                Assert.True(originalVerdict == cnfVerdict,
                    $"Trial {trial}: {style} CNF of '{ast}' is {cnfVerdict} but Z3 finds the formula {originalVerdict}");
            }
        }
    }
}
