namespace LogicalOptimizer;

/// <summary>
///     <b>Experimental (until v4).</b> Raised when a compiled-circuit binary blob (the
///     <c>Save</c>/<c>Load</c> format shared by <c>BinaryDecisionDiagram</c> and
///     <c>DnnfCircuit</c>) is malformed: a bad magic marker, an unrecognised — in particular a
///     newer, forward — format version, the wrong engine byte, a checksum mismatch, a truncated
///     stream, or a structurally invalid node table (an out-of-range index, a non-topological
///     reference, an invalid root or terminal). A checksum only catches corruption; the loader
///     still validates structure and reports every violation as this typed exception rather than
///     misreading the input. A resource limit hit while loading is reported separately as
///     <see cref="NodeBudgetExceededException" />, never as this type.
///     <para>
///         The binary format is experimental and carries no cross-version compatibility guarantee
///         before v4 other than the version gate, which refuses a blob written by a newer build.
///     </para>
/// </summary>
public sealed class CircuitSerializationException : Exception
{
    public CircuitSerializationException(string message) : base(message)
    {
    }

    public CircuitSerializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
