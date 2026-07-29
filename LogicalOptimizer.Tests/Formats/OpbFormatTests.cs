using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     OPB (pseudo-Boolean) parser, round-trip writer and CNF-encoded engine hand-off.
///     Feasibility is cross-checked against an independent brute-force oracle that evaluates
///     the linear constraints directly.
/// </summary>
public class OpbFormatTests
{
    // Tiny corpus sample exercising an objective, all three operators and a ~x negation.
    private const string SampleOpb =
        "* #variable= 3 #constraint= 3\n" +
        "min: +2 x1 +1 x2 ;\n" +
        "+1 x1 +1 x2 +1 x3 >= 2 ;\n" +
        "+1 ~x1 +1 x2 <= 1 ;\n" +
        "+1 x2 = 1 ;\n";

    [Fact]
    public void Parse_ReadsObjectiveAndConstraints()
    {
        var problem = OpbParser.Parse(new StringReader(SampleOpb));

        Assert.Equal(3, problem.VariableCount);
        Assert.Equal(3, problem.Constraints.Count);
        Assert.NotNull(problem.Objective);
        Assert.Equal(new[] { (2L, 1), (1L, 2) }, problem.Objective!);

        Assert.Equal(PseudoBooleanComparison.GreaterOrEqual, problem.Constraints[0].Comparison);
        Assert.Equal(2, problem.Constraints[0].Bound);
        Assert.Equal(PseudoBooleanComparison.LessOrEqual, problem.Constraints[1].Comparison);
        Assert.Equal(new[] { (1L, -1), (1L, 2) }, problem.Constraints[1].Terms); // ~x1 -> literal -1
        Assert.Equal(PseudoBooleanComparison.Equal, problem.Constraints[2].Comparison);
    }

    [Theory]
    [InlineData(SampleOpb)]
    [InlineData("* #variable= 2 #constraint= 1\n+1 x1 +1 x2 >= 3 ;\n")] // infeasible: max sum is 2
    [InlineData("* #variable= 2 #constraint= 2\n+1 x1 <= 0 ;\n-1 x2 >= 0 ;\n")] // negative coefficient
    [InlineData("* #variable= 3 #constraint= 1\n+2 x1 +3 ~x2 +1 x3 = 4 ;\n")]
    public void Solve_AgreesWithBruteForceFeasibility(string text)
    {
        var problem = OpbParser.Parse(new StringReader(text));
        var expected = BruteForceFeasible(problem);

        var verdict = problem.Solve();

        Assert.Equal(expected ? SatResult.Satisfiable : SatResult.Unsatisfiable, verdict);
    }

    [Fact]
    public void RoundTrip_ParseWriteParse_PreservesConstraints()
    {
        var original = OpbParser.Parse(new StringReader(SampleOpb));

        var writer = new StringWriter();
        original.Write(writer);
        var reparsed = OpbParser.Parse(new StringReader(writer.ToString()));

        Assert.Equal(original.VariableCount, reparsed.VariableCount);
        Assert.Equal(original.Objective, reparsed.Objective);
        Assert.Equal(original.Constraints.Count, reparsed.Constraints.Count);
        for (var i = 0; i < original.Constraints.Count; i++)
        {
            Assert.Equal(original.Constraints[i].Comparison, reparsed.Constraints[i].Comparison);
            Assert.Equal(original.Constraints[i].Bound, reparsed.Constraints[i].Bound);
            Assert.Equal(original.Constraints[i].Terms, reparsed.Constraints[i].Terms);
        }
    }

    [Fact]
    public void Parse_ConstraintSpanningLines_IsOneConstraint()
    {
        var problem = OpbParser.Parse(new StringReader("* header\n+1 x1\n+1 x2\n>= 1 ;\n"));
        Assert.Single(problem.Constraints);
        Assert.Equal(2, problem.Constraints[0].Terms.Count);
    }

    [Fact]
    public void Parse_MalformedVariable_ThrowsWithPosition()
    {
        var ex = Assert.Throws<FormatParseException>(() =>
            OpbParser.Parse(new StringReader("+1 y1 >= 1 ;\n")));
        Assert.Equal(1, ex.Line);
    }

    private static bool BruteForceFeasible(PseudoBooleanProblem problem)
    {
        var n = problem.VariableCount;
        for (var mask = 0; mask < 1 << n; mask++)
        {
            var satisfied = true;
            foreach (var constraint in problem.Constraints)
            {
                var sum = 0L;
                foreach (var (coefficient, literal) in constraint.Terms)
                {
                    var bit = (mask >> (Math.Abs(literal) - 1)) & 1;
                    var value = literal > 0 ? bit : 1 - bit;
                    sum += coefficient * value;
                }

                var holds = constraint.Comparison switch
                {
                    PseudoBooleanComparison.GreaterOrEqual => sum >= constraint.Bound,
                    PseudoBooleanComparison.LessOrEqual => sum <= constraint.Bound,
                    _ => sum == constraint.Bound
                };
                if (!holds)
                {
                    satisfied = false;
                    break;
                }
            }

            if (satisfied) return true;
        }

        return false;
    }
}
