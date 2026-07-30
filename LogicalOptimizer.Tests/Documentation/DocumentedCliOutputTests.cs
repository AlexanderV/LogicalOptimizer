using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Keeps every documented CLI transcript equal to what the CLI actually prints.
///     <para>
///         <c>docs-site/articles/cli-usage.md</c> states "All outputs shown are verified against the
///         built CLI", and the README shows the same transcript to a reader deciding whether to
///         install. Nothing enforced that: when the formatter started printing
///         <c>Equivalent</c>/<c>Minimality</c>/<c>Cost</c>, the CLI snapshot was updated and the
///         documentation silently fell behind. This test makes the promise executable - the
///         documented block is compared, line for line, against output produced by the same
///         formatter the CLI uses.
///     </para>
///     <para>
///         Blocks that legitimately elide the tail (the README trims the truth table) are matched as
///         a PREFIX, so shortening is allowed but misreporting is not.
///     </para>
/// </summary>
[Collection(ConsoleCollection.Name)]
public class DocumentedCliOutputTests
{
    /// <summary>The expression every documented default-output transcript uses.</summary>
    private const string DocumentedExpression = "a & b | a & c";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the repository root");
    }

    /// <summary>Renders the default CLI output through the very formatter the tool runs.</summary>
    private static string RenderDefaultOutput(string expression)
    {
        var result = new BooleanExpressionOptimizer().OptimizeExpression(expression);

        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            new OutputFormatter().DisplayResult(result,
                new CommandLineProcessor.CommandLineOptions { Expression = expression });
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static string[] NonEmptyLines(string text) => text
        .ReplaceLineEndings("\n")
        .Split('\n')
        .Select(line => line.TrimEnd())
        .Where(line => line.Length > 0)
        .ToArray();

    /// <summary>
    ///     Documented transcripts, each as (file, the lines it claims the CLI prints). Markdown
    ///     comment prefixes (<c># </c>) used in the README's shell blocks are stripped.
    /// </summary>
    public static TheoryData<string, string> DocumentedTranscripts()
    {
        var root = RepositoryRoot();
        var data = new TheoryData<string, string>();

        foreach (var relative in new[]
                 {
                     "README.md",
                     Path.Combine("LogicalOptimizer.Cli", "README.md"),
                     Path.Combine("docs-site", "articles", "cli-usage.md"),
                     Path.Combine("docs-site", "articles", "introduction.md")
                 })
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path)) continue;

            var text = File.ReadAllText(path).ReplaceLineEndings("\n");

            // A transcript starts at the "Original: <the documented expression>" line and runs to
            // the end of its fenced block. Both the plain and the "# "-commented forms appear.
            foreach (Match match in Regex.Matches(text,
                         @"(?m)^(?<prefix>#\s)?Original:\s*" + Regex.Escape(DocumentedExpression) + @"\s*$"))
            {
                var block = text[match.Index..];
                var fenceEnd = block.IndexOf("\n```", StringComparison.Ordinal);
                if (fenceEnd > 0) block = block[..fenceEnd];

                var prefix = match.Groups["prefix"].Success;
                var lines = block.Split('\n')
                    .Select(line => line.TrimEnd())
                    // Stop at the first line that is not part of the transcript.
                    .TakeWhile(line => !prefix || line.StartsWith("#", StringComparison.Ordinal))
                    .Select(line => prefix && line.StartsWith("# ", StringComparison.Ordinal) ? line[2..] : line)
                    // The README annotates its trimmed block with a parenthetical aside.
                    .TakeWhile(line => !line.StartsWith("(", StringComparison.Ordinal))
                    .Where(line => line.Length > 0)
                    .ToArray();

                data.Add(relative.Replace('\\', '/'), string.Join("\n", lines));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentedTranscripts))]
    public void DocumentedTranscript_MatchesWhatTheCliPrints(string file, string documented)
    {
        var actual = NonEmptyLines(RenderDefaultOutput(DocumentedExpression));
        var claimed = NonEmptyLines(documented);

        Assert.True(claimed.Length > 0, $"{file}: extracted an empty transcript — the parser is wrong.");
        Assert.True(claimed.Length <= actual.Length,
            $"{file}: documents {claimed.Length} lines but the CLI prints {actual.Length}:\n" +
            string.Join("\n", actual));

        for (var i = 0; i < claimed.Length; i++)
            Assert.True(claimed[i] == actual[i],
                $"{file} line {i + 1} drifted from the CLI.\n" +
                $"  documented: {claimed[i]}\n" +
                $"  actual:     {actual[i]}\n\n" +
                "Update the documented block (running `logical-optimizer \"" + DocumentedExpression +
                "\"` prints the current output):\n" + string.Join("\n", actual));
    }

    [Fact]
    public void EveryDocumentedTranscript_IsActuallyFound()
    {
        // Guards the test itself: a renamed heading or a reformatted block must not turn this
        // suite into a no-op that silently stops checking anything.
        var found = DocumentedTranscripts()
            .Select(row => (string)row[0]!)
            .Distinct()
            .ToArray();

        Assert.Contains("README.md", found);
        Assert.Contains("LogicalOptimizer.Cli/README.md", found);
        Assert.Contains("docs-site/articles/cli-usage.md", found);
        Assert.Contains("docs-site/articles/introduction.md", found);
    }

    [Fact]
    public void TheVerifiedAgainstTheCliPromise_IsStillMade()
    {
        // The claim this test exists to back. If someone removes it, the enforcement should be
        // reconsidered deliberately rather than left running against nothing.
        var cliUsage = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs-site", "articles", "cli-usage.md"));
        Assert.Contains("verified against the built CLI", cliUsage, StringComparison.Ordinal);
    }
}
