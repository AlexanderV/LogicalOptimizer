using System.Globalization;
using LogicalOptimizer;

namespace LogicalOptimizer.Benchmarks;

/// <summary>
///     <c>-- generate-corpora</c>: (re)generate the two vendored regression corpus families
///     into the SOURCE tree — <c>PlaCorpus/*.pla</c> (<see cref="PlaCorpusGenerator" />) and
///     <c>BddOrderCorpus/*.expr</c> (<see cref="BddOrderCorpusGenerator" />) — and print the
///     measured reference tables (per-output literal counts and statuses; BDD node counts
///     under a large budget) that the regression tests pin. Both generators are fully
///     deterministic, so a re-run is byte-identical; the regression tests verify the
///     committed files against the generator output on every gate run.
/// </summary>
public static class CorpusGenerationMode
{
    public static int Run(string[] args)
    {
        _ = args;
        var root = RepositoryRoot();

        var plaDirectory = Path.Combine(root, "LogicalOptimizer.Benchmarks", "PlaCorpus");
        Directory.CreateDirectory(plaDirectory);
        foreach (var instance in PlaCorpusGenerator.Generate())
            File.WriteAllText(Path.Combine(plaDirectory, instance.FileName), instance.Content);

        var bddDirectory = Path.Combine(root, "LogicalOptimizer.Benchmarks", "BddOrderCorpus");
        Directory.CreateDirectory(bddDirectory);
        foreach (var instance in BddOrderCorpusGenerator.Generate())
            File.WriteAllText(Path.Combine(bddDirectory, instance.FileName), instance.Content);

        Console.WriteLine($"PlaCorpus written to      {plaDirectory}");
        Console.WriteLine($"BddOrderCorpus written to {bddDirectory}");
        Console.WriteLine();

        ReportPla();
        ReportBddOrder();
        return 0;
    }

    /// <summary>Per-output minimization reference table (what PlaCorpusRegressionTests pins).</summary>
    private static void ReportPla()
    {
        Console.WriteLine("PLA corpus reference (per output: cube-expansion literals -> optimized literals):");
        Console.WriteLine($"{"file",-16} {"output",-6} {"cubeLits",8} {"optLits",8}  {"status",-14} equiv");

        var optimizer = new BooleanExpressionOptimizer();
        var factory = new FormulaFactory();
        foreach (var instance in PlaCorpusGenerator.Generate())
        {
            var pla = PlaFile.Parse(instance.Content);
            var total = 0;
            var totalCubes = 0;
            for (var o = 0; o < pla.OutputNames.Count; o++)
            {
                var result = optimizer.OptimizeExpression(pla.OutputExpression(o));
                var optimizedLiterals = AstMetrics.CountLiterals(factory.Parse(result.Optimized));
                total += optimizedLiterals;
                totalCubes += pla.OutputCubeLiteralCount(o);
                Console.WriteLine(
                    $"{instance.FileName,-16} {pla.OutputNames[o],-6} {pla.OutputCubeLiteralCount(o),8} " +
                    $"{optimizedLiterals,8}  {result.MinimizationStatus,-14} {result.IsEquivalent()}");
            }

            Console.WriteLine($"{instance.FileName,-16} {"TOTAL",-6} {totalCubes,8} {total,8}");
        }

        Console.WriteLine();
    }

    /// <summary>BDD node-count reference table (what BddOrderCorpusRegressionTests pins).</summary>
    private static void ReportBddOrder()
    {
        const int largeBudget = 1_000_000;
        Console.WriteLine("BDD order corpus reference (allocated NodeCount after Build under a large budget):");
        Console.WriteLine($"{"file",-24} {"vars",4} {"build",8} {"bestOrder",9} {"sifted",8} models");

        var factory = new FormulaFactory();
        foreach (var instance in BddOrderCorpusGenerator.Generate())
        {
            var ast = factory.Parse(instance.Expression);
            var build = BinaryDecisionDiagram.Build(ast, largeBudget);
            var best = BinaryDecisionDiagram.BuildWithBestOrder(ast, largeBudget);
            var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(ast, largeBudget);
            var models = build.CountSatisfyingAssignments().ToString(CultureInfo.InvariantCulture);
            Console.WriteLine(
                $"{instance.FileName,-24} {2 * instance.Bits,4} {build.NodeCount,8} " +
                $"{best.NodeCount,9} {sifted.NodeCount,8} {models}");
        }

        Console.WriteLine();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root (LogicalOptimizer.sln)");
    }
}
