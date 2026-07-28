namespace LogicalOptimizer;

/// <summary>
///     A k-feasible cut: the set of node indices (the "leaves") that feed a root node and
///     cut off its fanin cone. Leaves are stored sorted ascending and distinct, so subset
///     (dominance) tests and equality are cheap. A cut is used together with the root node
///     it belongs to; the root is not stored here (it is the dictionary key in
///     <see cref="AigCutEnumerator.EnumerateAll" />).
/// </summary>
internal readonly struct Cut
{
    public Cut(int[] leaves)
    {
        Leaves = leaves;
    }

    /// <summary>Leaf node indices, sorted ascending and distinct.</summary>
    public int[] Leaves { get; }

    public int Size => Leaves.Length;

    /// <summary>True when this cut's leaves are a subset of <paramref name="other" />'s.</summary>
    public bool IsSubsetOf(in Cut other)
    {
        if (Leaves.Length > other.Leaves.Length) return false;
        var j = 0;
        foreach (var leaf in Leaves)
        {
            while (j < other.Leaves.Length && other.Leaves[j] < leaf) j++;
            if (j == other.Leaves.Length || other.Leaves[j] != leaf) return false;
            j++;
        }

        return true;
    }
}

/// <summary>
///     k-feasible cut enumeration over an <see cref="AndInverterGraph" /> plus local
///     truth-table extraction for each cut. Cuts are computed bottom-up: the trivial cut of
///     every node is the node itself; an AND node's cuts are the pairwise unions of its two
///     fanins' cuts whose size stays ≤ k. Dominated (non-minimal) cuts are pruned and the
///     surviving set is capped per node for tractability. The class snapshots the graph's
///     node array once at construction, so it must be rebuilt if the graph mutates.
/// </summary>
internal sealed class AigCutEnumerator
{
    private readonly int _maxCutsPerNode;
    private readonly (int Left, int Right)[] _nodes;

    public AigCutEnumerator(AndInverterGraph graph, int maxCutsPerNode = 12)
    {
        _nodes = graph.SnapshotNodes();
        _maxCutsPerNode = Math.Max(1, maxCutsPerNode);
    }

    private bool IsLeaf(int node)
    {
        // Inputs (Left < 0, index > 0) and the constant node (index 0) are leaves: they
        // have no AND fanins to expand into.
        return node == 0 || _nodes[node].Left < 0;
    }

    /// <summary>
    ///     Enumerate k-feasible cuts for every node, bottom-up. The result maps each node
    ///     index to its cut list; the trivial cut is always present and appears first.
    /// </summary>
    public Dictionary<int, List<Cut>> EnumerateAll(int k = 4)
    {
        var cuts = new Dictionary<int, List<Cut>>(_nodes.Length);
        for (var node = 1; node < _nodes.Length; node++)
            cuts[node] = ComputeCuts(node, k, cuts);
        return cuts;
    }

    /// <summary>k-feasible cuts of a single node (bottom-up over its fanin cone).</summary>
    public IReadOnlyList<Cut> Enumerate(int node, int k = 4)
    {
        var cuts = new Dictionary<int, List<Cut>>();
        for (var n = 1; n <= node; n++)
            if (!cuts.ContainsKey(n))
                ComputeInto(n, k, cuts);
        return cuts[node];
    }

    private void ComputeInto(int node, int k, Dictionary<int, List<Cut>> cuts)
    {
        cuts[node] = ComputeCuts(node, k, cuts);
    }

    private List<Cut> ComputeCuts(int node, int k, Dictionary<int, List<Cut>> cuts)
    {
        var trivial = new Cut(new[] { node });
        if (IsLeaf(node)) return new List<Cut> { trivial };

        var (left, right) = _nodes[node];
        var leftCuts = cuts[left >> 1];
        var rightCuts = cuts[right >> 1];

        var result = new List<Cut> { trivial };
        foreach (var cu in leftCuts)
            foreach (var cv in rightCuts)
            {
                var merged = Merge(cu.Leaves, cv.Leaves);
                if (merged.Length > k) continue;
                AddPruned(result, new Cut(merged));
            }

        // Cap: keep the smallest cuts (the trivial one, size 1, always survives).
        if (result.Count > _maxCutsPerNode)
        {
            result.Sort((a, b) => a.Size.CompareTo(b.Size));
            result.RemoveRange(_maxCutsPerNode, result.Count - _maxCutsPerNode);
        }

        return result;
    }

    /// <summary>
    ///     Insert <paramref name="candidate" /> keeping the list minimal: skip it when an
    ///     existing cut dominates it (is a subset), and drop any existing cut it dominates.
    /// </summary>
    private static void AddPruned(List<Cut> cuts, Cut candidate)
    {
        for (var i = 0; i < cuts.Count; i++)
            if (cuts[i].IsSubsetOf(candidate))
                return; // dominated by an existing (or equal) cut

        cuts.RemoveAll(existing => candidate.IsSubsetOf(existing));
        cuts.Add(candidate);
    }

    private static int[] Merge(int[] a, int[] b)
    {
        var merged = new int[a.Length + b.Length];
        int i = 0, j = 0, n = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] < b[j]) merged[n++] = a[i++];
            else if (a[i] > b[j]) merged[n++] = b[j++];
            else
            {
                merged[n++] = a[i++];
                j++;
            }
        }

        while (i < a.Length) merged[n++] = a[i++];
        while (j < b.Length) merged[n++] = b[j++];
        if (n == merged.Length) return merged;
        Array.Resize(ref merged, n);
        return merged;
    }

    /// <summary>
    ///     Local truth table of <paramref name="node" /> as a function of a cut's leaves,
    ///     computed by simulating the AIG cone between the leaves and the node. Leaf i takes
    ///     the projection column of input i; the result is exact. Bit p of the returned
    ///     value is the node's value when the leaves hold the pattern p (leaf i = bit i).
    /// </summary>
    public uint CutTruthTable(int node, in Cut cut)
    {
        var m = cut.Leaves.Length;
        var mask = TruthTableOps.Mask(m);
        var projection = TruthTableOps.Projections(m);

        var leafColumn = new Dictionary<int, uint>(m);
        for (var i = 0; i < m; i++) leafColumn[cut.Leaves[i]] = projection[i];

        var memo = new Dictionary<int, uint>();
        return Simulate(node, leafColumn, memo, mask);
    }

    private uint Simulate(int node, Dictionary<int, uint> leafColumn, Dictionary<int, uint> memo, uint mask)
    {
        if (leafColumn.TryGetValue(node, out var column)) return column;
        if (node == 0) return 0; // constant false
        if (memo.TryGetValue(node, out var cached)) return cached;

        var (left, right) = _nodes[node];
        var lt = Simulate(left >> 1, leafColumn, memo, mask) ^ ((left & 1) == 1 ? mask : 0);
        var rt = Simulate(right >> 1, leafColumn, memo, mask) ^ ((right & 1) == 1 ? mask : 0);
        var value = lt & rt & mask;
        memo[node] = value;
        return value;
    }
}
