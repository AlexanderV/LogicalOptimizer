using System.Buffers.Binary;
using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Which engine a serialized circuit blob was written by. Stored as a single self-describing
///     byte in the header so a d-DNNF blob can never be silently loaded as a BDD (or vice versa).
/// </summary>
internal enum CircuitEngine : byte
{
    Bdd = 1,
    Dnnf = 2
}

/// <summary>
///     <b>Experimental (until v4).</b> Shared, hand-written binary layout used by both engines'
///     <c>Save</c>/<c>Load</c>. There is NO reflection or object deserialization: the reader and
///     writer move primitive fields only, every multi-byte integer is little-endian (via
///     <see cref="BinaryPrimitives" />) and the whole blob is framed by a fixed header and a
///     trailing CRC-32.
///     <para>
///         Layout: magic <c>"LOCX"</c> (4 bytes) · format version (1 byte) · engine (1 byte) ·
///         engine-specific body · CRC-32 over every preceding byte (4 bytes, little-endian).
///         The reader validates magic, refuses a newer (forward) version, refuses the wrong
///         engine, and verifies the checksum — but the checksum only catches corruption, so the
///         engines additionally validate the node table's structure (indices in range, children
///         strictly before their parent i.e. acyclic/topological, a valid root and terminals).
///     </para>
/// </summary>
internal static class CircuitBinaryFormat
{
    /// <summary>Magic marker: ASCII "LOCX" (LogicalOptimizer Circuit, eXperimental).</summary>
    internal static readonly byte[] Magic = "LOCX"u8.ToArray();

    /// <summary>
    ///     Current on-disk format version. Bump ONLY on a wire-incompatible change; a reader
    ///     refuses any version greater than the one it was built with (forward rejection).
    /// </summary>
    internal const byte FormatVersion = 1;

    /// <summary>Hard cap on a single variable name's UTF-8 length, so a hostile header cannot force a huge allocation.</summary>
    internal const int MaxVariableNameBytes = 65536;
}

/// <summary>
///     Accumulates a circuit blob in memory, then frames it with a trailing CRC-32 and copies it
///     to the destination in one shot. Buffering keeps <c>Save</c> a pure function of the circuit
///     (deterministic bytes) and lets the checksum cover the whole body.
/// </summary>
internal sealed class CircuitBinaryWriter
{
    private readonly MemoryStream _buffer = new();

    internal CircuitBinaryWriter(CircuitEngine engine)
    {
        _buffer.Write(CircuitBinaryFormat.Magic, 0, CircuitBinaryFormat.Magic.Length);
        _buffer.WriteByte(CircuitBinaryFormat.FormatVersion);
        _buffer.WriteByte((byte)engine);
    }

    internal void WriteByte(byte value)
    {
        _buffer.WriteByte(value);
    }

    internal void WriteInt32(int value)
    {
        Span<byte> scratch = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(scratch, value);
        _buffer.Write(scratch);
    }

    internal void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(bytes.Length);
        _buffer.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Append the CRC-32 of everything written so far and copy the framed blob to <paramref name="destination" />.</summary>
    internal void Finish(Stream destination)
    {
        var body = _buffer.GetBuffer();
        var length = (int)_buffer.Length;
        var checksum = Crc32.Compute(body.AsSpan(0, length));

        destination.Write(body, 0, length);
        Span<byte> scratch = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, checksum);
        destination.Write(scratch);
    }
}

/// <summary>
///     Sequential, budget-aware reader over the source stream. It never trusts a length field to
///     pre-size an allocation — counts are range-checked against the caller's budget first and
///     every element is read one at a time straight from the stream, so a hostile header claiming
///     a huge node count aborts against the real bytes (a truncated stream) or the budget rather
///     than materialising anything. A running CRC-32 is accumulated over every consumed byte and
///     checked in <see cref="Finish" />.
/// </summary>
internal sealed class CircuitBinaryReader
{
    private readonly Stream _source;
    private uint _crc = Crc32.Seed;

    /// <summary>Reads and validates the header, refusing a bad magic, a forward version, or the wrong engine.</summary>
    internal CircuitBinaryReader(Stream source, CircuitEngine expected)
    {
        _source = source;

        Span<byte> magic = stackalloc byte[CircuitBinaryFormat.Magic.Length];
        FillExact(magic);
        if (!magic.SequenceEqual(CircuitBinaryFormat.Magic))
            throw new CircuitSerializationException(
                "Not a LogicalOptimizer circuit blob: the magic marker does not match.");

        var version = ReadByte();
        if (version > CircuitBinaryFormat.FormatVersion)
            throw new CircuitSerializationException(
                $"Circuit blob format version {version} is newer than this build understands " +
                $"(maximum supported version {CircuitBinaryFormat.FormatVersion}); refusing to guess at it.");
        if (version != CircuitBinaryFormat.FormatVersion)
            throw new CircuitSerializationException(
                $"Unsupported circuit blob format version {version}.");

        var engine = ReadByte();
        if (engine != (byte)expected)
            throw new CircuitSerializationException(
                $"Wrong engine: this blob was written by engine {engine} but is being loaded as " +
                $"{(byte)expected} ({expected}). A d-DNNF blob cannot be loaded as a BDD, or vice versa.");
    }

    internal byte ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        FillExact(one);
        return one[0];
    }

    internal int ReadInt32()
    {
        Span<byte> scratch = stackalloc byte[sizeof(int)];
        FillExact(scratch);
        return BinaryPrimitives.ReadInt32LittleEndian(scratch);
    }

    /// <summary>
    ///     Read a non-negative count and range-check it against <paramref name="budget" /> BEFORE any
    ///     allocation keyed off it. A negative or over-budget count is refused up front — the header
    ///     is never allowed to drive an unbounded allocation.
    /// </summary>
    internal int ReadCount(long budget, string what)
    {
        var value = ReadInt32();
        if (value < 0)
            throw new CircuitSerializationException($"Negative {what} ({value}) in circuit blob.");
        if (value > budget)
            throw new NodeBudgetExceededException(
                $"Circuit blob declares {value} {what}, exceeding the load budget of {budget}.");
        return value;
    }

    /// <summary>Read a length-prefixed UTF-8 string, refusing a length past <see cref="CircuitBinaryFormat.MaxVariableNameBytes" />.</summary>
    internal string ReadString()
    {
        var length = ReadInt32();
        if (length < 0 || length > CircuitBinaryFormat.MaxVariableNameBytes)
            throw new CircuitSerializationException(
                $"Invalid string length {length} in circuit blob (limit {CircuitBinaryFormat.MaxVariableNameBytes}).");
        if (length == 0) return string.Empty;
        var bytes = new byte[length];
        FillExact(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Verify the trailing CRC-32 against the running checksum and confirm the stream ends there.</summary>
    internal void Finish()
    {
        // The checksum itself is NOT folded into the running CRC.
        var running = Crc32.Finalize(_crc);

        Span<byte> scratch = stackalloc byte[sizeof(uint)];
        var read = 0;
        while (read < scratch.Length)
        {
            var n = _source.Read(scratch.Slice(read));
            if (n <= 0)
                throw new CircuitSerializationException("Truncated circuit blob: missing trailing checksum.");
            read += n;
        }

        var stored = BinaryPrimitives.ReadUInt32LittleEndian(scratch);
        if (stored != running)
            throw new CircuitSerializationException(
                $"Circuit blob checksum mismatch (stored 0x{stored:X8}, computed 0x{running:X8}); the data is corrupt.");

        if (_source.ReadByte() != -1)
            throw new CircuitSerializationException("Trailing bytes after the circuit blob checksum.");
    }

    private void FillExact(Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var n = _source.Read(destination.Slice(read));
            if (n <= 0)
                throw new CircuitSerializationException("Truncated circuit blob: the stream ended early.");
            read += n;
        }

        _crc = Crc32.Append(_crc, destination);
    }
}

/// <summary>
///     Minimal CRC-32 (IEEE 802.3, reflected) over a byte span. A checksum catches accidental
///     corruption/truncation of a circuit blob; it is NOT a substitute for the structural
///     validation the engines perform on load.
/// </summary>
internal static class Crc32
{
    internal const uint Seed = 0xFFFFFFFFu;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256u; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    internal static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    internal static uint Finalize(uint crc)
    {
        return crc ^ 0xFFFFFFFFu;
    }

    internal static uint Compute(ReadOnlySpan<byte> data)
    {
        return Finalize(Append(Seed, data));
    }
}
