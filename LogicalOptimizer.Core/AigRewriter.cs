namespace LogicalOptimizer;

/// <summary>
///     DAG-aware, cut-based AIG rewriting (ABC-style <c>rewrite</c>). For each AND node the
///     rewriter enumerates its ≤4-input cuts, extracts the local truth table, fetches an exact
///     replacement recipe from <see cref="AigRewriteLibrary" />, and — when the replacement is
///     strictly smaller — swaps it in. The result is always the same boolean function with a
///     non-increasing AND-node count.
///
///     <para>
///         <b>Fanout redirection — mechanism (b), whole-graph rebuild-with-substitution.</b>
///         The graph carries reference counts but no parent/fanout index, so instead of an
///         in-place fanout transfer this rewriter rebuilds the entire graph through the
///         structural-hashing <see cref="And" />, mapping the rewritten node's literal to the
///         replacement literal. Every node reachable from the Root is re-created except the ones
///         that lived only inside the rewritten node's maximum fanout-free cone — those simply
///         are not visited and so disappear, which is exactly the gain. This is inherently
///         correct (a node's function is reproduced from its own cut leaves, whose functions the
///         rebuild reproduces identically) and needs no fanout bookkeeping; the cost is an
///         O(graph) rebuild per accepted move, acceptable because correctness dominates and the
///         pass is opt-in and budgeted. A move is accepted only after the rebuilt graph is
///         observed to have a strictly smaller AND count, so the non-increasing invariant holds
///         by construction regardless of the gain estimate.
///     </para>
/// </summary>
internal sealed partial class AndInverterGraph
{
    /// <summary>
    ///     Apply cut-based rewriting until no size-reducing move remains or
    ///     <paramref name="maxRounds" /> passes have run. Returns a graph (possibly this one,
    ///     when nothing improved) computing the identical function with an AND-node count no
    ///     larger than this graph's. Honors <paramref name="cancellationToken" />.
    /// </summary>
    public AndInverterGraph RewriteToFixpoint(int maxRounds = 32, CancellationToken cancellationToken = default)
    {
        var current = this;
        for (var round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = current.RewriteOnce(cancellationToken);
            if (next is null) break;
            current = next;
        }

        return current;
    }

    /// <summary>
    ///     Find one strictly size-reducing rewrite and return the rebuilt graph, or null when
    ///     none exists. Candidates are ranked by an MFFC-based gain estimate; each is confirmed
    ///     by an actual rebuild whose AND count must be strictly smaller before it is accepted.
    /// </summary>
    private AndInverterGraph? RewriteOnce(CancellationToken cancellationToken)
    {
        var enumerator = new AigCutEnumerator(this);
        var cuts = enumerator.EnumerateAll();

        // Collect promising (node, cut) moves with a positive gain estimate, best first.
        var candidates = new List<(int Gain, int Node, Cut Cut, AigTemplate Template)>();
        for (var node = 1; node < _nodes.Count; node++)
        {
            if (_nodes[node].Left < 0) continue; // input or constant: nothing to rewrite
            if (_refCount[node] == 0) continue; // already dead

            var mffc = MffcSize(node << 1);
            foreach (var cut in cuts[node])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cut.Size < 2) continue; // trivial (the node itself)

                var truthTable = enumerator.CutTruthTable(node, cut);
                var template = AigRewriteLibrary.Synthesize(truthTable, cut.Size);

                var leafLiterals = new int[cut.Size];
                for (var i = 0; i < cut.Size; i++) leafLiterals[i] = cut.Leaves[i] << 1;
                var newNodes = CountNewAndNodes(template, leafLiterals);

                var gain = mffc - newNodes;
                if (gain > 0) candidates.Add((gain, node, cut, template));
            }
        }

        candidates.Sort((a, b) => b.Gain.CompareTo(a.Gain));

        var baseline = AndNodeCount;
        foreach (var (_, node, cut, template) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rebuilt = RebuildWithSubstitution(node, cut, template);
            if (rebuilt.AndNodeCount < baseline) return rebuilt;
        }

        return null;
    }

    /// <summary>
    ///     Count how many brand-new AND nodes replaying <paramref name="template" /> onto
    ///     <paramref name="leafLiterals" /> would create against the current structural-hash
    ///     table, without mutating the graph. Mirrors <see cref="And" />'s constant folding and
    ///     hashing exactly, plus a scratch table so nodes shared inside the template count once.
    /// </summary>
    private int CountNewAndNodes(in AigTemplate template, IReadOnlyList<int> leafLiterals)
    {
        var virtualTable = new Dictionary<(int, int), int>();
        var nextVirtual = _nodes.Count;

        int DryAnd(int a, int b)
        {
            if (a == FalseLiteral || b == FalseLiteral) return FalseLiteral;
            if (a == TrueLiteral) return b;
            if (b == TrueLiteral) return a;
            if (a == b) return a;
            if (a == (b ^ 1)) return FalseLiteral;

            var key = a <= b ? (a, b) : (b, a);
            if (_uniqueTable.TryGetValue(key, out var cached)) return cached;
            if (virtualTable.TryGetValue(key, out var scratch)) return scratch;

            var literal = nextVirtual++ << 1;
            virtualTable[key] = literal;
            return literal;
        }

        template.BuildInto(DryAnd, leafLiterals);
        return virtualTable.Count;
    }

    /// <summary>
    ///     Rebuild the whole graph, substituting the function at old node <paramref name="node" />
    ///     with <paramref name="template" /> instantiated on the literals of the rebuilt cut
    ///     leaves. Nodes that lived only inside the rewritten node's MFFC are never visited and
    ///     so vanish; everything else is reproduced through structural hashing.
    /// </summary>
    private AndInverterGraph RebuildWithSubstitution(int node, in Cut cut, in AigTemplate template)
    {
        var rebuilt = new AndInverterGraph();
        foreach (var name in _inputs) rebuilt.CreateInput(name);

        var map = new Dictionary<int, int>();
        rebuilt.Root = RebuildSubst(Root, rebuilt, map, node, cut, template);
        rebuilt._refCount[rebuilt.Root >> 1]++; // Root counts as a primary-output reference.
        return rebuilt;
    }

    private int RebuildSubst(int literal, AndInverterGraph target, Dictionary<int, int> map,
        int substNode, in Cut cut, in AigTemplate template)
    {
        var node = literal >> 1;
        var complemented = literal & 1;
        if (node == 0) return FalseLiteral ^ complemented;

        if (!map.TryGetValue(node, out var mapped))
        {
            if (node == substNode)
            {
                var leafLiterals = new int[cut.Size];
                for (var i = 0; i < cut.Size; i++)
                    leafLiterals[i] = RebuildSubst(cut.Leaves[i] << 1, target, map, substNode, cut, template);
                mapped = template.BuildInto(target, leafLiterals);
            }
            else
            {
                var (left, right) = _nodes[node];
                mapped = left < 0
                    ? target._inputLiteral[_inputs[node - 1]]
                    : target.And(RebuildSubst(left, target, map, substNode, cut, template),
                        RebuildSubst(right, target, map, substNode, cut, template));
            }

            map[node] = mapped;
        }

        return mapped ^ complemented;
    }
}
