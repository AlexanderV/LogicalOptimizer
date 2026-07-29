using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     DIMACS CNF parser, round-trip writer and engine hand-off. Correctness is checked
///     against an independent 2^n brute-force oracle, never the parser against itself.
/// </summary>
public class DimacsFormatTests
{
    // Tiny official-style corpus sample: a 3-variable CNF, comments and a p-header, parsed
    // with NO preprocessing.
    private const string SampleCnf =
        "c sample DIMACS instance\n" +
        "c two clauses over three variables\n" +
        "p cnf 3 2\n" +
        "1 -3 0\n" +
        "2 3 -1 0\n";

    [Fact]
    public void Parse_ReadsHeaderCommentsAndClauses()
    {
        var problem = DimacsParser.Parse(new StringReader(SampleCnf));

        Assert.Equal(3, problem.VariableCount);
        Assert.Equal(2, problem.Clauses.Count);
        Assert.Equal(new[] { 1, -3 }, problem.Clauses[0]);
        Assert.Equal(new[] { 2, 3, -1 }, problem.Clauses[1]);
    }

    [Fact]
    public void Parse_ClauseSpanningMultipleLines_IsOneClause()
    {
        var problem = DimacsParser.Parse(new StringReader("p cnf 3 1\n1 2\n-3\n0\n"));

        Assert.Single(problem.Clauses);
        Assert.Equal(new[] { 1, 2, -3 }, problem.Clauses[0]);
    }

    [Theory]
    [InlineData("p cnf 2 2\n1 2 0\n-1 -2 0\n")] // satisfiable
    [InlineData("p cnf 1 2\n1 0\n-1 0\n")] // unsatisfiable
    [InlineData("p cnf 3 4\n1 2 0\n-1 3 0\n-2 -3 0\n1 -3 0\n")]
    public void Solve_AgreesWithBruteForceOracle(string text)
    {
        var problem = DimacsParser.Parse(new StringReader(text));
        var clauses = problem.Clauses.Select(c => c.ToArray()).ToList();
        var expected = SatTestOracles.BruteForceSatisfiable(problem.VariableCount, clauses);

        var verdict = problem.Solve();

        Assert.Equal(expected ? SatResult.Satisfiable : SatResult.Unsatisfiable, verdict);
    }

    [Fact]
    public void RoundTrip_ParseWriteParse_PreservesSemantics()
    {
        var original = DimacsParser.Parse(new StringReader(SampleCnf));

        var writer = new StringWriter();
        original.Write(writer);
        var reparsed = DimacsParser.Parse(new StringReader(writer.ToString()));

        Assert.Equal(original.VariableCount, reparsed.VariableCount);
        Assert.Equal(original.Clauses.Count, reparsed.Clauses.Count);
        for (var i = 0; i < original.Clauses.Count; i++)
            Assert.Equal(original.Clauses[i], reparsed.Clauses[i]);
    }

    [Fact]
    public void ToFormula_ModelCountViaDnnf_MatchesBruteForce()
    {
        // count input.cnf --engine dnnf path: DNNF exact #SAT × 2^(free variables).
        var problem = DimacsParser.Parse(new StringReader("p cnf 4 3\n1 2 0\n-2 3 0\n-1 -3 0\n"));
        var circuit = KnowledgeCompilation.CompileToDnnf(problem.ToFormula());

        var free = problem.VariableCount - problem.ToFormula().GetVariables().Count;
        var count = circuit.CountModels() * BigInteger.Pow(2, free);

        var clauses = problem.Clauses.Select(c => c.ToArray()).ToList();
        var bruteForce = 0;
        for (var mask = 0; mask < 1 << problem.VariableCount; mask++)
            if (clauses.All(clause => clause.Any(l => ((mask >> (Math.Abs(l) - 1)) & 1) == 1 == l > 0)))
                bruteForce++;

        Assert.Equal(new BigInteger(bruteForce), count);
    }

    [Fact]
    public void Parse_ClauseReferencingUndeclaredVariable_SizesToMaxVariable()
    {
        // A literal beyond the declared count must not be silently dropped by the solver.
        var problem = DimacsParser.Parse(new StringReader("p cnf 1 1\n1 5 0\n"));
        Assert.Equal(5, problem.VariableCount);
    }

    [Fact]
    public void Parse_NonIntegerLiteral_ThrowsWithPosition()
    {
        var ex = Assert.Throws<FormatParseException>(() =>
            DimacsParser.Parse(new StringReader("p cnf 2 1\n1 x 0\n")));
        Assert.Equal(2, ex.Line);
        Assert.Equal(3, ex.Column);
    }

    [Fact]
    public void Parse_OversizedDeclaredVariableCount_ThrowsBudget()
    {
        Assert.Throws<ComputationBudgetExceededException>(() =>
            DimacsParser.Parse(new StringReader("p cnf 999999999 1\n1 0\n")));
    }
}
