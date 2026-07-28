namespace LogicalOptimizer;

/// <summary>Small shared helpers for ≤5-input local truth tables packed into a uint.</summary>
internal static class TruthTableOps
{
    private static readonly uint[][] ProjectionCache = new uint[6][];

    /// <summary>All-ones mask for a 2^m-bit truth table (m ≤ 5).</summary>
    public static uint Mask(int m)
    {
        return (uint)((1UL << (1 << m)) - 1);
    }

    /// <summary>
    ///     Projection columns: <c>Projections(m)[i]</c> is the truth table of input i — bit p
    ///     is set exactly when bit i of p is set. Cached per m.
    /// </summary>
    public static uint[] Projections(int m)
    {
        if (ProjectionCache[m] is { } cached) return cached;
        var size = 1 << m;
        var columns = new uint[m];
        for (var i = 0; i < m; i++)
        {
            uint column = 0;
            for (var p = 0; p < size; p++)
                if (((p >> i) & 1) != 0)
                    column |= 1u << p;
            columns[i] = column;
        }

        ProjectionCache[m] = columns;
        return columns;
    }
}

/// <summary>
///     One element of the NPN transformation group: an input permutation, an input-negation
///     mask, and an output negation. Applied to a function it permutes/negates the inputs and
///     optionally negates the output.
/// </summary>
internal readonly struct NpnTransform
{
    public NpnTransform(int[] perm, int negMask, int outNeg)
    {
        Perm = perm;
        NegMask = negMask;
        OutNeg = outNeg;
    }

    /// <summary><c>Perm[i]</c> is the source-input position that destination input i maps to.</summary>
    public int[] Perm { get; }

    /// <summary>Bit i set = negate destination input i.</summary>
    public int NegMask { get; }

    /// <summary>1 = negate the output.</summary>
    public int OutNeg { get; }
}

/// <summary>
///     The canonical NPN form of a truth table together with the transforms relating it to
///     the original. <see cref="Forward" /> maps the original function to
///     <see cref="Canonical" />; <see cref="Inverse" /> maps <see cref="Canonical" /> back to
///     the original (used by the rewrite library to instantiate a canonical structure onto a
///     concrete cut).
/// </summary>
internal readonly struct NpnClass
{
    public NpnClass(uint canonical, int numInputs, NpnTransform forward, NpnTransform inverse)
    {
        Canonical = canonical;
        NumInputs = numInputs;
        Forward = forward;
        Inverse = inverse;
    }

    public uint Canonical { get; }
    public int NumInputs { get; }
    public NpnTransform Forward { get; }
    public NpnTransform Inverse { get; }
}

/// <summary>
///     NPN canonicalization for ≤4-input truth tables. The NPN group for m inputs has
///     2^m input negations · m! permutations · 2 output negations of transforms (768 for
///     m = 4); the canonical form is the numerically smallest truth table over all of them.
///     There are 222 distinct NPN classes over the 2^16 four-input functions — a classic
///     invariant exercised by the test suite.
/// </summary>
internal static class NpnCanonicalizer
{
    private static readonly int[][][] PermCache = new int[6][][];

    /// <summary>All permutations of [0, m).</summary>
    public static int[][] Permutations(int m)
    {
        if (PermCache[m] is { } cached) return cached;
        var result = new List<int[]>();
        var current = new int[m];
        for (var i = 0; i < m; i++) current[i] = i;
        Permute(current, 0, result);
        var array = result.ToArray();
        PermCache[m] = array;
        return array;
    }

    private static void Permute(int[] current, int start, List<int[]> result)
    {
        if (start == current.Length)
        {
            result.Add((int[])current.Clone());
            return;
        }

        for (var i = start; i < current.Length; i++)
        {
            (current[start], current[i]) = (current[i], current[start]);
            Permute(current, start + 1, result);
            (current[start], current[i]) = (current[i], current[start]);
        }
    }

    /// <summary>
    ///     Apply an NPN transform to a truth table over m inputs. The destination function g
    ///     is defined by g(x) = f(y) XOR outNeg where y[perm[i]] = x[i] XOR negBit[i]; that
    ///     is, destination input i drives source input perm[i], possibly negated.
    /// </summary>
    public static uint Apply(uint tt, int m, int[] perm, int negMask, int outNeg)
    {
        var size = 1 << m;
        var mask = TruthTableOps.Mask(m);
        uint result = 0;
        for (var p = 0; p < size; p++)
        {
            var q = 0;
            for (var i = 0; i < m; i++)
            {
                var yi = ((p >> i) & 1) ^ ((negMask >> i) & 1);
                if (yi != 0) q |= 1 << perm[i];
            }

            if ((((tt >> q) & 1) ^ outNeg) != 0) result |= 1u << p;
        }

        return result & mask;
    }

    /// <summary>Invert an NPN transform (perm becomes its inverse; negation follows it).</summary>
    public static NpnTransform Invert(NpnTransform transform, int m)
    {
        var invPerm = new int[m];
        for (var i = 0; i < m; i++) invPerm[transform.Perm[i]] = i;

        var invNeg = 0;
        for (var k = 0; k < m; k++)
            if (((transform.NegMask >> invPerm[k]) & 1) != 0)
                invNeg |= 1 << k;

        return new NpnTransform(invPerm, invNeg, transform.OutNeg);
    }

    /// <summary>
    ///     Canonicalize a truth table over <paramref name="numInputs" /> inputs: return the
    ///     numerically smallest truth table over the whole NPN group, the forward transform
    ///     that produces it, and the inverse that maps it back to the original.
    /// </summary>
    public static NpnClass Canonicalize(uint truthTable, int numInputs)
    {
        var m = numInputs;
        var mask = TruthTableOps.Mask(m);
        var permutations = Permutations(m);
        var negCount = 1 << m;

        var best = uint.MaxValue;
        NpnTransform bestTransform = default;

        foreach (var perm in permutations)
            for (var negMask = 0; negMask < negCount; negMask++)
            {
                var positive = Apply(truthTable, m, perm, negMask, 0);
                if (positive < best)
                {
                    best = positive;
                    bestTransform = new NpnTransform(perm, negMask, 0);
                }

                var negated = positive ^ mask;
                if (negated < best)
                {
                    best = negated;
                    bestTransform = new NpnTransform(perm, negMask, 1);
                }
            }

        return new NpnClass(best, m, bestTransform, Invert(bestTransform, m));
    }
}
