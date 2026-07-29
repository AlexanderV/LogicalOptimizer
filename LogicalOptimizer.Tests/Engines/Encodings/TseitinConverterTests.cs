using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests for the Tseitin transformation: for every input assignment the CNF must be
///     satisfiable by some extension to the auxiliary variables exactly when the original
///     formula is true (checked exhaustively for small expressions).
/// </summary>
public class TseitinConverterTests
{
    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    private static void AssertEquisatisfiablePerAssignment(AstNode ast)
    {
        var cnf = TseitinConverter.Convert(ast);
        var variables = cnf.InputVariables;
        var assignment = new Dictionary<string, bool>();

        for (var inputMask = 0; inputMask < 1 << variables.Count; inputMask++)
        {
            var inputValues = new bool[variables.Count];
            for (var j = 0; j < variables.Count; j++)
            {
                inputValues[j] = (inputMask & (1 << j)) != 0;
                assignment[variables[j]] = inputValues[j];
            }

            var formulaValue = TruthTable.Evaluate(ast, assignment);
            var cnfSatisfiable = ExistsAuxExtension(cnf, inputValues);

            Assert.True(formulaValue == cnfSatisfiable,
                $"Tseitin mismatch for '{ast}' under mask {inputMask}: formula={formulaValue}, cnf={cnfSatisfiable}");
        }
    }

    private static bool ExistsAuxExtension(TseitinCnf cnf, bool[] inputValues)
    {
        var auxCount = cnf.AuxiliaryVariableCount;
        Assert.True(auxCount <= 20, "Test helper is exhaustive; keep expressions small");

        for (long auxMask = 0; auxMask < 1L << auxCount; auxMask++)
        {
            var satisfied = true;
            foreach (var clause in cnf.Clauses)
            {
                var clauseTrue = false;
                foreach (var literal in clause)
                {
                    var index = Math.Abs(literal);
                    var value = index <= inputValues.Length
                        ? inputValues[index - 1]
                        : (auxMask & (1L << (index - inputValues.Length - 1))) != 0;
                    if (literal < 0) value = !value;
                    if (value)
                    {
                        clauseTrue = true;
                        break;
                    }
                }

                if (!clauseTrue)
                {
                    satisfied = false;
                    break;
                }
            }

            if (satisfied) return true;
        }

        return false;
    }

    [Theory]
    [InlineData("a")]
    [InlineData("!a")]
    [InlineData("a & b | a & !b")]
    [InlineData("(a | b) & (!a | c)")]
    [InlineData("a & (b | c) & !d")]
    [InlineData("!(a & b) | !(c | d)")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("a & 1 | b & 0")]
    public void Convert_BasicOperators_Equisatisfiable(string expression)
    {
        AssertEquisatisfiablePerAssignment(Parse(expression));
    }

    [Fact]
    public void Convert_ExtendedOperators_Equisatisfiable()
    {
        var a = new VariableNode("a");
        var b = new VariableNode("b");
        var c = new VariableNode("c");

        AssertEquisatisfiablePerAssignment(new XorNode(a, b));
        AssertEquisatisfiablePerAssignment(new EqvNode(a, b));
        AssertEquisatisfiablePerAssignment(new NandNode(a, b));
        AssertEquisatisfiablePerAssignment(new NorNode(a, b));
        AssertEquisatisfiablePerAssignment(new ImpNode(a, b));
        AssertEquisatisfiablePerAssignment(
            new XorNode(new EqvNode(a, b), new ImpNode(new NandNode(a, c), new NorNode(b, c))));
    }

    [Fact]
    public void Convert_SharedSubtrees_ReuseGateVariables()
    {
        // (a & b) | !(a & b): the shared conjunction must be encoded once
        var shared = new AndNode(new VariableNode("a"), new VariableNode("b"));
        var cnf = TseitinConverter.Convert(new OrNode(shared, new NotNode(shared)));

        // 1 AND-gate + 1 OR-gate; without sharing there would be a third gate
        Assert.Equal(2, cnf.AuxiliaryVariableCount);
    }

    [Fact]
    public void Convert_NaryAndGate_HasPinnedClauseAndAuxCounts()
    {
        // "a & b & c" stays a single 3-input n-ary AND gate: one auxiliary variable,
        // and 3 positive clauses (-g, x_i) + 1 negative clause (g, -a, -b, -c) + the
        // root unit clause = 5 clauses. (CanonicalInvariantTests pins the 4-input
        // sibling at 1 aux / 6 clauses; the count here includes the root unit too.)
        var cnf = TseitinConverter.Convert(Parse("a & b & c"));

        Assert.Equal(1, cnf.AuxiliaryVariableCount);
        Assert.Equal(5, cnf.Clauses.Count);
    }

    [Fact]
    public void Convert_XorChain_LinearSize()
    {
        // XOR chain over 24 variables: equivalent CNF has 2^23 clauses, Tseitin stays linear
        AstNode chain = new VariableNode("v01");
        for (var i = 2; i <= 24; i++)
            chain = new XorNode(chain, new VariableNode($"v{i:D2}"));

        var cnf = TseitinConverter.Convert(chain);

        Assert.Equal(24, cnf.InputVariables.Count);
        Assert.Equal(23, cnf.AuxiliaryVariableCount);
        Assert.Equal(23 * 4 + 1, cnf.Clauses.Count);
    }

    [Fact]
    public void ToDimacs_HeaderMatchesCounts()
    {
        var cnf = TseitinConverter.Convert(Parse("a & b | c"));
        var dimacs = cnf.ToDimacs();

        Assert.Contains($"p cnf {cnf.TotalVariableCount} {cnf.Clauses.Count}", dimacs);
        Assert.Contains("c 1 = a", dimacs);
        var clauseLines = dimacs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimEnd().EndsWith(" 0"));
        Assert.Equal(cnf.Clauses.Count, clauseLines);
    }

    [Fact]
    public void ToAst_RoundTripsThroughParser()
    {
        var cnf = TseitinConverter.Convert(Parse("a & b | !c"));
        var reparsed = Parse(cnf.ToAst().ToString());

        Assert.Equal(cnf.TotalVariableCount, reparsed.GetVariables().Count);

        // The reparsed AST must evaluate exactly as the clause set on every assignment
        // over all variables (inputs and Tseitin auxiliaries alike)
        var variableCount = cnf.TotalVariableCount;
        var assignment = new Dictionary<string, bool>();
        for (var mask = 0; mask < 1 << variableCount; mask++)
        {
            var values = new bool[variableCount + 1];
            for (var v = 1; v <= variableCount; v++)
            {
                values[v] = (mask & (1 << (v - 1))) != 0;
                assignment[cnf.VariableName(v)] = values[v];
            }

            var clausesValue = cnf.Clauses.All(clause =>
                clause.Any(literal => literal > 0 ? values[literal] : !values[-literal]));

            Assert.True(clausesValue == TruthTable.Evaluate(reparsed, assignment),
                $"Round-tripped CNF AST disagrees with clauses under mask {mask}");
        }
    }

    [Fact]
    public void VariableName_MapsInputsThenAuxiliaries()
    {
        var cnf = TseitinConverter.Convert(Parse("b & a"));

        Assert.Equal("a", cnf.VariableName(1));
        Assert.Equal("b", cnf.VariableName(2));
        Assert.Equal("_t1", cnf.VariableName(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => cnf.VariableName(0));
    }

    [Fact]
    public void OptimizeExpression_TseitinMode_NeverTooLarge()
    {
        // 26 variables: far beyond the exact threshold; distribution-based CNF would give up
        var terms = Enumerable.Range(1, 13).Select(i => $"x{i:D2} & y{i:D2}");
        var expression = string.Join(" | ", terms);

        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions { CnfMode = CnfMode.Tseitin, ComputeDnf = false, ComputeAdvancedForms = false });

        Assert.Equal(ComputationStatus.Computed, result.CnfStatus);
        Assert.NotEqual("-", result.CNF);
        Assert.Contains("_t", result.CNF);
    }

    [Fact]
    public void ToEquisatisfiableCnf_PublicApi_ReturnsLinearCnf()
    {
        var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf("a & b | c & d");

        Assert.Equal(4, cnf.InputVariables.Count);
        // 3 aux gates (a&b, c&d, the OR) → exactly 10 clauses. Pin it: the `<= 3*aux+1`
        // linear bound is also satisfied by a wastefully larger (still-linear) encoding.
        Assert.Equal(3, cnf.AuxiliaryVariableCount);
        Assert.Equal(10, cnf.Clauses.Count);
    }

    [Fact]
    public void ToDimacs_Exporter_FallsBackToTseitinOnBlowup()
    {
        // OR of 16 AND-pairs: distribution needs 2^16 clauses, NormalFormConverter aborts, Tseitin takes over
        var expression = string.Join(" | ", Enumerable.Range(1, 16).Select(i => $"x{i:D2} & y{i:D2}"));
        var dimacs = BooleanExpressionExporter.ToDimacs(expression);

        Assert.Contains("Tseitin", dimacs);
        Assert.Contains("p cnf", dimacs);
    }
}
