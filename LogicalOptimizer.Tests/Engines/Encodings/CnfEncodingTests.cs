using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Plaisted–Greenbaum polarity encoding vs full Tseitin: both must be
///     equisatisfiable with the formula, both must project models onto the inputs
///     correctly, and PG must never emit more clauses than Tseitin.
/// </summary>
public class CnfEncodingTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static bool BruteForceSatisfiableFormula(AstNode ast)
    {
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        var assignment = new Dictionary<string, bool>();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (m & (1 << j)) != 0;
            if (TruthTable.Evaluate(ast, assignment)) return true;
        }

        return false;
    }

    [Fact]
    public void PlaistedGreenbaum_RandomFormulas_EquisatisfiableAndModelsProject()
    {
        var rng = new Random(616);
        for (var trial = 0; trial < 150; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 5, rng.Next(1, 6), allowConstants: true);
            var ast = Parse(expression);

            var pg = TseitinConverter.Convert(ast, CnfEncodingStyle.PlaistedGreenbaum);
            var solver = SatSolver.FromCnf(pg);
            var verdict = solver.Solve();
            var expected = BruteForceSatisfiableFormula(ast);

            Assert.True((verdict == SatResult.Satisfiable) == expected,
                $"Trial {trial}: PG verdict {verdict} vs oracle {expected} for '{expression}'");

            if (verdict != SatResult.Satisfiable) continue;

            // The PG model restricted to inputs must satisfy the original formula
            var assignment = new Dictionary<string, bool>();
            for (var i = 1; i <= pg.InputVariables.Count; i++)
                assignment[pg.InputVariables[i - 1]] = solver.GetValue(i);
            Assert.True(TruthTable.Evaluate(ast, assignment),
                $"Trial {trial}: PG model does not project for '{expression}'");
        }
    }

    [Fact]
    public void PlaistedGreenbaum_NeverEmitsMoreClausesThanTseitin()
    {
        var rng = new Random(717);
        long tseitinTotal = 0, pgTotal = 0;
        for (var trial = 0; trial < 100; trial++)
        {
            var ast = Parse(RandomExpressions.Generate(rng, 5, rng.Next(1, 6)));
            var tseitin = TseitinConverter.Convert(ast, CnfEncodingStyle.Tseitin);
            var pg = TseitinConverter.Convert(ast, CnfEncodingStyle.PlaistedGreenbaum);

            Assert.True(pg.Clauses.Count <= tseitin.Clauses.Count,
                $"Trial {trial}: PG produced MORE clauses ({pg.Clauses.Count} > {tseitin.Clauses.Count})");
            tseitinTotal += tseitin.Clauses.Count;
            pgTotal += pg.Clauses.Count;
        }

        // The whole point of PG: on negation-poor formulas it saves a large fraction
        Assert.True(pgTotal < tseitinTotal,
            $"PG produced no aggregate saving ({pgTotal} vs {tseitinTotal})");
    }

    [Fact]
    public void PlaistedGreenbaum_PurePositiveFormula_EmitsOnlyOneDirection()
    {
        // (a & b) | (c & d) with no negations: every gate occurs positively,
        // so each AND gate contributes 2 clauses and the OR gate 1 (+ root unit)
        var pg = TseitinConverter.Convert(Parse("a & b | c & d"), CnfEncodingStyle.PlaistedGreenbaum);
        var tseitin = TseitinConverter.Convert(Parse("a & b | c & d"), CnfEncodingStyle.Tseitin);

        Assert.Equal(6, pg.Clauses.Count);      // 2 + 2 + 1 + root
        Assert.Equal(10, tseitin.Clauses.Count); // 3 + 3 + 3 + root
    }

    [Fact]
    public void PlaistedGreenbaum_SharedSubtreeWithMixedPolarity_GetsBothDirections()
    {
        // (a & b) | !(a & b) is a tautology; the shared AND occurs with both
        // polarities, so PG must emit both directions and stay satisfiable while
        // the negated formula is UNSAT
        var formula = Parse("(a & b) | !(a & b)");
        var solver = SatSolver.FromCnf(TseitinConverter.Convert(formula, CnfEncodingStyle.PlaistedGreenbaum));
        Assert.Equal(SatResult.Satisfiable, solver.Solve());

        var negated = Parse("!((a & b) | !(a & b))");
        var negSolver = SatSolver.FromCnf(TseitinConverter.Convert(negated, CnfEncodingStyle.PlaistedGreenbaum));
        Assert.Equal(SatResult.Unsatisfiable, negSolver.Solve());
    }

    [Fact]
    public void FullGrid_StyleByOperatorByPolarity_AllCellsEquisatisfiable()
    {
        // Combinatorial grid over the three interacting axes of the encoder:
        // encoding style (2) x connective (7) x outer polarity (2) = 28 cells, each
        // checked for equisatisfiability against the brute-force oracle AND for the
        // PG-never-larger clause guarantee within each (operator, polarity) pair
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var connectives = new (string Name, AstNode Node)[]
        {
            ("And", new AndNode(a, b)),
            ("Or", new OrNode(a, b)),
            ("Xor", new XorNode(a, b)),
            ("Eqv", new EqvNode(a, b)),
            ("Imp", new ImpNode(a, b)),
            ("Nand", new NandNode(a, b)),
            ("Nor", new NorNode(a, b))
        };

        foreach (var (name, node) in connectives)
            foreach (var negated in new[] { false, true })
            {
                AstNode formula = negated ? new NotNode(node) : node;
                var expected = BruteForceSatisfiableFormula(formula);
                var clauseCounts = new Dictionary<CnfEncodingStyle, int>();

                foreach (var style in new[] { CnfEncodingStyle.Tseitin, CnfEncodingStyle.PlaistedGreenbaum })
                {
                    var cnf = TseitinConverter.Convert(formula, style);
                    clauseCounts[style] = cnf.Clauses.Count;
                    var verdict = SatSolver.FromCnf(cnf).Solve();
                    Assert.True((verdict == SatResult.Satisfiable) == expected,
                        $"{style} x {name} x negated={negated}: verdict {verdict}, oracle {expected}");
                }

                Assert.True(clauseCounts[CnfEncodingStyle.PlaistedGreenbaum]
                            <= clauseCounts[CnfEncodingStyle.Tseitin],
                    $"{name} x negated={negated}: PG emitted more clauses than Tseitin");
            }
    }

    [Fact]
    public void PlaistedGreenbaum_ExtendedOperators_StayEquisatisfiable()
    {
        // XOR/EQV/IMP/NAND/NOR lower onto the same gate encoder; check contradictions
        // and tautologies built from them
        var xorContradiction = new AndNode(
            new XorNode(new VariableNode("a"), new VariableNode("b")),
            new EqvNode(new VariableNode("a"), new VariableNode("b")));
        var solver = SatSolver.FromCnf(
            TseitinConverter.Convert(xorContradiction, CnfEncodingStyle.PlaistedGreenbaum));
        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());

        var impChain = new ImpNode(new VariableNode("a"), new ImpNode(new VariableNode("b"), new VariableNode("a")));
        var negatedTautology = new NotNode(impChain);
        var impSolver = SatSolver.FromCnf(
            TseitinConverter.Convert(negatedTautology, CnfEncodingStyle.PlaistedGreenbaum));
        Assert.Equal(SatResult.Unsatisfiable, impSolver.Solve());
    }
}
