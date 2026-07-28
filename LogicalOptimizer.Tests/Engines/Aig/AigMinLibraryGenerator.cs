using System.Text;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Reproducible offline generator for the provably minimum-AND AIG rewrite table baked into
///     <c>LogicalOptimizer.Core/AigMinLibraryData.cs</c>. For every NPN class of ≤4-input
///     functions it finds a template that uses the minimum possible number of two-input AND
///     nodes, by combining:
///     <list type="bullet">
///         <item>
///             a complete breadth-first enumeration of multi-output AIG sub-structures
///             (<see cref="Frontier" />), deduplicated up to input permutation/negation and edge
///             complementation, which yields the <b>exact</b> minimum AND-count for every class
///             realizable within the built depth (a class's first appearance level is its minimum
///             by construction), plus two cheap record-only passes that extend the proven minimum
///             two levels further without the costly frontier dedup; and
///         </item>
///         <item>
///             SAT-based exact synthesis (<see cref="ExactSynthesizer" />) that produces the actual
///             gate recipe realizing a class at its known minimum AND-count.
///         </item>
///     </list>
///     The BFS supplies certified lower bounds so the SAT layer only ever solves satisfiable
///     instances at the true minimum (no expensive unsatisfiability proofs). The result is
///     certified minimum by the BFS completeness argument; the Exhaustive tests re-run this
///     generator and assert the baked table matches.
/// </summary>
internal static class AigMinLibraryGenerator
{
    /// <summary>Build-depth of the complete frontier per input count. The two record-only
    /// passes then certify minima up to buildDepth + 2, and SAT covers the rest.</summary>
    private static int BuildDepth(int m)
    {
        // Depth 7 for four inputs so the two record-only passes certify minima up to 9 AND nodes,
        // leaving only the min-10 classes uncertified by BFS (they are then pinned as min ≥ 10).
        return m switch { 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => throw new ArgumentOutOfRangeException(nameof(m)) };
    }

    /// <summary>
    ///     Generate, for the given input count, the minimum-AND template of every NPN class,
    ///     keyed by canonical truth table. Each template's <see cref="AigTemplate.Simulate" />
    ///     equals its canonical truth table exactly.
    /// </summary>
    public static SortedDictionary<uint, AigTemplate> Generate(int m)
    {
        var mask = TruthTableOps.Mask(m);
        var classes = new SortedSet<uint>();
        for (uint tt = 0; tt <= mask; tt++) classes.Add(NpnCanonicalizer.Canonicalize(tt, m).Canonical);

        var frontier = new Frontier(m);
        frontier.Build(BuildDepth(m)); // exact min for min <= BuildDepth
        frontier.RecordNextLevel(); //   exact min for min == BuildDepth + 1
        frontier.RecordDepthPlus2(); //   exact min for min == BuildDepth + 2
        var lower = frontier.ClassMin;
        var lbUnfound = BuildDepth(m) + 3;

        var sat = new ExactSynthesizer(m);
        var result = new SortedDictionary<uint, AigTemplate>();
        foreach (var canonical in classes)
        {
            var lb = lower.TryGetValue(canonical, out var known) ? known : lbUnfound;
            // The BFS lower bound means the first r tried is already satisfiable, so we drop both
            // symmetry breaking and the used-gates constraint — empirically the fastest setting for
            // finding a witness of the hardest (min-10) four-input classes.
            var (r, template) = sat.MinSynthesize(canonical, lb, symmetryBreak: false, useUsedConstraint: false);
            if (template.Simulate() != canonical)
                throw new InvalidOperationException($"generated template does not realize 0x{canonical:X}");
            if (lower.TryGetValue(canonical, out var certifiedMin) && r != certifiedMin)
                throw new InvalidOperationException(
                    $"class 0x{canonical:X}: BFS-certified min {certifiedMin} != SAT witness size {r}");
            result[canonical] = template;
        }

        return result;
    }

    /// <summary>
    ///     Certified exact minimum AND-count per NPN class, from the complete BFS plus its two
    ///     record-only extensions — no SAT. Classes not present have a minimum strictly greater
    ///     than the returned <c>certifiedUpTo</c> (for m = 4 exactly one gate more: certifiedUpTo
    ///     is 9 and the four uncertified classes are the maximum, 10). This is the independent
    ///     minimality oracle used by the Exhaustive tests.
    /// </summary>
    public static (Dictionary<uint, int> ClassMin, int CertifiedUpTo, int UnfoundMin) CertifiedMinimums(int m)
    {
        var frontier = new Frontier(m);
        frontier.Build(BuildDepth(m));
        frontier.RecordNextLevel();
        frontier.RecordDepthPlus2();
        return (frontier.ClassMin, BuildDepth(m) + 2, BuildDepth(m) + 3);
    }

    /// <summary>Encode a template as the baked recipe row
    /// { canonicalTt, outputLiteral, gateCount, aLit0, bLit0, ... }.</summary>
    public static int[] Encode(uint canonicalTt, AigTemplate template)
    {
        var row = new int[3 + 2 * template.Gates.Length];
        row[0] = (int)canonicalTt;
        row[1] = template.Output;
        row[2] = template.Gates.Length;
        for (var g = 0; g < template.Gates.Length; g++)
        {
            row[3 + 2 * g] = template.Gates[g].A;
            row[4 + 2 * g] = template.Gates[g].B;
        }

        return row;
    }

    /// <summary>Regenerate the full <c>AigMinLibraryData.cs</c> source text for inputs 1..4.</summary>
    public static string EmitDataSource()
    {
        var sb = new StringBuilder();
        sb.AppendLine("namespace LogicalOptimizer;");
        sb.AppendLine();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//   Provably minimum-AND AIG rewrite recipes for every NPN class of Boolean functions");
        sb.AppendLine("//   over 1..4 inputs (2, 4, 14 and 222 classes). Regenerated by the Exhaustive");
        sb.AppendLine("//   AigMinLibraryTests via AigMinLibraryGenerator (SAT-based exact synthesis with");
        sb.AppendLine("//   complete BFS lower bounds); every recipe uses the minimum possible number of");
        sb.AppendLine("//   two-input AND nodes. Do not edit by hand. Each entry is");
        sb.AppendLine("//   { canonicalTruthTable, outputLiteral, gateCount, aLit0, bLit0, aLit1, bLit1, ... }.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("internal static class AigMinLibraryData");
        sb.AppendLine("{");
        sb.AppendLine("    internal static int[][] For(int numInputs)");
        sb.AppendLine("    {");
        sb.AppendLine("        return numInputs switch");
        sb.AppendLine("        {");
        sb.AppendLine("            1 => M1,");
        sb.AppendLine("            2 => M2,");
        sb.AppendLine("            3 => M3,");
        sb.AppendLine("            4 => M4,");
        sb.AppendLine("            _ => throw new System.ArgumentOutOfRangeException(nameof(numInputs))");
        sb.AppendLine("        };");
        sb.AppendLine("    }");

        for (var m = 1; m <= 4; m++)
        {
            sb.AppendLine();
            sb.AppendLine($"    internal static readonly int[][] M{m} =");
            sb.AppendLine("    {");
            foreach (var (tt, template) in Generate(m))
            {
                var row = Encode(tt, template);
                sb.Append("        new[] { ").Append(string.Join(", ", row)).AppendLine(" },");
            }

            sb.AppendLine("    };");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------
    // Complete BFS over multi-output AIG sub-structures for certified minimum AND-counts.
    // ------------------------------------------------------------------------------------------
    private sealed class Frontier
    {
        private readonly int _m;
        private readonly uint _mask;
        private readonly uint[] _proj;
        private readonly int[][] _transform; // input perm+negation position maps
        private readonly ushort[][] _transformTable; // _transformTable[t][f] = TransformFunc(f, _transform[t])
        private readonly ushort[] _canonClass; // canonical class truth table per function
        public readonly Dictionary<uint, int> ClassMin = new();
        private List<uint[]> _states = new();

        public Frontier(int m)
        {
            _m = m;
            _mask = TruthTableOps.Mask(m);
            _proj = TruthTableOps.Projections(m);
            var size = 1 << m;
            var maps = new List<int[]>();
            foreach (var perm in NpnCanonicalizer.Permutations(m))
                for (var neg = 0; neg < size; neg++)
                {
                    var map = new int[size];
                    for (var p = 0; p < size; p++)
                    {
                        var q = 0;
                        for (var i = 0; i < m; i++)
                        {
                            var yi = ((p >> i) & 1) ^ ((neg >> i) & 1);
                            if (yi != 0) q |= 1 << perm[i];
                        }

                        map[p] = q;
                    }

                    maps.Add(map);
                }

            _transform = maps.ToArray();
            var funcCount = 1 << (1 << m);
            // Precompute every transform of every function once (≈50 MB for m = 4) so the hot
            // permutation-canonical dedup is an O(1) table lookup instead of a per-bit shuffle.
            _transformTable = new ushort[_transform.Length][];
            for (var t = 0; t < _transform.Length; t++)
            {
                var col = new ushort[funcCount];
                var map = _transform[t];
                for (uint f = 0; f < funcCount; f++) col[f] = (ushort)TransformFunc(f, map);
                _transformTable[t] = col;
            }

            _canonClass = new ushort[funcCount];
            for (uint f = 0; f < funcCount; f++)
            {
                var best = uint.MaxValue;
                for (var t = 0; t < _transformTable.Length; t++)
                {
                    uint tf = _transformTable[t][f];
                    if (tf < best) best = tf;
                    var neg = tf ^ _mask;
                    if (neg < best) best = neg;
                }

                _canonClass[f] = (ushort)best;
            }
        }

        private uint TransformFunc(uint f, int[] map)
        {
            var size = 1 << _m;
            uint r = 0;
            for (var p = 0; p < size; p++)
                if (((f >> map[p]) & 1) != 0)
                    r |= 1u << p;
            return r;
        }

        // A state is the array of node truth tables [const, inputs..., gates...].
        private uint[] SeedState()
        {
            var nf = new uint[1 + _m];
            for (var i = 0; i < _m; i++) nf[i + 1] = _proj[i];
            return nf;
        }

        private string PermCanon(ushort[] compCanonSorted)
        {
            ushort[]? best = null;
            var tmp = new ushort[compCanonSorted.Length];
            foreach (var col in _transformTable)
            {
                for (var i = 0; i < compCanonSorted.Length; i++)
                {
                    uint tf = col[compCanonSorted[i]];
                    tmp[i] = (ushort)Math.Min(tf, (~tf) & _mask);
                }

                Array.Sort(tmp);
                if (best == null || Less(tmp, best)) best = (ushort[])tmp.Clone();
            }

            var sb = new StringBuilder(best!.Length);
            foreach (var v in best!) sb.Append((char)v);
            return sb.ToString();
        }

        private static bool Less(ushort[] a, ushort[] b)
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] < b[i]) return true;
                if (a[i] > b[i]) return false;
            }

            return false;
        }

        private static string Key(ushort[] a)
        {
            var sb = new StringBuilder(a.Length);
            foreach (var v in a) sb.Append((char)v);
            return sb.ToString();
        }

        public void Build(int depth)
        {
            ClassMin[_canonClass[0]] = 0;
            for (var i = 0; i < _m; i++) ClassMin.TryAdd(_canonClass[_proj[i]], 0);

            var frontier = new List<uint[]> { SeedState() };
            var perm = new HashSet<string> { "" };
            var cheap = new HashSet<string> { "" };
            for (var level = 0; level < depth; level++)
            {
                var next = new List<uint[]>();
                var cmp = new ushort[level + 1];
                foreach (var nf in frontier)
                {
                    var nn = nf.Length;
                    for (var i = 1; i < nn; i++)
                        for (var j = i + 1; j < nn; j++)
                        {
                            uint fi = nf[i], fj = nf[j];
                            for (var pol = 0; pol < 4; pol++)
                            {
                                var va = (pol & 1) == 1 ? (~fi & _mask) : fi;
                                var vb = (pol & 2) == 2 ? (~fj & _mask) : fj;
                                var h = va & vb & _mask;
                                if (h == 0 || h == _mask) continue;
                                var present = false;
                                for (var k = 0; k < nn; k++)
                                    if (nf[k] == h)
                                    {
                                        present = true;
                                        break;
                                    }

                                if (present) continue;
                                ClassMin.TryAdd(_canonClass[h], level + 1);
                                var gates = nn - 1 - _m;
                                for (var k = 0; k < gates; k++)
                                    cmp[k] = (ushort)Math.Min(nf[1 + _m + k], (~nf[1 + _m + k]) & _mask);
                                cmp[gates] = (ushort)Math.Min(h, (~h) & _mask);
                                Array.Sort(cmp);
                                if (!cheap.Add(Key(cmp))) continue;
                                if (!perm.Add(PermCanon(cmp))) continue;
                                var nns = new uint[nn + 1];
                                Array.Copy(nf, nns, nn);
                                nns[nn] = h;
                                next.Add(nns);
                            }
                        }
                }

                frontier = next;
                if (frontier.Count == 0) break;
            }

            _states = frontier;
        }

        /// <summary>Record exact min = D+1 for classes realized by one extra gate on the depth-D frontier.</summary>
        public void RecordNextLevel()
        {
            var depth = _states.Count > 0 ? _states[0].Length - 1 - _m : 0;
            foreach (var nf in _states)
            {
                var nn = nf.Length;
                for (var i = 1; i < nn; i++)
                    for (var j = i + 1; j < nn; j++)
                    {
                        uint fi = nf[i], fj = nf[j];
                        for (var pol = 0; pol < 4; pol++)
                        {
                            var va = (pol & 1) == 1 ? (~fi & _mask) : fi;
                            var vb = (pol & 2) == 2 ? (~fj & _mask) : fj;
                            var h = va & vb & _mask;
                            if (h == 0 || h == _mask) continue;
                            ClassMin.TryAdd(_canonClass[h], depth + 1);
                        }
                    }
            }
        }

        /// <summary>
        ///     Record exact min = D+2 for classes realized by two extra gates: a new gate on top of
        ///     the depth-D frontier, then a root AND using it and one existing literal. Complete for
        ///     min = D+2 because any (D+2)-gate single-output AIG's operand-join cone is a
        ///     (D+1)-gate structure = (depth-D frontier state) + one gate. Symmetry-safe (records by
        ///     NPN class).
        /// </summary>
        public void RecordDepthPlus2()
        {
            var depth = _states.Count > 0 ? _states[0].Length - 1 - _m : 0;
            foreach (var nf in _states)
            {
                var nn = nf.Length;
                for (var i = 1; i < nn; i++)
                    for (var j = i + 1; j < nn; j++)
                    {
                        uint fi = nf[i], fj = nf[j];
                        for (var pol = 0; pol < 4; pol++)
                        {
                            var va = (pol & 1) == 1 ? (~fi & _mask) : fi;
                            var vb = (pol & 2) == 2 ? (~fj & _mask) : fj;
                            var h7 = va & vb & _mask;
                            if (h7 == 0 || h7 == _mask) continue;
                            var present = false;
                            for (var k = 0; k < nn; k++)
                                if (nf[k] == h7)
                                {
                                    present = true;
                                    break;
                                }

                            if (present) continue;
                            ClassMin.TryAdd(_canonClass[h7], depth + 1);
                            var nh7 = (~h7) & _mask;
                            for (var k = 1; k < nn; k++)
                            {
                                var fk = nf[k];
                                Rec8(h7 & fk & _mask, depth);
                                Rec8(h7 & ((~fk) & _mask), depth);
                                Rec8(nh7 & fk & _mask, depth);
                                Rec8(nh7 & ((~fk) & _mask), depth);
                            }
                        }
                    }
            }
        }

        private void Rec8(uint target, int depth)
        {
            if (target == 0 || target == _mask) return;
            ClassMin.TryAdd(_canonClass[target], depth + 2);
        }
    }

    // ------------------------------------------------------------------------------------------
    // SAT-based exact synthesis of a minimum two-input-AND AIG realizing a single target function.
    // ------------------------------------------------------------------------------------------
    private sealed class ExactSynthesizer
    {
        private readonly int _n;
        private readonly uint _mask;

        public ExactSynthesizer(int n)
        {
            _n = n;
            _mask = TruthTableOps.Mask(n);
        }

        private int InputVal(int nodeIndex, int t) => (t >> (nodeIndex - 1)) & 1;

        /// <summary>Minimum-AND template realizing <paramref name="tt" />, searching r from
        /// <paramref name="lowerBound" /> upward. With an exact lower bound the first solve is
        /// satisfiable, so symmetry breaking can be disabled for speed.</summary>
        public (int R, AigTemplate Template) MinSynthesize(uint tt, int lowerBound = 1,
            bool symmetryBreak = true, bool useUsedConstraint = true)
        {
            for (var i = 1; i <= _n; i++)
            {
                uint col = 0;
                for (var t = 0; t < 1 << _n; t++)
                    if (InputVal(i, t) != 0)
                        col |= 1u << t;
                if (tt == col) return (0, new AigTemplate(_n, Array.Empty<(int, int)>(), i << 1));
                if (tt == ((~col) & _mask)) return (0, new AigTemplate(_n, Array.Empty<(int, int)>(), (i << 1) | 1));
            }

            if (tt == 0) return (0, new AigTemplate(_n, Array.Empty<(int, int)>(), 0));
            if (tt == _mask) return (0, new AigTemplate(_n, Array.Empty<(int, int)>(), 1));

            for (var r = Math.Max(1, lowerBound); r <= 12; r++)
            {
                var template = TrySynthesize(tt, r, symmetryBreak, useUsedConstraint);
                if (template is { } found)
                {
                    if (found.Simulate() != tt)
                        throw new InvalidOperationException($"SAT recipe simulates wrong for 0x{tt:X}");
                    return (r, found);
                }
            }

            throw new InvalidOperationException($"no AIG up to 12 AND nodes realizes 0x{tt:X}");
        }

        private AigTemplate? TrySynthesize(uint tt, int r, bool symmetryBreak, bool useUsedConstraint)
        {
            var t2 = 1 << _n;
            var nv = 0;
            int NewVar() => ++nv;

            var b = new int[r][];
            var lv = new int[r][];
            var rv = new int[r][];
            var selL = new int[r][];
            var selR = new int[r][];
            var polL = new int[r];
            var polR = new int[r];
            for (var g = 0; g < r; g++)
            {
                b[g] = new int[t2];
                lv[g] = new int[t2];
                rv[g] = new int[t2];
                for (var t = 0; t < t2; t++)
                {
                    b[g][t] = NewVar();
                    lv[g][t] = NewVar();
                    rv[g][t] = NewVar();
                }

                var cand = _n + g;
                selL[g] = new int[cand + 1];
                selR[g] = new int[cand + 1];
                for (var c = 1; c <= cand; c++)
                {
                    selL[g][c] = NewVar();
                    selR[g][c] = NewVar();
                }

                polL[g] = NewVar();
                polR[g] = NewVar();
            }

            var opol = NewVar();

            var clauses = new List<int[]>();
            void Add(params int[] c) => clauses.Add(c);

            for (var g = 0; g < r; g++)
            {
                var cand = _n + g;
                var atLeastL = new List<int>();
                var atLeastR = new List<int>();
                for (var c = 1; c <= cand; c++)
                {
                    atLeastL.Add(selL[g][c]);
                    atLeastR.Add(selR[g][c]);
                }

                Add(atLeastL.ToArray());
                Add(atLeastR.ToArray());
                for (var a = 1; a <= cand; a++)
                    for (var bb = a + 1; bb <= cand; bb++)
                    {
                        Add(-selL[g][a], -selL[g][bb]);
                        Add(-selR[g][a], -selR[g][bb]);
                    }

                for (var a = 1; a <= cand; a++)
                    for (var bb = 1; bb <= cand; bb++)
                        if (a >= bb)
                            Add(-selL[g][a], -selR[g][bb]);

                for (var t = 0; t < t2; t++)
                {
                    int bv = b[g][t], l = lv[g][t], rr = rv[g][t];
                    Add(-bv, l);
                    Add(-bv, rr);
                    Add(bv, -l, -rr);
                    for (var c = 1; c <= cand; c++)
                    {
                        int sL = selL[g][c], sR = selR[g][c], pL = polL[g], pR = polR[g];
                        if (c <= _n)
                        {
                            var cval = InputVal(c, t);
                            if (cval == 0)
                            {
                                Add(-sL, -l, pL);
                                Add(-sL, l, -pL);
                                Add(-sR, -rr, pR);
                                Add(-sR, rr, -pR);
                            }
                            else
                            {
                                Add(-sL, -l, -pL);
                                Add(-sL, l, pL);
                                Add(-sR, -rr, -pR);
                                Add(-sR, rr, pR);
                            }
                        }
                        else
                        {
                            var bc = b[c - (_n + 1)][t];
                            Add(-sL, -l, bc, pL);
                            Add(-sL, l, -bc, pL);
                            Add(-sL, l, bc, -pL);
                            Add(-sL, -l, -bc, -pL);
                            Add(-sR, -rr, bc, pR);
                            Add(-sR, rr, -bc, pR);
                            Add(-sR, rr, bc, -pR);
                            Add(-sR, -rr, -bc, -pR);
                        }
                    }
                }
            }

            var last = r - 1;
            for (var t = 0; t < t2; t++)
            {
                var m = (int)((tt >> t) & 1);
                var bv = b[last][t];
                if (m == 1)
                {
                    Add(bv, opol);
                    Add(-bv, -opol);
                }
                else
                {
                    Add(-bv, opol);
                    Add(bv, -opol);
                }
            }

            for (var g = 0; useUsedConstraint && g < r - 1; g++)
            {
                var nodeIdx = _n + 1 + g;
                var lits = new List<int>();
                for (var k = g + 1; k < r; k++)
                {
                    var cand = _n + k;
                    if (nodeIdx <= cand)
                    {
                        lits.Add(selL[k][nodeIdx]);
                        lits.Add(selR[k][nodeIdx]);
                    }
                }

                if (lits.Count > 0) Add(lits.ToArray());
            }

            for (var g = 0; symmetryBreak && g + 1 < r; g++)
            {
                var candG = _n + g;
                var candH = _n + g + 1;
                for (var a = 1; a <= candG; a++)
                    for (var bb = 1; bb <= candH && bb < a; bb++)
                        Add(-selR[g][a], -selR[g + 1][bb]);
                for (var a = 1; a <= candG && a <= candH; a++)
                    for (var la = 1; la < a; la++)
                        for (var lb = 1; lb < la; lb++)
                            Add(-selR[g][a], -selR[g + 1][a], -selL[g][la], -selL[g + 1][lb]);
            }

            var solver = new SatSolver(nv);
            foreach (var c in clauses) solver.AddClause(c);
            var res = solver.Solve(100_000_000);
            if (res == SatResult.Unknown)
                throw new InvalidOperationException("SAT budget exhausted during exact synthesis");
            if (res != SatResult.Satisfiable) return null;

            var gates = new (int A, int B)[r];
            for (var g = 0; g < r; g++)
            {
                var cand = _n + g;
                int cl = 0, cr = 0;
                for (var c = 1; c <= cand; c++)
                {
                    if (solver.GetValue(selL[g][c])) cl = c;
                    if (solver.GetValue(selR[g][c])) cr = c;
                }

                var pl = solver.GetValue(polL[g]) ? 1 : 0;
                var pr = solver.GetValue(polR[g]) ? 1 : 0;
                gates[g] = ((cl << 1) | pl, (cr << 1) | pr);
            }

            var op = solver.GetValue(opol) ? 1 : 0;
            return new AigTemplate(_n, gates, ((_n + r) << 1) | op);
        }
    }
}
