namespace LogicalOptimizer;

public sealed partial class DnnfCircuit
{
    // Output node indices 0 and 1 are reserved for the False / True terminals; every other node
    // is emitted in children-before-parent (post-order) order, so a child's index is always
    // strictly smaller than its parent's. That makes the byte output a deterministic function of
    // the circuit and lets the loader verify acyclicity by a single index comparison.
    private const int OutFalse = 0;
    private const int OutTrue = 1;

    /// <summary>
    ///     <b>Experimental (until v4).</b> Serialize this d-DNNF circuit to a compact, self-describing
    ///     binary blob (little-endian, CRC-32 checked). The output is deterministic — the same circuit
    ///     always produces identical bytes — and can be read back with <see cref="Load" />; a
    ///     round-trip preserves the variables, model count, weighted counts and evaluation exactly.
    ///     <para>
    ///         The format is EXPERIMENTAL: it may change before v4 and carries no cross-version
    ///         compatibility guarantee other than the version gate, which makes a future build refuse
    ///         (rather than misread) a blob it does not understand. The engine byte makes a d-DNNF blob
    ///         a typed error if it is loaded as a BDD. No reflection or object deserialization is used.
    ///     </para>
    /// </summary>
    /// <param name="destination">The stream the blob is written to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination" /> is null.</exception>
    public void Save(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Canonical renumbering: terminals fixed at 0/1, all other reachable nodes in post-order.
        var map = new int[_nodes.Length];
        Array.Fill(map, -1);
        var ordered = new List<int>(); // old ids of non-terminal nodes, in emission order
        var next = 2;

        var state = new byte[_nodes.Length]; // 0 unseen, 1 in progress, 2 done
        var stack = new Stack<int>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var id = stack.Peek();
            var node = _nodes[id];
            if (node.Kind == DnnfKind.False)
            {
                map[id] = OutFalse;
                state[id] = 2;
                stack.Pop();
                continue;
            }

            if (node.Kind == DnnfKind.True)
            {
                map[id] = OutTrue;
                state[id] = 2;
                stack.Pop();
                continue;
            }

            if (state[id] == 0)
            {
                state[id] = 1;
                // Push children in reverse so they finish left-to-right (deterministic emission).
                for (var i = node.Children.Length - 1; i >= 0; i--)
                    if (state[node.Children[i]] != 2)
                        stack.Push(node.Children[i]);
            }
            else if (state[id] == 1)
            {
                state[id] = 2;
                map[id] = next++;
                ordered.Add(id);
                stack.Pop();
            }
            else
            {
                stack.Pop();
            }
        }

        var nodeCount = 2 + ordered.Count;

        var writer = new CircuitBinaryWriter(CircuitEngine.Dnnf);
        writer.WriteInt32(_inputVariables.Count);
        foreach (var name in _inputVariables)
            writer.WriteString(name);

        writer.WriteInt32(nodeCount);
        // Node 0: False terminal, node 1: True terminal.
        WriteNode(writer, DnnfKind.False, 0, Array.Empty<int>());
        WriteNode(writer, DnnfKind.True, 0, Array.Empty<int>());
        foreach (var oldId in ordered)
        {
            var node = _nodes[oldId];
            var children = new int[node.Children.Length];
            for (var i = 0; i < children.Length; i++) children[i] = map[node.Children[i]];
            WriteNode(writer, node.Kind, node.Value, children);
        }

        writer.WriteInt32(map[_root]);
        writer.WriteInt32(OutFalse); // falseId is always the reserved False terminal
        writer.Finish(destination);
    }

    private static void WriteNode(CircuitBinaryWriter writer, DnnfKind kind, int value, int[] children)
    {
        writer.WriteByte((byte)kind);
        writer.WriteInt32(value);
        writer.WriteInt32(children.Length);
        foreach (var child in children) writer.WriteInt32(child);
    }

    /// <summary>
    ///     <b>Experimental (until v4).</b> Read a d-DNNF circuit back from a blob produced by
    ///     <see cref="Save" />. The load is fully validated — magic, format version (a newer version
    ///     is refused, not misread), engine byte (a BDD blob is a typed error here), CRC-32 checksum,
    ///     and the node table's structure (indices in range, children strictly before their parent so
    ///     the graph is acyclic, a valid root and terminals). The checksum only catches corruption; it
    ///     does not replace the structural checks.
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
    public static DnnfCircuit Load(Stream source, ResourceBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        budget ??= ResourceBudget.Default;
        long nodeBudget = budget.BddNodeLimit;

        var reader = new CircuitBinaryReader(source, CircuitEngine.Dnnf);

        var variableCount = reader.ReadCount(nodeBudget, "variables");
        var variables = new List<string>();
        for (var i = 0; i < variableCount; i++)
        {
            if ((i & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            variables.Add(reader.ReadString());
        }

        var nodeCount = reader.ReadCount(nodeBudget, "nodes");
        if (nodeCount < 2)
            throw new CircuitSerializationException(
                $"A d-DNNF blob must contain at least the two terminal nodes, but declares {nodeCount}.");

        var nodes = new List<DnnfNode>();
        for (var index = 0; index < nodeCount; index++)
        {
            if ((index & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var kindByte = reader.ReadByte();
            if (kindByte > (byte)DnnfKind.Or)
                throw new CircuitSerializationException($"Unknown d-DNNF node kind {kindByte} at index {index}.");
            var kind = (DnnfKind)kindByte;
            var value = reader.ReadInt32();

            var childCount = reader.ReadInt32();
            if (childCount < 0 || childCount > index)
                throw new CircuitSerializationException(
                    $"Node {index} declares {childCount} children (must be in [0, {index}] for an acyclic graph).");
            var children = new int[childCount];
            for (var c = 0; c < childCount; c++)
            {
                var child = reader.ReadInt32();
                if (child < 0 || child >= index)
                    throw new CircuitSerializationException(
                        $"Node {index} references child {child}, which is not a strictly earlier node.");
                children[c] = child;
            }

            ValidateNodeShape(index, kind, value, children);
            nodes.Add(new DnnfNode(kind, value, children));
        }

        var root = reader.ReadInt32();
        if (root < 0 || root >= nodeCount)
            throw new CircuitSerializationException($"Root index {root} is out of range [0, {nodeCount}).");
        var falseId = reader.ReadInt32();
        if (falseId < 0 || falseId >= nodeCount || nodes[falseId].Kind != DnnfKind.False)
            throw new CircuitSerializationException($"False-terminal index {falseId} does not point at a False node.");

        reader.Finish();

        if (nodes[OutFalse].Kind != DnnfKind.False || nodes[OutTrue].Kind != DnnfKind.True)
            throw new CircuitSerializationException(
                "A d-DNNF blob must reserve node 0 for the False terminal and node 1 for the True terminal.");

        return new DnnfCircuit(nodes.ToArray(), root, variables, variables.Count, falseId);
    }

    private static void ValidateNodeShape(int index, DnnfKind kind, int value, int[] children)
    {
        switch (kind)
        {
            case DnnfKind.False:
            case DnnfKind.True:
                if (children.Length != 0)
                    throw new CircuitSerializationException($"Terminal node {index} must have no children.");
                break;
            case DnnfKind.Literal:
                if (children.Length != 0)
                    throw new CircuitSerializationException($"Literal node {index} must have no children.");
                if (value == 0)
                    throw new CircuitSerializationException($"Literal node {index} has an invalid literal 0.");
                break;
            case DnnfKind.Or:
                if (children.Length != 2)
                    throw new CircuitSerializationException(
                        $"Decision (Or) node {index} must have exactly two children [low, high].");
                if (value < 1)
                    throw new CircuitSerializationException(
                        $"Decision (Or) node {index} has an invalid decision variable {value}.");
                break;
            case DnnfKind.And:
                // A decomposable conjunction may carry any number of conjuncts (including zero).
                break;
            default:
                throw new CircuitSerializationException($"Unknown d-DNNF node kind at index {index}.");
        }
    }
}
