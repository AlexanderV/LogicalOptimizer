using System.Text;

namespace LogicalOptimizer.Benchmarks;

/// <summary>One generated BDD variable-order corpus member.</summary>
/// <param name="FileName">Committed file name under BddOrderCorpus/.</param>
/// <param name="Kind">Structure family: "eq-comparator" or "disjoint-pairs".</param>
/// <param name="Bits">Number of (a_i, b_i) bit pairs.</param>
/// <param name="InterleavedByName">
///     True when the variable NAMES are chosen so the engine's default alphabetical order is
///     the good interleaved order (a_i next to b_i); false when alphabetical order is the
///     adversarial separated order (all a's before all b's).
/// </param>
/// <param name="Expression">The formula in parser syntax (!/&amp;/|).</param>
/// <param name="Content">Full committed file text (# header + expression line).</param>
public sealed record BddOrderInstance(
    string FileName, string Kind, int Bits, bool InterleavedByName, string Expression, string Content);

/// <summary>
///     Deterministic generator for the vendored adversarial BDD variable-order corpus
///     (LogicalOptimizer.Benchmarks/BddOrderCorpus). Closes part of competitive-assessment
///     gap #3 (30-day roadmap item 1: adversarial BDD suite). Two classic order-sensitive
///     structures over n bit pairs (a_i, b_i):
///     <list type="bullet">
///         <item>
///             <b>eq-comparator</b> — EQ(a,b) = AND_i (a_i &lt;-&gt; b_i), written as SOP
///             factors. Interleaved order (a1,b1,a2,b2,…) gives a linear-size ROBDD;
///             separated order (a1..an,b1..bn) is exponential (~2^n nodes).
///         </item>
///         <item>
///             <b>disjoint-pairs</b> — OR_i (a_i &amp; b_i), Bryant's textbook example:
///             linear when the pairs are adjacent in the order, ~2^n when separated.
///         </item>
///     </list>
///     The engine's public <c>Build</c> sorts variables alphabetically, so each structure is
///     committed twice with different VARIABLE NAMES: <c>*-interleaved</c> names the bits
///     <c>x01a,x01b,…</c> (alphabetical = good interleaved order) and <c>*-separated</c>
///     names them <c>a01..a{n},b01..b{n}</c> (alphabetical = adversarial separated order).
///     The formula text itself always lists the pairs together, so first-appearance order —
///     one of <c>BuildWithBestOrder</c>'s candidate heuristics — remains interleaved even
///     for the adversarially named files (that is what the recovery regression exercises).
///     No randomness at all; regeneration is byte-identical. Regenerate with:
///     <c>dotnet run -c Release --project LogicalOptimizer.Benchmarks -- generate-corpora</c>.
/// </summary>
public static class BddOrderCorpusGenerator
{
    private static readonly int[] EqBitSizes = { 10, 12 };
    private static readonly int[] PairsBitSizes = { 12, 14 };

    public static IReadOnlyList<BddOrderInstance> Generate()
    {
        var instances = new List<BddOrderInstance>();
        foreach (var bits in EqBitSizes)
        {
            instances.Add(Equality(bits, interleavedByName: true));
            instances.Add(Equality(bits, interleavedByName: false));
        }

        foreach (var bits in PairsBitSizes)
        {
            instances.Add(DisjointPairs(bits, interleavedByName: true));
            instances.Add(DisjointPairs(bits, interleavedByName: false));
        }

        return instances;
    }

    /// <summary>The expression payload of a committed corpus file: its last non-comment line.</summary>
    public static string ReadExpression(string path)
    {
        var expression = File.ReadLines(path)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0 && !line.StartsWith('#'));
        return expression ?? throw new FormatException($"No expression line in {path}");
    }

    private static BddOrderInstance Equality(int bits, bool interleavedByName)
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= bits; i++)
        {
            if (i > 1) builder.Append(" & ");
            var (a, b) = Names(i, interleavedByName);
            builder.Append($"({a} & {b} | !{a} & !{b})");
        }

        return Finish("eq", "eq-comparator", bits, interleavedByName, builder.ToString(),
            "EQ(a,b) = AND_i (a_i <-> b_i): linear ROBDD interleaved, ~2^n separated");
    }

    private static BddOrderInstance DisjointPairs(int bits, bool interleavedByName)
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= bits; i++)
        {
            if (i > 1) builder.Append(" | ");
            var (a, b) = Names(i, interleavedByName);
            builder.Append($"{a} & {b}");
        }

        return Finish("pairs", "disjoint-pairs", bits, interleavedByName, builder.ToString(),
            "OR_i (a_i & b_i), Bryant's example: linear ROBDD interleaved, ~2^n separated");
    }

    /// <summary>
    ///     Pair names for bit <paramref name="i" />. Interleaved naming sorts as
    ///     x01a,x01b,x02a,… (pairs adjacent); separated naming sorts as a01..a(n),b01..b(n).
    /// </summary>
    private static (string A, string B) Names(int i, bool interleavedByName)
    {
        return interleavedByName ? ($"x{i:D2}a", $"x{i:D2}b") : ($"a{i:D2}", $"b{i:D2}");
    }

    private static BddOrderInstance Finish(string prefix, string kind, int bits,
        bool interleavedByName, string expression, string note)
    {
        var order = interleavedByName ? "interleaved" : "separated";
        var fileName = $"{prefix}{bits:D2}-{order}.expr";
        var content = string.Join('\n', new[]
        {
            $"# kind: {kind}",
            $"# bits: {bits}",
            $"# alphabetical-order: {order}" + (interleavedByName
                ? " (good: a_i and b_i adjacent)"
                : " (adversarial: all a's before all b's)"),
            $"# note: {note}",
            "# generator: LogicalOptimizer.Benchmarks BddOrderCorpusGenerator (deterministic, no randomness)",
            "# regenerate: dotnet run -c Release --project LogicalOptimizer.Benchmarks -- generate-corpora",
            expression
        }) + "\n";
        return new BddOrderInstance(fileName, kind, bits, interleavedByName, expression, content);
    }
}
