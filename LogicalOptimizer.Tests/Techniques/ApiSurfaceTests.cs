using PublicApiGenerator;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     API-compatibility (SemVer) testing: the complete member-level public API surface of the
///     LogicalOptimizer assembly is pinned in TestData/PublicApi.approved.txt. ANY change to
///     the public API must show up as a reviewed diff of that file, never as silent drift.
///     Patch/minor releases must be additive only; a breaking change (removed or altered
///     public member) requires a MAJOR version bump. To regenerate after an intended change,
///     run with environment variable LOGICALOPTIMIZER_REGENERATE_API=1 and commit the diff.
/// </summary>
public class ApiSurfaceTests
{
    private const string RegenerateVariable = "LOGICALOPTIMIZER_REGENERATE_API";

    private static string ApprovedPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "PublicApi.approved.txt");

    private static string GeneratePublicApi()
    {
        // The toolkit ships as seven assemblies (facade + Core/Sat/Bdd/Dnnf/Minimization/Formats);
        // the SemVer contract covers the union of their public surfaces
        var assemblies = new[]
        {
            typeof(AstNode).Assembly, // LogicalOptimizer.Core
            typeof(SatSolver).Assembly, // LogicalOptimizer.Sat
            typeof(BinaryDecisionDiagram).Assembly, // LogicalOptimizer.Bdd
            typeof(DnnfCircuit).Assembly, // LogicalOptimizer.Dnnf
            typeof(TruthTableMinimizer).Assembly, // LogicalOptimizer.Minimization
            typeof(DimacsParser).Assembly, // LogicalOptimizer.Formats
            typeof(BooleanExpressionOptimizer).Assembly // facade
        };
        var options = new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false
        };
        var parts = assemblies
            .DistinctBy(a => a.FullName)
            .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
            .Select(a => $"// ===== {a.GetName().Name} =====\n" +
                         a.GeneratePublicApi(options).ReplaceLineEndings("\n").TrimEnd('\n'));
        return string.Join("\n\n", parts) + "\n";
    }

    [Fact]
    public void PublicApi_MatchesApprovedBaseline()
    {
        var actual = GeneratePublicApi();

        var sourceBaselinePath = Path.Combine(FindProjectRoot(), "TestData", "PublicApi.approved.txt");

        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            // Write into the SOURCE tree so the diff lands in review
            Directory.CreateDirectory(Path.GetDirectoryName(sourceBaselinePath)!);
            File.WriteAllText(sourceBaselinePath, actual);
            return;
        }

        if (!File.Exists(ApprovedPath) && !File.Exists(sourceBaselinePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceBaselinePath)!);
            File.WriteAllText(sourceBaselinePath, actual);
            Assert.Fail($"Public API baseline was missing; it has been created at {sourceBaselinePath}. " +
                        "Review and commit it, then re-run the tests.");
        }

        // Prefer the copied baseline; fall back to the source tree (e.g. freshly regenerated)
        var expectedPath = File.Exists(ApprovedPath) ? ApprovedPath : sourceBaselinePath;
        var expected = File.ReadAllText(expectedPath).ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

        if (expected != actual)
        {
            var receivedPath = Path.Combine(Path.GetDirectoryName(sourceBaselinePath)!, "PublicApi.received.txt");
            File.WriteAllText(receivedPath, actual);
            Assert.Fail("The public API surface has changed. Any public API change must be intentional: " +
                        "patch/minor releases must be additive only, and a breaking change (removed or altered " +
                        "public member) requires a MAJOR version bump per SemVer. " +
                        $"Actual API written to {receivedPath}; diff it against {expectedPath}. " +
                        $"If the change is intended, regenerate with {RegenerateVariable}=1 and commit the diff.");
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
