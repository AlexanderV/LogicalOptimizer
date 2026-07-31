using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Contract tests for <see cref="ExternalSatProblem" />: literal validation, the
///     DIMACS rendering adapters hand to a solver process, and the model verification
///     that backs the seam's trust asymmetry (SAT claims are checked, UNSAT trusted).
/// </summary>
public class ExternalSatProblemTests
{
    private static ExternalSatProblem Sample()
    {
        // (a | b) & (!a | c)
        return new ExternalSatProblem(3, new[] { new[] { 1, 2 }, new[] { -1, 3 } });
    }

    [Fact]
    public void Constructor_RejectsOutOfRangeAndZeroLiterals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExternalSatProblem(-1, Array.Empty<int[]>()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExternalSatProblem(2, new[] { new[] { 1, 3 } }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExternalSatProblem(2, new[] { new[] { 1, 0 } }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExternalSatProblem(2, new[] { new[] { 1 } }, new[] { -5 }));
    }

    [Fact]
    public void ToDimacs_EmitsHeaderAndZeroTerminatedClauses()
    {
        var dimacs = Sample().ToDimacs();
        var lines = dimacs.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal(new[] { "p cnf 3 2", "1 2 0", "-1 3 0" }, lines);
    }

    [Fact]
    public void ToDimacs_AppendsAssumptionsAsUnitClauses()
    {
        var problem = new ExternalSatProblem(3, new[] { new[] { 1, 2 } }, new[] { -3 });
        var dimacs = problem.ToDimacs();

        Assert.Contains("p cnf 3 2", dimacs); // clause count includes the assumption unit
        Assert.Contains("-3 0", dimacs);
    }

    [Fact]
    public void FromCnf_TakesVariableCountAndClausesFromTheTseitinCnf()
    {
        var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf("(a | b) & (b | c)");
        var problem = ExternalSatProblem.FromCnf(cnf);

        Assert.Equal(cnf.TotalVariableCount, problem.VariableCount);
        Assert.Equal(cnf.Clauses.Count, problem.Clauses.Count);
        Assert.Empty(problem.Assumptions);
    }

    [Fact]
    public void IsSatisfiedBy_AcceptsASatisfyingModel()
    {
        Assert.True(Sample().IsSatisfiedBy(new[] { 1, -2, 3 }));
    }

    [Fact]
    public void IsSatisfiedBy_AcceptsAPartialModelThatSatisfiesEveryClause()
    {
        // b and c alone satisfy both clauses; a stays unassigned.
        Assert.True(Sample().IsSatisfiedBy(new[] { 2, 3 }));
    }

    [Fact]
    public void IsSatisfiedBy_RejectsAModelThatFalsifiesAClause()
    {
        Assert.False(Sample().IsSatisfiedBy(new[] { 1, -2, -3 })); // (!a | c) falsified
        Assert.False(Sample().IsSatisfiedBy(new[] { 1 })); // second clause not witnessed
    }

    [Fact]
    public void IsSatisfiedBy_RejectsContradictoryAndOutOfRangeModels()
    {
        Assert.False(Sample().IsSatisfiedBy(new[] { 1, -1, 3 }));
        Assert.False(Sample().IsSatisfiedBy(new[] { 1, 3, 4 }));
        Assert.False(Sample().IsSatisfiedBy(new[] { 1, 3, 0 }));
    }

    [Fact]
    public void IsSatisfiedBy_RequiresAssumptionsToBeAsserted()
    {
        var problem = new ExternalSatProblem(3, new[] { new[] { 1, 2 } }, new[] { 3 });

        Assert.True(problem.IsSatisfiedBy(new[] { 1, 3 }));
        Assert.False(problem.IsSatisfiedBy(new[] { 1, -3 })); // assumption violated
        Assert.False(problem.IsSatisfiedBy(new[] { 1 })); // assumption unassigned
    }

    [Fact]
    public void ExternalSatResult_FactoriesCarryTheVerdictAndModel()
    {
        var sat = ExternalSatResult.Satisfiable(new[] { 1, -2 });
        Assert.Equal(SatResult.Satisfiable, sat.Verdict);
        Assert.Equal(new[] { 1, -2 }, sat.Model);

        Assert.Equal(SatResult.Unsatisfiable, ExternalSatResult.Unsatisfiable().Verdict);
        Assert.Null(ExternalSatResult.Unsatisfiable().Model);
        Assert.Equal(SatResult.Unknown, ExternalSatResult.Unknown().Verdict);
        Assert.Null(ExternalSatResult.Unknown().Model);
    }
}
