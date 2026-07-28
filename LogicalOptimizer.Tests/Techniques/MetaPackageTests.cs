using System.Numerics;
using System.Xml.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Guards the LogicalOptimizer.Full meta-package contract (roadmap P3.1). The package
///     ships no code, so there is nothing to reflect over; the two things worth protecting
///     are its dependency closure and the promise that the aggregated surface really does
///     cover optimizer + SAT + BDD + d-DNNF.
/// </summary>
public class MetaPackageTests
{
    /// <summary>
    ///     The meta-package must depend on exactly the facade and Dnnf: the facade
    ///     transitively pulls in Core/Sat/Bdd/Minimization, and Dnnf is the only managed
    ///     package it does not already cover. A drift here (an extra or missing reference)
    ///     silently changes what `dotnet add package LogicalOptimizer.Full` installs.
    /// </summary>
    [Fact]
    public void FullMetaPackage_ReferencesExactlyFacadeAndDnnf()
    {
        var csprojPath = Path.Combine(RepositoryRoot(), "LogicalOptimizer.Full", "LogicalOptimizer.Full.csproj");
        Assert.True(File.Exists(csprojPath), $"Meta-package project not found at {csprojPath}");

        var project = XDocument.Load(csprojPath);

        var referencedProjects = project.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(
                r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "LogicalOptimizer", "LogicalOptimizer.Dnnf" }, referencedProjects);

        // Pure metadata package: no assembly must be packed.
        var includeBuildOutput = project.Descendants("IncludeBuildOutput").SingleOrDefault()?.Value;
        Assert.Equal("false", includeBuildOutput, ignoreCase: true);
    }

    /// <summary>
    ///     Smoke test: exercise all four engines the bundle promises, through the very
    ///     assemblies the meta-package aggregates. If any engine stopped being reachable
    ///     from the facade + Dnnf closure this would fail to compile or run.
    /// </summary>
    [Fact]
    public void FullMetaPackage_SurfaceExercisesAllFourEngines()
    {
        var formula = "a & b | a & c";
        var ast = new FormulaFactory().Parse(formula);

        // Optimizer (facade)
        var optimized = new BooleanExpressionOptimizer().OptimizeExpression(formula);
        Assert.False(string.IsNullOrWhiteSpace(optimized.Optimized));

        // SAT (Sat)
        var solver = new SatSolver(1);
        solver.AddClause(1);
        Assert.Equal(SatResult.Satisfiable, solver.Solve());

        // BDD (Bdd)
        var bddCount = BinaryDecisionDiagram.BuildWithBestOrder(ast).CountSatisfyingAssignments();

        // d-DNNF (Dnnf) - independent model count that must agree with the BDD.
        BigInteger dnnfCount = KnowledgeCompilation.CompileToDnnf(ast).CountModels();

        Assert.Equal(bddCount, dnnfCount);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root (LogicalOptimizer.sln)");
    }
}
