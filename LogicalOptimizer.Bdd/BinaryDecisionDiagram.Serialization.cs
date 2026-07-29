namespace LogicalOptimizer;

public sealed partial class BinaryDecisionDiagram
{
    // The terminal ONE keeps output index 0; every reachable internal node is emitted in
    // post-order, so a node's children always have a strictly smaller index than the node
    // itself. That makes the byte output a deterministic function of the diagram (independent of
    // build history and dead nodes) and lets the loader verify acyclicity by an index comparison.

    /// <summary>
    ///     <b>Experimental (until v4).</b> Serialize this diagram to a compact, self-describing binary
    ///     blob (little-endian, CRC-32 checked). The output is deterministic — the same diagram always
    ///     produces identical bytes — and can be read back with <see cref="Load" /> into a valid
    ///     hash-consed manager whose queries (variable set, model count, evaluation, enumeration)
    ///     answer identically. Both the variable identities and the current variable ORDER are stored,
    ///     so a sifted order round-trips exactly.
    ///     <para>
    ///         The format is EXPERIMENTAL: it may change before v4 and carries no cross-version
    ///         compatibility guarantee other than the version gate, which makes a future build refuse
    ///         (rather than misread) a blob it does not understand. The engine byte makes a BDD blob a
    ///         typed error if it is loaded as a d-DNNF circuit. No reflection or object deserialization
    ///         is used.
    ///     </para>
    /// </summary>
    /// <param name="destination">The stream the blob is written to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination" /> is null.</exception>
    public void Save(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var writer = new CircuitBinaryWriter(CircuitEngine.Bdd);

        // Variable table in IDENTITY (id) order: position i is the name of variable id i.
        writer.WriteInt32(_variables.Count);
        foreach (var name in _variables) writer.WriteString(name);

        // Variable ORDER: the variable id sitting at each level, top to bottom.
        for (var level = 0; level < _variables.Count; level++) writer.WriteInt32(_levelVar[level]);

        // Canonical renumbering: terminal ONE -> 0, reachable internal nodes -> 1.. in post-order.
        var map = new int[_nodes.Count];
        Array.Fill(map, -1);
        map[One] = 0;
        var ordered = new List<int>();
        var next = 1;

        var state = new byte[_nodes.Count]; // 0 unseen, 1 in progress, 2 done
        state[One] = 2;
        var stack = new Stack<int>();
        stack.Push(NodeOf(Root));
        while (stack.Count > 0)
        {
            var node = stack.Peek();
            if (state[node] == 2)
            {
                stack.Pop();
                continue;
            }

            var (_, low, high) = _nodes[node];
            if (state[node] == 0)
            {
                state[node] = 1;
                // Push high then low so low finishes first (deterministic emission order).
                if (state[NodeOf(high)] != 2) stack.Push(NodeOf(high));
                if (state[NodeOf(low)] != 2) stack.Push(NodeOf(low));
            }
            else
            {
                state[node] = 2;
                map[node] = next++;
                ordered.Add(node);
                stack.Pop();
            }
        }

        writer.WriteInt32(ordered.Count);
        foreach (var node in ordered)
        {
            var (variable, low, high) = _nodes[node];
            writer.WriteInt32(variable);
            writer.WriteInt32(RemapEdge(low, map));
            writer.WriteInt32(RemapEdge(high, map));
        }

        writer.WriteInt32(RemapEdge(Root, map));
        writer.Finish(destination);
    }

    private static int RemapEdge(int edge, int[] map)
    {
        return (map[edge >> 1] << 1) | (edge & 1);
    }

    /// <summary>
    ///     <b>Experimental (until v4).</b> Read a diagram back from a blob produced by
    ///     <see cref="Save" /> into a valid hash-consed manager. The load is fully validated — magic,
    ///     format version (a newer version is refused, not misread), engine byte (a d-DNNF blob is a
    ///     typed error here), CRC-32 checksum, and the node table's structure: a genuine variable-order
    ///     permutation, variable indices in range, children strictly before their parent (acyclic) and
    ///     at a deeper level (a reduced ORDERED diagram), no duplicate or redundant nodes, and a valid
    ///     root. The checksum only catches corruption; it does not replace the structural checks.
    ///     <para>
    ///         The read is budgeted and never trusts a length field to pre-size an allocation: a header
    ///         claiming a huge node count is checked against <paramref name="budget" /> and against the
    ///         actual stream, so a hostile blob aborts with <see cref="NodeBudgetExceededException" />
    ///         or a truncation error rather than allocating unboundedly. Any malformed input is a
    ///         <see cref="CircuitSerializationException" />.
    ///     </para>
    /// </summary>
    /// <param name="source">The stream to read the blob from.</param>
    /// <param name="budget">Load budget; the node table is bounded by <see cref="ResourceBudget.BddNodeLimit" />. Defaults to <see cref="ResourceBudget.Default" />.</param>
    /// <param name="cancellationToken">Cancels a long load.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> is null.</exception>
    /// <exception cref="CircuitSerializationException">The blob is malformed, corrupt, a newer version, or the wrong engine.</exception>
    /// <exception cref="NodeBudgetExceededException">The blob's declared size exceeds the load budget.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static BinaryDecisionDiagram Load(Stream source, ResourceBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        budget ??= ResourceBudget.Default;
        var nodeBudget = budget.BddNodeLimit;

        var reader = new CircuitBinaryReader(source, CircuitEngine.Bdd);

        var variableCount = reader.ReadCount(nodeBudget, "variables");
        var names = new List<string>();
        var seenNames = new HashSet<string>();
        for (var i = 0; i < variableCount; i++)
        {
            if ((i & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var name = reader.ReadString();
            if (!seenNames.Add(name))
                throw new CircuitSerializationException($"Duplicate variable name '{name}' in BDD blob.");
            names.Add(name);
        }

        // Variable order: a permutation of the variable ids, one id per level.
        var levelVar = new int[variableCount];
        var varLevel = new int[variableCount];
        var seenLevel = new bool[variableCount];
        for (var level = 0; level < variableCount; level++)
        {
            var id = reader.ReadInt32();
            if (id < 0 || id >= variableCount || seenLevel[id])
                throw new CircuitSerializationException("BDD variable order is not a valid permutation.");
            seenLevel[id] = true;
            levelVar[level] = id;
            varLevel[id] = level;
        }

        var nodeCount = reader.ReadCount(nodeBudget, "nodes");
        var internalNodes = new List<(int Variable, int Low, int High)>();
        var seenKeys = new HashSet<(int, int, int)>();
        for (var index = 1; index <= nodeCount; index++)
        {
            if ((index & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var variable = reader.ReadInt32();
            if (variable < 0 || variable >= variableCount)
                throw new CircuitSerializationException($"Node {index} references variable {variable} out of range.");
            var low = ReadChildEdge(reader, index, "low");
            var high = ReadChildEdge(reader, index, "high");

            if (IsComplemented(high))
                throw new CircuitSerializationException(
                    $"Node {index} stores a complemented high edge, violating the canonical invariant.");
            if (low == high)
                throw new CircuitSerializationException($"Node {index} is redundant (low == high).");

            // Reduced-ORDERED check: each child must branch on a strictly deeper variable (or be terminal).
            var nodeLevel = varLevel[variable];
            EnsureDeeper(low, nodeLevel, varLevel, internalNodes, index);
            EnsureDeeper(high, nodeLevel, varLevel, internalNodes, index);

            var key = (variable, low, high);
            if (!seenKeys.Add(key))
                throw new CircuitSerializationException($"Duplicate node {key} in BDD blob (not hash-consed).");
            internalNodes.Add(key);
        }

        var rootEdge = reader.ReadInt32();
        if (rootEdge < 0 || rootEdge >> 1 > nodeCount)
            throw new CircuitSerializationException($"Root edge {rootEdge} is out of range.");

        reader.Finish();

        var bdd = new BinaryDecisionDiagram(names, sortVariables: false, nodeBudget, cancellationToken);
        bdd.RestoreSerialized(levelVar, internalNodes, rootEdge);
        return bdd;
    }

    private static int ReadChildEdge(CircuitBinaryReader reader, int index, string which)
    {
        var edge = reader.ReadInt32();
        if (edge < 0 || edge >> 1 >= index)
            throw new CircuitSerializationException(
                $"Node {index} {which} edge {edge} does not reference a strictly earlier node.");
        return edge;
    }

    private static void EnsureDeeper(int edge, int nodeLevel, int[] varLevel,
        List<(int Variable, int Low, int High)> internalNodes, int index)
    {
        var child = edge >> 1;
        if (child == One) return; // terminal sits past the last level
        var childLevel = varLevel[internalNodes[child - 1].Variable];
        if (childLevel <= nodeLevel)
            throw new CircuitSerializationException(
                $"Node {index} has a child at level {childLevel}, not deeper than its own level {nodeLevel}.");
    }

    // Rebuild the manager state from validated serialized data: the variable order, the internal
    // node table (indices 1.. above the terminal), the unique table, the per-variable node lists
    // and the root. The data has already been validated, so this only populates the structures.
    private void RestoreSerialized(int[] levelVar, List<(int Variable, int Low, int High)> internalNodes, int root)
    {
        for (var level = 0; level < levelVar.Length; level++)
        {
            var id = levelVar[level];
            _levelVar[level] = id;
            _varLevel[id] = level;
        }

        foreach (var key in internalNodes)
        {
            var idx = _nodes.Count;
            _nodes.Add(key);
            _uniqueTable[key] = idx;
            _nodesOfVar[key.Variable].Add(idx);
        }

        Root = root;
    }
}
