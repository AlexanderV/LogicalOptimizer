using System.Text;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     In-process deterministic fuzzing (seed-fixed, reproducible). Three styles:
///     garbage-input fuzzing of the lexer/parser (only ArgumentException is an
///     acceptable failure), mutation fuzzing of valid expressions, and generative
///     grammar fuzzing of the full pipeline with an equivalence oracle.
/// </summary>
public class FuzzingTests
{
    private static readonly OptimizationOptions ResultOnly = new()
    { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

    /// <summary>Parse must either succeed or throw ArgumentException — nothing else.</summary>
    private static void AssertControlledParse(string input, string context)
    {
        try
        {
            new Parser(new Lexer(input).Tokenize()).Parse();
        }
        catch (ArgumentException)
        {
            // The documented rejection path
        }
        catch (Exception ex)
        {
            Assert.Fail($"{context}: input {Printable(input)} escaped with {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Printable(string input)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in input)
            builder.Append(c < ' ' || c > '~' ? $"\\u{(int)c:X4}" : c.ToString());
        return builder.Append('"').ToString();
    }

    [Fact]
    public void GarbageFuzz_ParserNeverEscapesControlledFailure()
    {
        // Random strings over a hostile alphabet: operators, digits, unicode, controls
        const string alphabet = "ab01!&|()_ \t\r\n~@#$%^*+-=[]{};:'\",.<>/?\\`éЖ中\0";
        var rng = new Random(90001);
        for (var trial = 0; trial < 3000; trial++)
        {
            var length = rng.Next(0, 40);
            var builder = new StringBuilder(length);
            for (var i = 0; i < length; i++) builder.Append(alphabet[rng.Next(alphabet.Length)]);
            AssertControlledParse(builder.ToString(), $"Garbage trial {trial}");
        }
    }

    [Fact]
    public void MutationFuzz_DamagedValidExpressionsFailControlled()
    {
        // Take a valid expression, damage it at random positions: the parser must
        // accept or reject cleanly, never crash with an unexpected exception type
        const string atoms = "ab01!&|() ";
        var rng = new Random(90002);
        for (var trial = 0; trial < 1500; trial++)
        {
            var chars = new StringBuilder(RandomExpressions.Generate(rng, 4, 3, allowConstants: true));
            var mutations = rng.Next(1, 4);
            for (var i = 0; i < mutations && chars.Length > 0; i++)
                switch (rng.Next(3))
                {
                    case 0:
                        chars[rng.Next(chars.Length)] = atoms[rng.Next(atoms.Length)];
                        break;
                    case 1:
                        chars.Insert(rng.Next(chars.Length + 1), atoms[rng.Next(atoms.Length)]);
                        break;
                    default:
                        chars.Remove(rng.Next(chars.Length), 1);
                        break;
                }

            AssertControlledParse(chars.ToString(), $"Mutation trial {trial}");
        }
    }

    [Fact]
    public void GrammarFuzz_FullPipelinePreservesSemantics()
    {
        // Valid random inputs through the WHOLE optimizer, verified by the truth-table oracle
        var rng = new Random(90003);
        var optimizer = new BooleanExpressionOptimizer();
        for (var trial = 0; trial < 150; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(2, 9), rng.Next(1, 6),
                allowConstants: true);

            var result = optimizer.OptimizeExpression(expression, ResultOnly);

            Assert.True(TruthTable.AreEquivalent(expression, result.Optimized),
                $"Trial {trial}: semantics broken for '{expression}' -> '{result.Optimized}'");
        }
    }

    [Fact]
    public void DeepNesting_ParsesUpToLimitAndRejectsBeyondWithoutStackOverflow()
    {
        // The parser documents a nesting cap; below it must work, far above it must
        // throw ArgumentException instead of dying with StackOverflowException
        var shallow = new string('(', 100) + "a" + new string(')', 100);
        Assert.Equal("a", RandomExpressions.Parse(shallow).ToString());

        var tooDeep = new string('(', 100_000) + "a" + new string(')', 100_000);
        Assert.Throws<ArgumentException>(() => RandomExpressions.Parse(tooDeep));

        var negations = new string('!', 100_000) + "a";
        Assert.Throws<ArgumentException>(() => RandomExpressions.Parse(negations));
    }

    [Fact]
    public void WideExpressions_LongFlatChainsStayLinear()
    {
        // 1400-term flat chains (inside the documented 10k-char input cap): must parse
        // and optimize without quadratic blowup or crash
        var wideOr = string.Join(" | ", Enumerable.Range(0, 1400).Select(i => $"v{i % 50}"));
        var wideAnd = string.Join(" & ", Enumerable.Range(0, 1400).Select(i => $"v{i % 50}"));

        var optimizer = new BooleanExpressionOptimizer();
        var orResult = optimizer.OptimizeExpression(wideOr, ResultOnly);
        var andResult = optimizer.OptimizeExpression(wideAnd, ResultOnly);

        // Idempotence collapses each chain to the 50 distinct variables
        Assert.Equal(50, RandomExpressions.Parse(orResult.Optimized).GetVariables().Count);
        Assert.Equal(50, RandomExpressions.Parse(andResult.Optimized).GetVariables().Count);

        // The documented input cap itself is a contract: over-limit input must be
        // rejected loudly, not truncated
        var overLimit = string.Join(" | ", Enumerable.Range(0, 2000).Select(i => $"v{i % 50}"));
        Assert.Throws<ArgumentException>(() => optimizer.OptimizeExpression(overLimit, ResultOnly));
    }

    [Fact]
    public void CsvFuzz_CorruptedTablesFailControlled()
    {
        // Structured-input fuzzing: damage a valid CSV truth table in random ways;
        // the parser must throw ArgumentException, never index/null/format exceptions
        var rng = new Random(90004);
        const string valid = "a,b,Result\n0,0,0\n0,1,1\n1,0,1\n1,1,0";
        const string atoms = "ab01,\nResult;x-";

        for (var trial = 0; trial < 800; trial++)
        {
            var chars = new StringBuilder(valid);
            var mutations = rng.Next(1, 5);
            for (var i = 0; i < mutations && chars.Length > 0; i++)
                switch (rng.Next(3))
                {
                    case 0:
                        chars[rng.Next(chars.Length)] = atoms[rng.Next(atoms.Length)];
                        break;
                    case 1:
                        chars.Insert(rng.Next(chars.Length + 1), atoms[rng.Next(atoms.Length)]);
                        break;
                    default:
                        chars.Remove(rng.Next(chars.Length), 1);
                        break;
                }

            try
            {
                CsvTruthTableParser.ParseCsvToExpression(chars.ToString());
            }
            catch (ArgumentException)
            {
                // Documented rejection
            }
            catch (Exception ex)
            {
                Assert.Fail($"CSV trial {trial}: {Printable(chars.ToString())} escaped with {ex.GetType().Name}");
            }
        }
    }

    [Fact]
    public void BddOperationChainFuzz_RandomOpSequencesMatchBruteForce()
    {
        // Random chains of Restrict/Exists/ForAll/Negate applied to a random function,
        // with a parallel brute-force model of the same chain as the oracle
        var rng = new Random(90006);
        for (var trial = 0; trial < 60; trial++)
        {
            var expression = RandomExpressions.Generate(rng, 4, 3);
            var ast = RandomExpressions.Parse(expression);
            var bdd = BinaryDecisionDiagram.Build(ast);
            var variables = bdd.Variables.ToList();
            if (variables.Count == 0) continue;

            var node = bdd.Root;
            Func<Dictionary<string, bool>, bool> reference =
                a => TruthTable.Evaluate(ast, a);

            for (var step = 0; step < rng.Next(1, 5); step++)
            {
                var x = variables[rng.Next(variables.Count)];
                var previous = reference;
                switch (rng.Next(4))
                {
                    case 0:
                        var value = rng.Next(2) == 0;
                        node = bdd.Restrict(node, x, value);
                        reference = a => previous(new Dictionary<string, bool>(a) { [x] = value });
                        break;
                    case 1:
                        node = bdd.Exists(node, x);
                        reference = a => previous(new Dictionary<string, bool>(a) { [x] = false })
                                     || previous(new Dictionary<string, bool>(a) { [x] = true });
                        break;
                    case 2:
                        node = bdd.ForAll(node, x);
                        reference = a => previous(new Dictionary<string, bool>(a) { [x] = false })
                                     && previous(new Dictionary<string, bool>(a) { [x] = true });
                        break;
                    default:
                        node = bdd.Negate(node);
                        reference = a => !previous(a);
                        break;
                }
            }

            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 1 << variables.Count; m++)
            {
                for (var j = 0; j < variables.Count; j++)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
                Assert.True(bdd.Evaluate(node, assignment) == reference(assignment),
                    $"Trial {trial}: op chain diverged at mask {m} for '{expression}'");
            }
        }
    }

    [Fact]
    public void EspressoFuzz_HostileCoversStaySound()
    {
        // Covers with duplicate cubes, contained cubes, complementary pairs and unit
        // cubes; ≤5 vars so brute-force equivalence is the oracle
        var rng = new Random(90007);
        for (var trial = 0; trial < 150; trial++)
        {
            var terms = new List<string>();
            var termCount = rng.Next(1, 10);
            for (var t = 0; t < termCount; t++)
            {
                var width = rng.Next(1, 4);
                var literals = Enumerable.Range(0, width)
                    .Select(_ => $"{(rng.Next(2) == 0 ? "!" : "")}v{rng.Next(5)}");
                terms.Add(string.Join(" & ", literals));
            }

            // Deliberately duplicate a random term sometimes
            if (rng.Next(2) == 0) terms.Add(terms[rng.Next(terms.Count)]);

            var sop = string.Join(" | ", terms);
            AstNode ast;
            try
            {
                ast = RandomExpressions.Parse(sop);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var minimized = Transformations.MinimizeDnfHeuristic(ast);
            Assert.True(TruthTable.AreEquivalent(ast, minimized),
                $"Trial {trial}: espresso unsound on '{sop}' -> '{minimized}'");
            Assert.True(AstMetrics.CountLiterals(minimized) <= AstMetrics.CountLiterals(ast),
                $"Trial {trial}: espresso worsened '{sop}'");
        }
    }

    [Fact]
    public void FormulaFactoryFuzz_DeepRandomConstruction_NeverProducesRedundantShapes()
    {
        // Random construction sequences: the factory invariants (no nested same-op
        // when flattenable, no duplicate operands, no constants inside connectives)
        // must hold for every produced formula
        var rng = new Random(90008);
        var factory = new FormulaFactory();
        for (var trial = 0; trial < 300; trial++)
        {
            var formula = RandomFactoryFormula(factory, rng, 4);
            AssertFactoryInvariants(formula);
        }
    }

    private static AstNode RandomFactoryFormula(FormulaFactory factory, Random rng, int depth)
    {
        if (depth <= 0 || rng.Next(4) == 0)
        {
            var leaf = rng.Next(8) == 0
                ? rng.Next(2) == 0 ? factory.True : factory.False
                : factory.Variable($"v{rng.Next(4)}");
            return rng.Next(3) == 0 ? factory.Not(leaf) : leaf;
        }

        var operands = Enumerable.Range(0, rng.Next(2, 5))
            .Select(_ => RandomFactoryFormula(factory, rng, depth - 1))
            .ToArray();
        return rng.Next(2) == 0 ? factory.And(operands) : factory.Or(operands);
    }

    private static void AssertFactoryInvariants(AstNode node)
    {
        switch (node)
        {
            case AndNode and:
                foreach (var term in FlattenSame<AndNode>(and, n => (n.Left, n.Right)))
                {
                    Assert.False(term is ConstantNode, $"constant inside AND chain: {node}");
                    AssertFactoryInvariants(term);
                }

                break;
            case OrNode or:
                foreach (var term in FlattenSame<OrNode>(or, n => (n.Left, n.Right)))
                {
                    Assert.False(term is ConstantNode, $"constant inside OR chain: {node}");
                    AssertFactoryInvariants(term);
                }

                break;
            case NotNode not:
                Assert.False(not.Operand is NotNode, $"double negation survived: {node}");
                Assert.False(not.Operand is ConstantNode, $"negated constant survived: {node}");
                AssertFactoryInvariants(not.Operand);
                break;
        }
    }

    private static IEnumerable<AstNode> FlattenSame<T>(T root, Func<T, (AstNode, AstNode)> children)
        where T : AstNode
    {
        var (left, right) = children(root);
        foreach (var side in new[] { left, right })
            if (side is T same)
                foreach (var inner in FlattenSame(same, children))
                    yield return inner;
            else
                yield return side;
    }

    [Fact]
    public void SatSolverFuzz_RandomCnfAgreesWithBruteForce()
    {
        // Random small CNFs: the industrial solver vs naive 2^n enumeration
        var rng = new Random(90005);
        for (var trial = 0; trial < 200; trial++)
        {
            var variableCount = rng.Next(1, 9);
            var clauseCount = rng.Next(1, 20);
            var clauses = new List<int[]>();
            for (var c = 0; c < clauseCount; c++)
            {
                // Width is capped by the number of DISTINCT literals available
                // (2 per variable), otherwise the generator can never fill the set
                var width = rng.Next(1, Math.Min(3, 2 * variableCount) + 1);
                var clause = new HashSet<int>();
                while (clause.Count < width)
                {
                    var v = rng.Next(1, variableCount + 1);
                    clause.Add(rng.Next(2) == 0 ? v : -v);
                }

                clauses.Add(clause.ToArray());
            }

            var solver = new SatSolver(variableCount);
            foreach (var clause in clauses) solver.AddClause(clause);
            var verdict = solver.Solve();

            var bruteForceSat = false;
            for (var m = 0; m < 1 << variableCount && !bruteForceSat; m++)
            {
                var all = clauses.All(clause => clause.Any(literal =>
                    (m & (1 << (Math.Abs(literal) - 1))) != 0 == literal > 0));
                bruteForceSat = all;
            }

            Assert.Equal(bruteForceSat ? SatResult.Satisfiable : SatResult.Unsatisfiable, verdict);

            // The reported model must actually satisfy every clause
            if (verdict == SatResult.Satisfiable)
                Assert.True(clauses.All(clause => clause.Any(literal =>
                        solver.GetValue(Math.Abs(literal)) == literal > 0)),
                    $"Trial {trial}: solver returned a non-model");
        }
    }
}
