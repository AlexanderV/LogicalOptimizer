using System.Numerics;
using System.Xml.Linq;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Guards the v4.0 package-consolidation contract (doc/decisions/package-consolidation-v4.md):
///     the pre-4.0 package IDs survive only as deprecated forwarding shells whose single
///     dependency is the consolidated LogicalOptimizer package, and the consolidated package
///     really does cover optimizer + SAT + BDD + d-DNNF + formats.
/// </summary>
public class MetaPackageTests
{
    /// <summary>
    ///     Every forwarding shell must declare exactly one dependency — the consolidated
    ///     package, pinned to the EXACT version ("[$version$]" in NuGet range syntax; a bare
    ///     "$version$" would mean "&gt;=") — and say DEPRECATED. A second dependency, a wrong
    ///     id, a floating range, or a silent un-deprecation would change what the
    ///     transition-period IDs install.
    /// </summary>
    [Fact]
    public void ForwardingShells_ForwardExactlyToTheConsolidatedPackage()
    {
        var root = RepositoryRoot();
        var shells = new Dictionary<string, string>
        {
            ["LogicalOptimizer.Core"] = Path.Combine(root, "forwarding", "LogicalOptimizer.Core.Forwarding"),
            ["LogicalOptimizer.Sat"] = Path.Combine(root, "forwarding", "LogicalOptimizer.Sat.Forwarding"),
            ["LogicalOptimizer.Bdd"] = Path.Combine(root, "forwarding", "LogicalOptimizer.Bdd.Forwarding"),
            ["LogicalOptimizer.Dnnf"] = Path.Combine(root, "forwarding", "LogicalOptimizer.Dnnf.Forwarding"),
            ["LogicalOptimizer.Formats"] = Path.Combine(root, "forwarding", "LogicalOptimizer.Formats.Forwarding"),
            ["LogicalOptimizer.Minimization"] = Path.Combine(root, "forwarding", "LogicalOptimizer.Minimization.Forwarding"),
            ["LogicalOptimizer.Full"] = Path.Combine(root, "LogicalOptimizer.Full")
        };

        foreach (var (id, directory) in shells)
        {
            var nuspecPath = Path.Combine(directory, $"{id}.nuspec");
            Assert.True(File.Exists(nuspecPath), $"Forwarding nuspec not found at {nuspecPath}");

            var nuspec = XDocument.Load(nuspecPath);
            XNamespace ns = nuspec.Root!.GetDefaultNamespace();

            Assert.Equal(id, nuspec.Descendants(ns + "id").Single().Value);

            var dependencies = nuspec.Descendants(ns + "dependency").ToList();
            var single = Assert.Single(dependencies);
            Assert.Equal("LogicalOptimizer", single.Attribute("id")!.Value);
            Assert.Equal("[$version$]", single.Attribute("version")!.Value);

            Assert.Contains("DEPRECATED", nuspec.Descendants(ns + "description").Single().Value);
        }
    }

    /// <summary>
    ///     Smoke test: exercise every engine the consolidated package promises, through the
    ///     very assemblies it bundles. If any engine stopped being reachable from the
    ///     seven-assembly closure this would fail to compile or run.
    /// </summary>
    [Fact]
    public void ConsolidatedPackage_SurfaceExercisesEveryEngine()
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

        // Formats (Formats) - a DIMACS problem parses and solves through the same closure.
        var cnf = DimacsParser.Parse(new StringReader("p cnf 1 1\n1 0\n"));
        Assert.Equal(SatResult.Satisfiable, cnf.Solve());
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
