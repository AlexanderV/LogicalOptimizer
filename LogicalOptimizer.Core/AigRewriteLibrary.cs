namespace LogicalOptimizer;

/// <summary>
///     A self-contained AIG "template": a small recipe of two-input AND gates over m leaf
///     inputs that D1c can replay into a target <see cref="AndInverterGraph" />. Node
///     numbering mirrors an AIG: node 0 is the constant, nodes 1..m are the leaves (input i
///     is node i+1), and each gate is the next node index in order. Literals use the ABC
///     convention <c>node &lt;&lt; 1 | complement</c>, so a gate's operands reference the
///     constant, the leaves, or an earlier gate. <see cref="Output" /> is the literal that
///     realizes the requested function.
/// </summary>
internal readonly struct AigTemplate
{
    public AigTemplate(int numInputs, (int A, int B)[] gates, int output)
    {
        NumInputs = numInputs;
        Gates = gates;
        Output = output;
    }

    public int NumInputs { get; }

    /// <summary>AND gates in build order; operands are literals over earlier nodes.</summary>
    public (int A, int B)[] Gates { get; }

    /// <summary>Output literal realizing the function.</summary>
    public int Output { get; }

    /// <summary>Number of AND nodes in the template — the compactness metric.</summary>
    public int AndCount => Gates.Length;

    /// <summary>Simulate the template over all 2^m leaf patterns and return its truth table.</summary>
    public uint Simulate()
    {
        var mask = TruthTableOps.Mask(NumInputs);
        var projection = TruthTableOps.Projections(NumInputs);
        var nodeTable = new uint[1 + NumInputs + Gates.Length];
        nodeTable[0] = 0; // constant false
        for (var i = 0; i < NumInputs; i++) nodeTable[i + 1] = projection[i];

        for (var g = 0; g < Gates.Length; g++)
        {
            var (a, b) = Gates[g];
            var lt = Literal(nodeTable, a, mask);
            var rt = Literal(nodeTable, b, mask);
            nodeTable[1 + NumInputs + g] = lt & rt & mask;
        }

        return Literal(nodeTable, Output, mask);
    }

    private static uint Literal(uint[] nodeTable, int literal, uint mask)
    {
        var value = nodeTable[literal >> 1];
        return (literal & 1) == 1 ? value ^ mask : value;
    }

    /// <summary>
    ///     Replay the template into <paramref name="target" />, mapping leaf i to
    ///     <paramref name="leafLiterals" />[i], and return the output literal. This is the
    ///     bridge the D1c apply loop uses to instantiate a library structure onto a cut.
    /// </summary>
    public int BuildInto(AndInverterGraph target, IReadOnlyList<int> leafLiterals)
    {
        return BuildInto(target.And, leafLiterals);
    }

    /// <summary>
    ///     Replay the template through an arbitrary two-input AND builder. The graph overload
    ///     passes <see cref="AndInverterGraph.And" />; the rewriter also uses this with a
    ///     non-mutating "dry" AND to count how many new nodes a replacement would create
    ///     without committing it to the graph.
    /// </summary>
    public int BuildInto(Func<int, int, int> and, IReadOnlyList<int> leafLiterals)
    {
        var map = new int[1 + NumInputs + Gates.Length];
        map[0] = AndInverterGraph.FalseLiteral;
        for (var i = 0; i < NumInputs; i++) map[i + 1] = leafLiterals[i];

        for (var g = 0; g < Gates.Length; g++)
        {
            var (a, b) = Gates[g];
            var litA = map[a >> 1] ^ (a & 1);
            var litB = map[b >> 1] ^ (b & 1);
            map[1 + NumInputs + g] = and(litA, litB);
        }

        return map[Output >> 1] ^ (Output & 1);
    }
}

/// <summary>
///     Compact ≤4-input AIG rewrite library. Given a local truth table it returns an
///     <see cref="AigTemplate" /> that implements EXACTLY that function over the cut leaves
///     using the <b>provably minimum</b> number of two-input AND nodes.
///
///     <para>
///         Approach: the library is <b>NPN-class based</b> — a truth table is canonicalized
///         (<see cref="NpnCanonicalizer" />), the canonical representative's recipe is looked
///         up in a baked table (<see cref="AigMinLibraryData" />), then the forward NPN
///         transform re-labels the canonical inputs onto the concrete leaves (permuting/negating
///         inputs and optionally negating the output). The baked table holds one minimum-AND
///         recipe per NPN class over 1..4 inputs (2, 4, 14 and 222 classes respectively). It is
///         precomputed offline by a SAT-based exact-synthesis generator with complete BFS
///         lower bounds (see the LogicalOptimizer.Tests <c>AigMinLibraryGenerator</c> and the
///         Exhaustive <c>AigMinLibraryTests</c>), so every recipe is certified to use the fewest
///         possible AND nodes. Runtime lookup is O(1): the raw <see cref="AigMinLibraryData" />
///         arrays are indexed into per-class dictionaries on first use (222 tiny entries — no
///         heavy static initialisation), which matters because AIG rewriting is meant to run by
///         default. This replaces the earlier constructive Shannon/ITE synthesis, which was
///         correct but not AND-minimal.
///     </para>
/// </summary>
internal static class AigRewriteLibrary
{
    // Per-input-count lookup from a canonical truth table to its minimum-AND template, built
    // lazily from the baked AigMinLibraryData arrays on first use (index 0/5 unused). Nothing
    // heavy runs at static init; the first rewrite that needs a given arity materialises that
    // arity's ≤222-entry dictionary in microseconds.
    private static readonly Dictionary<uint, AigTemplate>?[] Tables = new Dictionary<uint, AigTemplate>?[5];
    private static readonly object TablesLock = new();

    /// <summary>
    ///     Build a template implementing <paramref name="truthTable" /> over
    ///     <paramref name="numInputs" /> leaves (numInputs ≤ 4) with the minimum AND-node count.
    /// </summary>
    public static AigTemplate Synthesize(uint truthTable, int numInputs)
    {
        var npn = NpnCanonicalizer.Canonicalize(truthTable, numInputs);
        var canonical = GetCanonicalTemplate(npn.Canonical, numInputs);
        return Instantiate(canonical, npn.Forward, numInputs);
    }

    private static AigTemplate GetCanonicalTemplate(uint canonicalTruthTable, int numInputs)
    {
        var table = GetTable(numInputs);
        if (table.TryGetValue(canonicalTruthTable, out var template)) return template;
        throw new InvalidOperationException(
            $"No baked minimum-AIG recipe for canonical 0x{canonicalTruthTable:X} over {numInputs} inputs");
    }

    private static Dictionary<uint, AigTemplate> GetTable(int numInputs)
    {
        lock (TablesLock)
        {
            if (Tables[numInputs] is { } existing) return existing;
            var data = AigMinLibraryData.For(numInputs);
            var table = new Dictionary<uint, AigTemplate>(data.Length);
            foreach (var recipe in data)
            {
                // recipe = { canonicalTt, outputLiteral, gateCount, aLit0, bLit0, aLit1, bLit1, ... }
                var canonical = (uint)recipe[0];
                var output = recipe[1];
                var gateCount = recipe[2];
                var gates = new (int A, int B)[gateCount];
                for (var g = 0; g < gateCount; g++) gates[g] = (recipe[3 + 2 * g], recipe[4 + 2 * g]);
                table[canonical] = new AigTemplate(numInputs, gates, output);
            }

            Tables[numInputs] = table;
            return table;
        }
    }

    /// <summary>
    ///     Re-label a canonical template onto concrete leaves using the forward NPN transform
    ///     (original → canonical): canonical input i is driven by leaf <c>perm[i]</c>,
    ///     complemented when its negation bit is set, and the output is complemented when the
    ///     transform negates the output. The gate structure is unchanged.
    /// </summary>
    private static AigTemplate Instantiate(AigTemplate canonical, NpnTransform forward, int m)
    {
        int MapLiteral(int literal)
        {
            var node = literal >> 1;
            var complement = literal & 1;
            if (node == 0) return literal; // constant
            if (node <= m)
            {
                var input = node - 1; // canonical input index
                var leaf = forward.Perm[input];
                var negated = complement ^ ((forward.NegMask >> input) & 1);
                return ((leaf + 1) << 1) | negated;
            }

            return literal; // gate node: index unchanged
        }

        var gates = new (int A, int B)[canonical.Gates.Length];
        for (var g = 0; g < gates.Length; g++)
            gates[g] = (MapLiteral(canonical.Gates[g].A), MapLiteral(canonical.Gates[g].B));

        var output = MapLiteral(canonical.Output) ^ forward.OutNeg;
        return new AigTemplate(m, gates, output);
    }
}
