using System.IO;
using System.Reflection;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The suite's own layout, enforced rather than documented.
///     <para>
///         doc/TESTING.md Part 2 describes a subject-based hierarchy, and a reader uses it to answer
///         "where do the X tests live?" before writing a new one. That only works if a test class can
///         be found by its name. It stopped being true quietly: <c>EquivalenceCheckerTests</c> sat
///         inside <c>Engines/Sat/SatSolverTests.cs</c>, so it appeared in no folder tally, in no
///         version of the Part 2 tree, and was noticed only by accident during a Performance run. A
///         test nobody can find by subject is a test somebody duplicates later.
///     </para>
///     <para>
///         One rule, mechanically checkable, and deliberately NOT a rule about which folder anything
///         belongs in — folder placement is a judgement call and encoding taste in a test would make
///         it brittle. This only requires that every test class is reachable by name.
///     </para>
/// </summary>
public class TestSuiteLayoutTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }

    private static IEnumerable<FileInfo> SourceFiles()
    {
        var project = new DirectoryInfo(Path.Combine(RepositoryRoot(), "LogicalOptimizer.Tests"));
        return project.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Types in this assembly declaring at least one xUnit test method.</summary>
    private static IEnumerable<Type> TestClasses() =>
        typeof(TestSuiteLayoutTests).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsNested)
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.GetCustomAttributes().Any(a =>
                    a.GetType().Name is "FactAttribute" or "TheoryAttribute")));

    [Fact]
    public void EveryTestClass_LivesInAFileNamedAfterIt()
    {
        // Also implies at most one test class per file: two would need the same file name. Helper
        // and fixture types (SnapshotSetup, ConsoleCollection, the TestInfrastructure oracles) are
        // exempt by construction — they declare no test methods.
        var files = SourceFiles()
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var homeless = TestClasses()
            .Select(t => t.Name)
            .Where(name => !files.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(homeless.Count == 0,
            "These test classes are not in a file named after them, so they cannot be found by " +
            "subject and are invisible to doc/TESTING.md's Part 2 hierarchy — give each its own " +
            $"file under the folder for its subject: {string.Join(", ", homeless)}");
    }

    [Fact]
    public void TheSuiteIsOrganizedIntoSubjectFolders_NotAFlatPile()
    {
        // Guards the premise of the Part 2 tree: if test files started landing in the project root
        // the hierarchy would be a fiction. Infrastructure files (Usings.cs and friends) may sit at
        // the root; test classes may not.
        var projectRoot = Path.Combine(RepositoryRoot(), "LogicalOptimizer.Tests");
        var names = TestClasses().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var atRoot = SourceFiles()
            .Where(f => string.Equals(f.DirectoryName, projectRoot, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .Where(names.Contains)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(atRoot.Count == 0,
            "Test classes must live in a subject folder (Core/, Optimizers/, Engines/…, Facade/, " +
            "Formats/, Cli/, Documentation/, Analysis/) or in Techniques/ for a cross-cutting " +
            $"suite, not in the project root: {string.Join(", ", atRoot)}");
    }
}
