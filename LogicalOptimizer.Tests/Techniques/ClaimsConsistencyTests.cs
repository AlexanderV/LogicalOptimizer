using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Keeps the project's public claims consistent with the vocabulary defined in
///     <c>doc/CLAIMS.md</c>, mechanically rather than by review discipline:
///     <list type="bullet">
///         <item>
///             no public-facing document uses a banned phrasing - an unqualified
///             "zero runtime/production dependencies" (LogicalOptimizer packages DO reference each
///             other) or an absolute competitive superlative, for which no external comparative
///             evidence exists;
///         </item>
///         <item>
///             every piece of evidence <c>doc/CLAIMS.md</c> points at still resolves - the file
///             exists, and where the link names a test method, that method is still in it. A claim
///             cannot outlive the test that backs it;
///         </item>
///         <item>
///             the glossary still covers every term the positioning plan requires a definition for.
///         </item>
///     </list>
///     A line may opt out with a trailing <c>&lt;!-- claim-ok: reason --&gt;</c> for a legitimate
///     use (quoting, or stating a limitation such as "not the fastest solver"). The escape is
///     deliberately visible so it surfaces in review.
/// </summary>
public class ClaimsConsistencyTests
{
    /// <summary>
    ///     Unqualified dependency phrasings. Banned because a shipped LogicalOptimizer package does
    ///     reference other LogicalOptimizer packages; only "third-party" makes the claim true.
    /// </summary>
    private static readonly string[] BannedDependencyPhrases =
    {
        "zero production dependencies",
        "zero runtime dependencies",
        "zero dependencies"
    };

    /// <summary>
    ///     Absolute competitive claims. POSITIONING_IMPROVEMENT_PLAN.md sections 2 and 7 rule these
    ///     out until there is external comparative evidence; the pinned comparison measures scope
    ///     and size, which supports concrete differentiators but not a superlative.
    /// </summary>
    private static readonly string[] BannedSuperlatives =
    {
        "most complete",
        "best-in-niche",
        "fastest",
        "safest",
        "most convenient",
        "unmatched",
        "industry-leading",
        "world-class"
    };

    /// <summary>The terms POSITIONING_IMPROVEMENT_PLAN.md V2 requires a shared definition for.</summary>
    private static readonly string[] RequiredGlossaryTerms =
    {
        "verified",
        "minimal",
        "dependency-free",
        "Native AOT support",
        "benchmark result"
    };

    private const string EscapeMarker = "<!-- claim-ok:";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }

    /// <summary>
    ///     The public surface: what a prospective user actually reads. Historical analysis documents
    ///     (comparisons, roadmaps, plans, the changelog) are deliberately excluded - they record what
    ///     was written at a point in time, and rewriting them would falsify the record. doc/CLAIMS.md
    ///     is excluded because it is the file that *defines* the banned phrasings.
    /// </summary>
    private static IEnumerable<string> PublicDocuments()
    {
        var root = RepositoryRoot();

        foreach (var name in new[] { "README.md", "SUPPORT.md", "SECURITY.md" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) yield return path;
        }

        var schemaReadme = Path.Combine(root, "schema", "README.md");
        if (File.Exists(schemaReadme)) yield return schemaReadme;

        // Per-package READMEs - these become the nuget.org package pages.
        foreach (var directory in Directory.GetDirectories(root, "LogicalOptimizer*"))
        {
            var path = Path.Combine(directory, "README.md");
            if (File.Exists(path)) yield return path;
        }

        var site = Path.Combine(root, "docs-site");
        var landing = Path.Combine(site, "index.md");
        if (File.Exists(landing)) yield return landing;

        var articles = Path.Combine(site, "articles");
        if (Directory.Exists(articles))
            foreach (var path in Directory.GetFiles(articles, "*.md", SearchOption.AllDirectories))
                yield return path;
    }

    /// <summary>Packable projects, whose &lt;Description&gt; is the nuget.org package summary.</summary>
    private static IEnumerable<string> PackageDescriptionProjects()
    {
        foreach (var directory in Directory.GetDirectories(RepositoryRoot(), "LogicalOptimizer*"))
            foreach (var project in Directory.GetFiles(directory, "*.csproj"))
                if (File.ReadAllText(project).Contains("<Description>"))
                    yield return project;
    }

    private static IEnumerable<(string Path, int Line, string Text)> ScannableLines(string file)
    {
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(EscapeMarker, StringComparison.Ordinal)) continue;
            yield return (file, i + 1, lines[i]);
        }
    }

    private static void AssertNoPhrase(string[] banned, string reason, string replacement)
    {
        var root = RepositoryRoot();
        var violations = new List<string>();

        foreach (var file in PublicDocuments())
            foreach (var (path, line, text) in ScannableLines(file))
                foreach (var phrase in banned)
                    if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        violations.Add($"  {Path.GetRelativePath(root, path)}:{line}  \"{phrase}\"");

        foreach (var project in PackageDescriptionProjects())
            foreach (var (path, line, text) in ScannableLines(project))
                foreach (var phrase in banned)
                    if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        violations.Add($"  {Path.GetRelativePath(root, path)}:{line}  \"{phrase}\"");

        Assert.True(violations.Count == 0,
            $"{reason}\n\nUse instead: {replacement}\n" +
            "See doc/CLAIMS.md. If the phrase is genuinely correct here (a quotation, or stating a\n" +
            "limitation), append '<!-- claim-ok: reason -->' to that line.\n\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void PublicSurface_DoesNotClaimDependencyFreedomWithoutQualifying()
    {
        AssertNoPhrase(BannedDependencyPhrases,
            "A shipped LogicalOptimizer package references other LogicalOptimizer packages, so an " +
            "unqualified dependency claim is not true.",
            "\"zero third-party runtime dependencies\" / \"no third-party runtime dependency\"");
    }

    [Fact]
    public void PublicSurface_MakesNoAbsoluteCompetitiveClaim()
    {
        AssertNoPhrase(BannedSuperlatives,
            "An absolute competitive claim needs external comparative evidence, and none exists yet " +
            "(no independent benchmark reproduction - see V3 in POSITIONING_IMPROVEMENT_PLAN.md).",
            "a concrete, checkable differentiator plus a link to doc/comparison/");
    }

    [Fact]
    public void Claims_GlossaryExists_AndCoversEveryRequiredTerm()
    {
        var path = Path.Combine(RepositoryRoot(), "doc", "CLAIMS.md");
        Assert.True(File.Exists(path), $"The claims glossary is missing at {path}.");

        var text = File.ReadAllText(path);
        foreach (var term in RequiredGlossaryTerms)
            Assert.True(text.Contains(term, StringComparison.OrdinalIgnoreCase),
                $"doc/CLAIMS.md does not define '{term}', which POSITIONING_IMPROVEMENT_PLAN.md V2 requires.");
    }

    [Fact]
    public void Claims_EveryEvidenceReference_StillResolves()
    {
        // The whole point of the glossary is that each claim names something you can open. A renamed
        // test or a moved workflow must break here, not silently leave a claim unbacked.
        var root = RepositoryRoot();
        var glossary = Path.Combine(root, "doc", "CLAIMS.md");
        var text = File.ReadAllText(glossary);

        var links = Regex.Matches(text, @"\[(?<label>[^\]]+)\]\((?<target>[^)]+)\)");
        Assert.True(links.Count > 20, $"Expected the glossary to cite evidence; found {links.Count} links.");

        var broken = new List<string>();
        var checkedMethods = 0;

        foreach (Match link in links)
        {
            var target = link.Groups["target"].Value;
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

            var withoutAnchor = target.Split('#')[0];
            if (withoutAnchor.Length == 0) continue;

            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(glossary)!, withoutAnchor));
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
            {
                broken.Add($"  missing target: {target}");
                continue;
            }

            // A label like "TruthTableMinimizerTests.OptimizeExpression_AllFourVariableFunctions_MinimalProven"
            // promises that method exists in the linked file - check it, not just the file.
            var label = link.Groups["label"].Value.Trim('`');
            var member = Regex.Match(label, @"^(?<type>\w+)\.(?<method>\w+)$");
            if (!member.Success || !resolved.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(resolved);
            var method = member.Groups["method"].Value;
            if (!source.Contains(method, StringComparison.Ordinal))
                broken.Add($"  {Path.GetRelativePath(root, resolved)} no longer contains '{method}'");
            else
                checkedMethods++;
        }

        Assert.True(broken.Count == 0,
            "doc/CLAIMS.md cites evidence that no longer resolves. Either restore it or change the " +
            "claim - a claim without backing must not stay published.\n\n" + string.Join("\n", broken));

        Assert.True(checkedMethods >= 8,
            $"Only {checkedMethods} named test method(s) were verified; the glossary should cite " +
            "specific tests, not just files.");
    }

    [Fact]
    public void Claims_GlossaryIsReachableFromTheReadme()
    {
        // A glossary nobody is pointed at does not make claims consistent.
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));
        Assert.Contains("doc/CLAIMS.md", readme, StringComparison.Ordinal);
    }
}
