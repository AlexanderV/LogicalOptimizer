using System.Text;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Characterization (golden-master) testing: the complete observable output of the
///     optimizer over a fixed corpus is pinned in TestData/characterization.golden.txt.
///     ANY behavior change — even an improvement — must show up as a reviewed diff of
///     that file, never as a silent drift. To regenerate after an intended change, run
///     with environment variable LOGICALOPTIMIZER_REGENERATE_GOLDEN=1 and commit the diff.
/// </summary>
public class CharacterizationTests
{
    private static readonly string[] Corpus =
    {
        "a",
        "!a",
        "!!a",
        "0",
        "1",
        "a & b",
        "a | b",
        "a & !a",
        "a | !a",
        "a & 1",
        "a | 0",
        "a & b | a & c",
        "a | a & b",
        "!(a & b)",
        "!(a | b)",
        "a & !b | !a & b",
        "a & b | !a & c",
        "a & b | !a & c | b & c",
        "(a | b) & (!a | c)",
        "s & a | !s & b",
        "a & b | a & c | b & c",
        "a & !b & !c | !a & b & !c | !a & !b & c | a & b & c",
        "(a | b) & (c | d) & (a | d)",
        "a & (b | c) & !d | e",
        "((a | b) & c) | (d & (e | f))",
        "!c & !d | c & !d | !c & d",
        "x & y | !x & z | y & z",
        "(a | b) & (a | !b) & (!a | b)",
        "a & b & c | a & b & !c | a & !b & c | !a & b & c",
        "v01 & v02 | !v01 & v03 | v02 & v03 | v04 & v05 | v06 & v07 | v08 & v09 | v10 & v11 | v12 & v01",
        // Heuristic zones (pins the SAT-cover path at 14 vars and the espresso DNF
        // path at 26 vars — both engines are deterministic)
        "x1 & y1 | x2 & y2 | x3 & y3 | x4 & y4 | x5 & y5 | x6 & y6 | x7 & y7 | x1 & y1 & x2",
        "a01 & b01 | a01 & b01 & a02 | !a01 & a02 | b01 & a02 | a03 & b03 | a03 & b03 & a04 | " +
        "!a03 & a04 | b03 & a04 | a05 & b05 | a06 & b06 | a07 & b07 | a08 & b08 | a09 & b09 | " +
        "a10 & b10 | a11 & b11 | a12 & b12 | a13 & b13"
    };

    private static string GoldenPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "characterization.golden.txt");

    private static string Describe(string expression)
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
            new OptimizationOptions());
        return $"expr: {expression}\n" +
               $"  optimized: {result.Optimized}\n" +
               $"  cnf: {result.CNF} [{result.CnfStatus}]\n" +
               $"  dnf: {result.DNF} [{result.DnfStatus}]\n" +
               $"  advanced: {result.Advanced}\n" +
               $"  minimality: {result.MinimizationStatus}\n" +
               $"  variables: [{string.Join(", ", result.Variables)}]";
    }

    private static string RenderCorpus()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Golden master of the optimizer's observable behavior.");
        builder.AppendLine("# Regenerate with LOGICALOPTIMIZER_REGENERATE_GOLDEN=1 and review the diff.");
        foreach (var expression in Corpus)
        {
            builder.AppendLine(Describe(expression));
            builder.AppendLine();
        }

        return builder.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void Optimizer_MatchesGoldenMaster()
    {
        var actual = RenderCorpus();

        if (Environment.GetEnvironmentVariable("LOGICALOPTIMIZER_REGENERATE_GOLDEN") == "1")
        {
            // Write into the SOURCE tree so the diff lands in review
            var sourcePath = Path.Combine(FindProjectRoot(), "TestData", "characterization.golden.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual);
            return;
        }

        Assert.True(File.Exists(GoldenPath),
            $"Golden file missing at {GoldenPath}. Run once with LOGICALOPTIMIZER_REGENERATE_GOLDEN=1.");

        var expected = File.ReadAllText(GoldenPath).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            var received = Path.ChangeExtension(GoldenPath, ".received.txt");
            File.WriteAllText(received, actual);
            var firstDiff = Enumerable.Range(0, Math.Min(expected.Length, actual.Length))
                .Cast<int?>().FirstOrDefault(i => expected[i!.Value] != actual[i.Value]) ?? Math.Min(expected.Length, actual.Length);
            Assert.Fail("Optimizer behavior drifted from the golden master. " +
                        $"First difference at offset {firstDiff}; full actual written to {received}. " +
                        "If the change is intended, regenerate with LOGICALOPTIMIZER_REGENERATE_GOLDEN=1 and review the diff.");
        }
    }

    [Fact]
    public void GoldenCorpus_EveryPinnedResultIsStillEquivalent()
    {
        // The golden master pins BEHAVIOR; this guards it against pinning a WRONG result:
        // every corpus entry's optimized form must stay semantically equivalent
        foreach (var expression in Corpus)
        {
            var result = new BooleanExpressionOptimizer().OptimizeExpression(expression,
                new OptimizationOptions { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false });
            // Heuristic-zone corpus entries exceed the truth-table range — the SAT
            // miter carries the equivalence proof there
            var ast = RandomExpressions.Parse(expression);
            var equivalent = ast.GetVariables().Count <= 12
                ? TruthTable.AreEquivalent(expression, result.Optimized)
                : EquivalenceChecker.Check(expression, result.Optimized).AreEquivalent == true;
            Assert.True(equivalent,
                $"Corpus entry '{expression}' produced a non-equivalent result");
        }
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.Tests.csproj")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the test project root");
    }
}
