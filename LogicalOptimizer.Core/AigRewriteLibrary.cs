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
        var map = new int[1 + NumInputs + Gates.Length];
        map[0] = AndInverterGraph.FalseLiteral;
        for (var i = 0; i < NumInputs; i++) map[i + 1] = leafLiterals[i];

        for (var g = 0; g < Gates.Length; g++)
        {
            var (a, b) = Gates[g];
            var litA = map[a >> 1] ^ (a & 1);
            var litB = map[b >> 1] ^ (b & 1);
            map[1 + NumInputs + g] = target.And(litA, litB);
        }

        return map[Output >> 1] ^ (Output & 1);
    }
}

/// <summary>
///     Compact ≤4-input AIG rewrite library. Given a local truth table it returns an
///     <see cref="AigTemplate" /> that implements EXACTLY that function over the cut leaves.
///
///     <para>
///         Approach and optimality note: the library is <b>NPN-class based</b> — a truth
///         table is canonicalized (<see cref="NpnCanonicalizer" />), a template for the
///         canonical representative is built once (there are at most 222 four-input classes)
///         and cached, then the forward NPN transform re-labels the canonical inputs onto the
///         concrete leaves (permuting/negating inputs and optionally negating the output).
///         Canonical templates are synthesized by a <b>memoized Shannon (ITE) decomposition</b>
///         with constant folding and structural hashing (the smart-ITE recognizes AND/OR/MUX
///         boundaries so plain gates stay minimal). This is a deterministic <i>constructive</i>
///         synthesis: it is guaranteed correct but <b>not guaranteed AND-minimal</b>. Memoized
///         cofactor sharing keeps the structures compact in practice; since D1c gates every
///         rewrite on gain &gt; 0 and equivalence, a correct-but-not-minimal library is safe.
///     </para>
/// </summary>
internal static class AigRewriteLibrary
{
    // Cache of canonical templates keyed by (numInputs, canonicalTruthTable). Bounded by the
    // number of NPN classes (≤ 222 for four inputs). Lazily populated, never precomputed at
    // static init, so nothing heavy runs unless a rewrite actually needs it.
    private static readonly Dictionary<(int, uint), AigTemplate> CanonicalCache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    ///     Build a template implementing <paramref name="truthTable" /> over
    ///     <paramref name="numInputs" /> leaves (numInputs ≤ 4).
    /// </summary>
    public static AigTemplate Synthesize(uint truthTable, int numInputs)
    {
        var npn = NpnCanonicalizer.Canonicalize(truthTable, numInputs);
        var canonical = GetCanonicalTemplate(npn.Canonical, numInputs);
        return Instantiate(canonical, npn.Forward, numInputs);
    }

    private static AigTemplate GetCanonicalTemplate(uint canonicalTruthTable, int numInputs)
    {
        var key = (numInputs, canonicalTruthTable);
        lock (CacheLock)
        {
            if (CanonicalCache.TryGetValue(key, out var cached)) return cached;
            var template = BuildViaShannon(canonicalTruthTable, numInputs);
            CanonicalCache[key] = template;
            return template;
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

    /// <summary>
    ///     Synthesize a template for an arbitrary truth table via memoized Shannon expansion.
    ///     Reuses <see cref="AndInverterGraph" /> so the well-tested constant folding and
    ///     structural hashing drive compactness; the template is then read back from the
    ///     graph's node array (constant, then the m inputs, then AND gates).
    /// </summary>
    private static AigTemplate BuildViaShannon(uint truthTable, int m)
    {
        var graph = new AndInverterGraph();
        var leaves = new int[m];
        for (var i = 0; i < m; i++) leaves[i] = graph.CreateInput("x" + i);

        var memo = new Dictionary<uint, int>();
        var output = Shannon(graph, truthTable, m, leaves, memo);

        var nodes = graph.SnapshotNodes();
        var gates = new List<(int A, int B)>();
        for (var node = 1 + m; node < nodes.Length; node++)
            gates.Add((nodes[node].Left, nodes[node].Right));

        return new AigTemplate(m, gates.ToArray(), output);
    }

    private static int Shannon(AndInverterGraph graph, uint tt, int m, int[] leaves,
        Dictionary<uint, int> memo)
    {
        var mask = TruthTableOps.Mask(m);
        tt &= mask;
        if (tt == 0) return AndInverterGraph.FalseLiteral;
        if (tt == mask) return AndInverterGraph.TrueLiteral;
        if (memo.TryGetValue(tt, out var cached)) return cached;

        // Split on the lowest support variable.
        var split = LowestSupport(tt, m);
        var (cofactor0, cofactor1) = Cofactors(tt, split, m);
        var low = Shannon(graph, cofactor0, m, leaves, memo);
        var high = Shannon(graph, cofactor1, m, leaves, memo);

        var result = SmartIte(graph, leaves[split], high, low);
        memo[tt] = result;
        return result;
    }

    /// <summary>ITE that collapses AND/OR/identity boundaries so simple gates stay minimal.</summary>
    private static int SmartIte(AndInverterGraph graph, int condition, int thenLit, int elseLit)
    {
        if (thenLit == AndInverterGraph.TrueLiteral && elseLit == AndInverterGraph.FalseLiteral) return condition;
        if (thenLit == AndInverterGraph.FalseLiteral && elseLit == AndInverterGraph.TrueLiteral)
            return AndInverterGraph.Not(condition);
        if (thenLit == AndInverterGraph.TrueLiteral) return graph.Or(condition, elseLit);
        if (elseLit == AndInverterGraph.FalseLiteral) return graph.And(condition, thenLit);
        if (thenLit == AndInverterGraph.FalseLiteral) return graph.And(AndInverterGraph.Not(condition), elseLit);
        if (elseLit == AndInverterGraph.TrueLiteral) return graph.Or(AndInverterGraph.Not(condition), thenLit);
        return graph.Ite(condition, thenLit, elseLit);
    }

    private static int LowestSupport(uint tt, int m)
    {
        for (var i = 0; i < m; i++)
        {
            var (c0, c1) = Cofactors(tt, i, m);
            if (c0 != c1) return i;
        }

        return 0; // unreachable for a non-constant tt
    }

    /// <summary>
    ///     Negative and positive cofactors with respect to variable <paramref name="var" />,
    ///     each returned as a full m-input truth table independent of that variable.
    /// </summary>
    private static (uint Cofactor0, uint Cofactor1) Cofactors(uint tt, int var, int m)
    {
        var size = 1 << m;
        uint c0 = 0, c1 = 0;
        for (var p = 0; p < size; p++)
        {
            var withoutBit = p & ~(1 << var);
            if (((tt >> withoutBit) & 1) != 0) c0 |= 1u << p;
            var withBit = p | (1 << var);
            if (((tt >> withBit) & 1) != 0) c1 |= 1u << p;
        }

        return (c0, c1);
    }
}
