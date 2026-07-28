using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     NPN canonicalization: functions in the same NPN class share a canonical representative,
///     different classes differ, and the returned inverse transform maps the canonical form
///     back to the original exactly. The 222 four-input NPN classes are a classic invariant
///     (pushed to the Exhaustive category because it sweeps all 2^16 functions).
/// </summary>
public class NpnCanonicalizationTests
{
    // Two-input truth tables (bit p = value at input pattern p).
    private const uint And2 = 0b1000; // p=3 only
    private const uint Or2 = 0b1110; // p in {1,2,3}
    private const uint Xor2 = 0b0110; // p in {1,2}
    private const uint Xnor2 = 0b1001; // p in {0,3}

    [Fact]
    public void And2_And_Or2_ShareNpnClass_ButXor2_Differs()
    {
        var and = NpnCanonicalizer.Canonicalize(And2, 2).Canonical;
        var or = NpnCanonicalizer.Canonicalize(Or2, 2).Canonical;
        var xor = NpnCanonicalizer.Canonicalize(Xor2, 2).Canonical;
        var xnor = NpnCanonicalizer.Canonicalize(Xnor2, 2).Canonical;

        Assert.Equal(and, or); // AND2 and OR2 are NPN-equivalent
        Assert.Equal(xor, xnor); // XOR2 and XNOR2 are NPN-equivalent (output negation)
        Assert.NotEqual(and, xor); // XOR2 is its own class
    }

    [Fact]
    public void MajorityAndMux_AreDistinctThreeInputClasses()
    {
        // maj(x0,x1,x2) and ite(x2,x1,x0) — well-known inequivalent 3-input functions.
        uint maj = 0, mux = 0;
        for (var p = 0; p < 8; p++)
        {
            var x0 = p & 1;
            var x1 = (p >> 1) & 1;
            var x2 = (p >> 2) & 1;
            if (x0 + x1 + x2 >= 2) maj |= 1u << p;
            if ((x2 == 1 ? x1 : x0) == 1) mux |= 1u << p;
        }

        Assert.NotEqual(NpnCanonicalizer.Canonicalize(maj, 3).Canonical,
            NpnCanonicalizer.Canonicalize(mux, 3).Canonical);
    }

    [Fact]
    public void TransformedFunction_CanonicalizesToSameRepresentative()
    {
        var rng = new Random(5150);
        for (var trial = 0; trial < 400; trial++)
        {
            var m = rng.Next(1, 5);
            var mask = TruthTableOps.Mask(m);
            var tt = (uint)rng.Next() & mask;

            var perms = NpnCanonicalizer.Permutations(m);
            var perm = perms[rng.Next(perms.Length)];
            var negMask = rng.Next(1 << m);
            var outNeg = rng.Next(2);
            var transformed = NpnCanonicalizer.Apply(tt, m, perm, negMask, outNeg);

            Assert.Equal(NpnCanonicalizer.Canonicalize(tt, m).Canonical,
                NpnCanonicalizer.Canonicalize(transformed, m).Canonical);
        }
    }

    [Fact]
    public void ForwardTransform_ProducesCanonical_AndInverseRoundTrips()
    {
        var rng = new Random(2718);
        for (var trial = 0; trial < 2000; trial++)
        {
            var m = rng.Next(1, 5);
            var mask = TruthTableOps.Mask(m);
            var tt = (uint)rng.Next() & mask;

            var npn = NpnCanonicalizer.Canonicalize(tt, m);

            // Forward maps original -> canonical.
            var forward = NpnCanonicalizer.Apply(tt, m, npn.Forward.Perm, npn.Forward.NegMask,
                npn.Forward.OutNeg);
            Assert.Equal(npn.Canonical, forward);

            // Inverse maps canonical -> original (the round trip).
            var back = NpnCanonicalizer.Apply(npn.Canonical, m, npn.Inverse.Perm,
                npn.Inverse.NegMask, npn.Inverse.OutNeg);
            Assert.Equal(tt, back);
        }
    }

    [Fact]
    public void SampledFourInputFunctions_YieldAtMost222Classes()
    {
        // Fast gate: a large sample can only ever surface classes that exist, so the distinct
        // count must not exceed the true 222. (The exact 222 count lives in the Exhaustive test.)
        var rng = new Random(88);
        var classes = new HashSet<uint>();
        for (var i = 0; i < 20000; i++)
            classes.Add(NpnCanonicalizer.Canonicalize((uint)rng.Next() & 0xFFFF, 4).Canonical);

        Assert.True(classes.Count <= 222, $"expected ≤222 NPN classes, saw {classes.Count}");
    }

    [Fact]
    [Trait("Category", "Exhaustive")]
    public void AllFourInputFunctions_FormExactly222NpnClasses()
    {
        var classes = new HashSet<uint>();
        for (uint tt = 0; tt <= 0xFFFF; tt++)
            classes.Add(NpnCanonicalizer.Canonicalize(tt, 4).Canonical);

        Assert.Equal(222, classes.Count);
    }
}
