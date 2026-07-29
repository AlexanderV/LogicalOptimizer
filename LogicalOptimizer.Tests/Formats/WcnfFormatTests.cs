using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     WCNF (weighted partial MaxSAT) parser, both dialects, round-trip writer and engine
///     hand-off. The optimum is cross-checked against an independent brute-force oracle.
/// </summary>
public class WcnfFormatTests
{
    // Classic weighted partial MaxSAT: exactly one of {a,b} (two hard clauses), soft a (3),
    // soft b (1). Optimum picks a=1,b=0 paying the cheaper soft (cost 1).
    private const string ClassicWcnf =
        "c classic weighted partial MaxSAT\n" +
        "p wcnf 2 4 10\n" +
        "10 1 2 0\n" +
        "10 -1 -2 0\n" +
        "3 1 0\n" +
        "1 2 0\n";

    // New-style dialect (MaxSAT Evaluation 2022+): same instance, 'h' for hard clauses, no
    // p-line and no trailing 0.
    private const string NewStyleWcnf =
        "c new-style WCNF\n" +
        "h 1 2\n" +
        "h -1 -2\n" +
        "3 1\n" +
        "1 2\n";

    [Fact]
    public void ParseClassic_SplitsHardAndSoftByTop()
    {
        var problem = WcnfParser.Parse(new StringReader(ClassicWcnf));

        Assert.Equal(2, problem.VariableCount);
        Assert.Equal(10, problem.Top);
        Assert.Equal(2, problem.HardClauses.Count);
        Assert.Equal(2, problem.SoftClauses.Count);
        Assert.Equal(3, problem.SoftClauses[0].Weight);
    }

    [Fact]
    public void ParseNewStyle_MarksHardWithH_SynthesizesTop()
    {
        var problem = WcnfParser.Parse(new StringReader(NewStyleWcnf));

        Assert.Equal(2, problem.VariableCount);
        Assert.Equal(2, problem.HardClauses.Count);
        Assert.Equal(2, problem.SoftClauses.Count);
        Assert.Equal(5, problem.Top); // (3 + 1) + 1
    }

    [Theory]
    [InlineData(ClassicWcnf)]
    [InlineData(NewStyleWcnf)]
    public void Solve_MatchesBruteForceOptimum(string text)
    {
        var problem = WcnfParser.Parse(new StringReader(text));
        var (expectedStatus, expectedCost) = BruteForceOptimum(problem);

        var result = problem.Solve();

        Assert.Equal(expectedStatus, result.Status);
        if (expectedStatus == MaxSatStatus.Optimal)
            Assert.Equal(expectedCost, result.Cost);
    }

    [Fact]
    public void RoundTrip_ParseWriteParse_PreservesOptimum()
    {
        var original = WcnfParser.Parse(new StringReader(ClassicWcnf));

        var writer = new StringWriter();
        original.Write(writer);
        var reparsed = WcnfParser.Parse(new StringReader(writer.ToString()));

        Assert.Equal(original.VariableCount, reparsed.VariableCount);
        Assert.Equal(original.Top, reparsed.Top);
        Assert.Equal(original.HardClauses.Count, reparsed.HardClauses.Count);
        Assert.Equal(original.SoftClauses.Count, reparsed.SoftClauses.Count);
        Assert.Equal(original.Solve().Cost, reparsed.Solve().Cost);
    }

    [Fact]
    public void Parse_UnsatisfiableHardClauses_ReportedAsSuch()
    {
        var problem = WcnfParser.Parse(new StringReader("p wcnf 1 3 5\n5 1 0\n5 -1 0\n1 1 0\n"));
        Assert.Equal(MaxSatStatus.HardClausesUnsatisfiable, problem.Solve().Status);
    }

    [Fact]
    public void Parse_NegativeWeight_ThrowsWithPosition()
    {
        var ex = Assert.Throws<FormatParseException>(() =>
            WcnfParser.Parse(new StringReader("p wcnf 1 1 5\n-2 1 0\n")));
        Assert.Equal(2, ex.Line);
    }

    private static (MaxSatStatus Status, long Cost) BruteForceOptimum(WeightedCnfProblem problem)
    {
        var n = problem.VariableCount;
        long? best = null;
        for (var mask = 0; mask < 1 << n; mask++)
        {
            var m = mask;
            bool Holds(int[] clause) => clause.Any(l => ((m >> (Math.Abs(l) - 1)) & 1) == 1 == l > 0);
            if (!problem.HardClauses.All(Holds)) continue;
            var cost = problem.SoftClauses.Where(s => !Holds(s.Literals)).Sum(s => s.Weight);
            if (best == null || cost < best) best = cost;
        }

        return best == null ? (MaxSatStatus.HardClausesUnsatisfiable, 0) : (MaxSatStatus.Optimal, best.Value);
    }
}
