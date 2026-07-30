using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Espresso-style heuristic two-level minimizer working purely on cube lists — no
///     2^n truth table anywhere. The classic loop EXPAND → IRREDUNDANT → REDUCE runs
///     until a pass stops improving; every step is validated by an EXACT cofactor
///     tautology check (unate reduction + binate-variable splitting), so the result is
///     sound by construction: only transformations proven cover-preserving are applied.
///     A step budget turns pathological instances into "return the best so far", never
///     into an unsound answer. This is the scale story past the SAT-based minimizer:
///     it only ever touches the cubes it was given.
/// </summary>
internal static class EspressoLiteMinimizer
{
    internal const int DefaultTautologyStepLimit = 500_000;

    /// <summary>A product term: bit v of Pos/Neg set = positive/negative literal of variable v.</summary>
    internal sealed class Cube
    {
        public readonly ulong[] Pos;
        public readonly ulong[] Neg;

        public Cube(int words)
        {
            Pos = new ulong[words];
            Neg = new ulong[words];
        }

        public Cube Clone()
        {
            var copy = new Cube(Pos.Length);
            Pos.CopyTo(copy.Pos, 0);
            Neg.CopyTo(copy.Neg, 0);
            return copy;
        }

        /// <summary>
        ///     Overwrite this cube with <paramref name="source" />. EXPAND and REDUCE need a
        ///     snapshot of a cube per trial literal, which <see cref="Clone" /> used to supply at
        ///     three allocations a time (the object plus both word arrays) - thousands of them per
        ///     pass. Refreshing one reusable scratch cube instead keeps the trials allocation-free
        ///     and produces the same bits.
        /// </summary>
        public void CopyFrom(Cube source)
        {
            source.Pos.CopyTo(Pos, 0);
            source.Neg.CopyTo(Neg, 0);
        }

        public int LiteralCount()
        {
            var count = 0;
            foreach (var w in Pos) count += BitOperations.PopCount(w);
            foreach (var w in Neg) count += BitOperations.PopCount(w);
            return count;
        }

        public bool HasPos(int v)
        {
            return (Pos[v >> 6] & (1UL << (v & 63))) != 0;
        }

        public bool HasNeg(int v)
        {
            return (Neg[v >> 6] & (1UL << (v & 63))) != 0;
        }

        public void SetPos(int v)
        {
            Pos[v >> 6] |= 1UL << (v & 63);
        }

        public void SetNeg(int v)
        {
            Neg[v >> 6] |= 1UL << (v & 63);
        }

        public void Clear(int v)
        {
            Pos[v >> 6] &= ~(1UL << (v & 63));
            Neg[v >> 6] &= ~(1UL << (v & 63));
        }

        /// <summary>this ⊇ other as a set of minterms: every literal of this appears in other.</summary>
        public bool Contains(Cube other)
        {
            for (var w = 0; w < Pos.Length; w++)
                if ((Pos[w] & ~other.Pos[w]) != 0 || (Neg[w] & ~other.Neg[w]) != 0)
                    return false;
            return true;
        }
    }

    /// <summary>
    ///     Minimize a cover in place-ish; returns the improved cube list. Sound for any
    ///     budget: exhaustion only stops further improvement.
    /// </summary>
    internal static List<Cube> Minimize(List<Cube> cover, int variableCount,
        int tautologyStepLimit = DefaultTautologyStepLimit, CancellationToken cancellationToken = default)
    {
        var budget = tautologyStepLimit;
        var current = cover.Select(c => c.Clone()).ToList();

        // One scratch for the whole run: the coverage tests below are issued in the thousands and
        // each used to allocate its own working memory.
        var scratch = new Scratch();

        var bestCost = Cost(current);
        for (var pass = 0; pass < 6; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Expand(current, variableCount, ref budget, scratch, cancellationToken);
            Irredundant(current, variableCount, ref budget, scratch, cancellationToken);
            if (pass > 0)
            {
                var cost = Cost(current);
                if (cost.CompareTo(bestCost) >= 0) break;
                bestCost = cost;
            }
            else
            {
                bestCost = Cost(current);
            }

            Reduce(current, variableCount, ref budget, scratch, cancellationToken);
            if (budget <= 0) break;
        }

        // REDUCE may have left shrunk cubes behind; one final grow-and-prune
        Expand(current, variableCount, ref budget, scratch, cancellationToken);
        Irredundant(current, variableCount, ref budget, scratch, cancellationToken);
        return current;
    }

    private static (int Literals, int Cubes) Cost(List<Cube> cover)
    {
        return (cover.Sum(c => c.LiteralCount()), cover.Count);
    }

    /// <summary>
    ///     EXPAND: drop literals while the cover still covers the grown cube. The
    ///     coverage reference is the OTHER cubes plus this cube as it was BEFORE the
    ///     drop — checking against a cover containing the expanded cube itself would be
    ///     trivially true and erase every literal.
    /// </summary>
    private static void Expand(List<Cube> cover, int variableCount, ref int budget,
        Scratch scratch, CancellationToken cancellationToken)
    {
        if (cover.Count == 0) return;

        // The reference cover and the "before" snapshot used to be rebuilt for EVERY trial
        // literal: a fresh List<Cube> over the whole cover plus a three-allocation Clone, tens of
        // thousands of times per pass. Neither actually varies per literal - the other cubes are
        // fixed while this cube is being expanded, and the snapshot only needs its bits refreshed.
        // Both are hoisted to reusable buffers here; the values handed to Covers are identical.
        // OrderByDescending is a STABLE sort and EXPAND is order-dependent, so cubes with equal
        // literal counts must keep their relative order - List.Sort (introsort) would not. This
        // runs once per pass, not per trial, so it is not where the allocations were.
        var order = cover.OrderByDescending(c => c.LiteralCount()).ToList();

        var reference = new List<Cube>(cover.Count);
        var before = new Cube(cover[0].Pos.Length);

        foreach (var cube in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Every other cube, plus one trailing slot holding the snapshot.
            reference.Clear();
            foreach (var other in cover)
                if (!ReferenceEquals(other, cube))
                    reference.Add(other);
            reference.Add(before);

            for (var v = 0; v < variableCount && budget > 0; v++)
            {
                if (!cube.HasPos(v) && !cube.HasNeg(v)) continue;
                var wasPos = cube.HasPos(v);
                before.CopyFrom(cube);
                cube.Clear(v);
                if (Covers(reference, cube, variableCount, scratch, ref budget) != true)
                {
                    if (wasPos) cube.SetPos(v);
                    else cube.SetNeg(v);
                }
            }
        }

        // Single-cube containment absorbs shrunk-away cubes for free
        for (var i = cover.Count - 1; i >= 0; i--)
            for (var j = 0; j < cover.Count; j++)
                if (i != j && cover[j].Contains(cover[i]))
                {
                    cover.RemoveAt(i);
                    break;
                }
    }

    /// <summary>IRREDUNDANT: drop any cube the rest of the cover already covers.</summary>
    private static void Irredundant(List<Cube> cover, int variableCount, ref int budget,
        Scratch scratch, CancellationToken cancellationToken)
    {
        for (var i = cover.Count - 1; i >= 0 && budget > 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = cover[i];
            cover.RemoveAt(i);
            if (Covers(cover, candidate, variableCount, scratch, ref budget) != true)
                cover.Insert(i, candidate);
        }
    }

    /// <summary>
    ///     REDUCE: shrink each cube by re-adding literals whose lost half is already
    ///     covered elsewhere — freeing the next EXPAND to grow in a different direction.
    /// </summary>
    private static void Reduce(List<Cube> cover, int variableCount, ref int budget,
        Scratch scratch, CancellationToken cancellationToken)
    {
        if (cover.Count == 0) return;

        // `rest` and the trial cube are reusable: the former only changes with i, the latter is
        // overwritten on every attempt anyway. Cloning it twice per variable per cube was the bulk
        // of REDUCE's allocation.
        var rest = new List<Cube>(cover.Count);
        var lost = new Cube(cover[0].Pos.Length);

        for (var i = 0; i < cover.Count && budget > 0; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cube = cover[i];

            rest.Clear();
            for (var k = 0; k < cover.Count; k++)
                if (k != i)
                    rest.Add(cover[k]);

            for (var v = 0; v < variableCount && budget > 0; v++)
            {
                if (cube.HasPos(v) || cube.HasNeg(v)) continue;

                // Adding literal v keeps the cover iff rest covers (cube & !v)
                lost.CopyFrom(cube);
                lost.SetNeg(v);
                if (Covers(rest, lost, variableCount, scratch, ref budget) == true)
                {
                    cube.SetPos(v);
                    continue;
                }

                lost.CopyFrom(cube);
                lost.SetPos(v);
                if (Covers(rest, lost, variableCount, scratch, ref budget) == true)
                    cube.SetNeg(v);
            }
        }
    }

    /// <summary>F ⊇ c ⟺ the cofactor of F by c is a tautology. Null = budget ran out.</summary>
    internal static bool? Covers(IReadOnlyList<Cube> cover, Cube cube, int variableCount, ref int budget)
    {
        return Covers(cover, cube, variableCount, new Scratch(), ref budget);
    }

    private static bool? Covers(IReadOnlyList<Cube> cover, Cube cube, int variableCount,
        Scratch scratch, ref int budget)
    {
        // Cofactoring F by `cube` fixes exactly cube's variables. Rather than allocate a
        // reduced Cube per element, seed those variables into an "eliminated" bitmask and
        // keep the original cubes by reference — EffectiveLiteralCount masks the fixed
        // positions out, giving the same result as building cofactor cubes.
        var words = variableCount == 0 ? 1 : (variableCount + 63) / 64;
        var eliminated = scratch.EliminatedFor(words);
        for (var w = 0; w < words; w++) eliminated[w] = cube.Pos[w] | cube.Neg[w];

        var filtered = scratch.Filtered;
        filtered.Clear();
        foreach (var f in cover)
        {
            var conflict = false;
            for (var w = 0; w < f.Pos.Length; w++)
                if ((f.Pos[w] & cube.Neg[w]) != 0 || (f.Neg[w] & cube.Pos[w]) != 0)
                {
                    conflict = true;
                    break;
                }

            if (!conflict) filtered.Add(f);
        }

        return IsTautology(filtered, variableCount, eliminated, scratch.Pool, 0, ref budget);
    }

    /// <summary>
    ///     Exact tautology check on a cube list: unate shortcut plus recursion on the
    ///     most binate variable. Null = step budget exhausted (treat as "don't know").
    /// </summary>
    internal static bool? IsTautology(List<Cube> cover, int variableCount, ref int budget)
    {
        var words = variableCount == 0 ? 1 : (variableCount + 63) / 64;
        return IsTautology(cover, variableCount, new ulong[words], ref budget);
    }

    /// <summary>
    ///     Per-depth scratch lists for the tautology recursion. The recursion is strictly
    ///     depth-first and a level's two cofactor branches are used one after the other - the
    ///     first branch's subtree is fully explored before the second list is built - so ONE
    ///     reusable list per depth is enough. Previously each node allocated two fresh
    ///     <see cref="List{Cube}" />, which across thousands of Covers calls was the single
    ///     largest allocation source in the minimizer.
    /// </summary>
    private sealed class BranchPool
    {
        private readonly List<List<Cube>> _byDepth = new();

        public List<Cube> RentCleared(int depth)
        {
            while (_byDepth.Count <= depth) _byDepth.Add(new List<Cube>());
            var list = _byDepth[depth];
            list.Clear();
            return list;
        }
    }

    /// <summary>
    ///     Reusable working memory for one <see cref="Minimize" /> run. The point is that it
    ///     outlives the individual <c>Covers</c> calls: a single pass issues thousands of
    ///     them, and each used to allocate its own cofactor mask, filter list and per-node branch
    ///     lists. Holding them here makes the whole EXPAND/IRREDUNDANT/REDUCE loop allocate a
    ///     bounded amount instead of an amount proportional to the number of coverage tests.
    /// </summary>
    private sealed class Scratch
    {
        public readonly List<Cube> Filtered = new();
        public readonly BranchPool Pool = new();
        private ulong[] _eliminated = Array.Empty<ulong>();

        /// <summary>A zeroed mask of at least <paramref name="words" /> words.</summary>
        public ulong[] EliminatedFor(int words)
        {
            if (_eliminated.Length < words) _eliminated = new ulong[words];
            else Array.Clear(_eliminated, 0, words);
            return _eliminated;
        }
    }

    /// <summary>
    ///     Tautology recursion over an "eliminated" variable mask: instead of cloning cubes
    ///     to strip the branch variable, both cofactor branches share one mask (they always
    ///     eliminate the same variable) — set the bit, recurse, clear it. Zero cube cloning.
    /// </summary>
    private static bool? IsTautology(List<Cube> cover, int variableCount, ulong[] eliminated, ref int budget)
    {
        return IsTautology(cover, variableCount, eliminated, new BranchPool(), 0, ref budget);
    }

    private static bool? IsTautology(List<Cube> cover, int variableCount, ulong[] eliminated,
        BranchPool pool, int depth, ref int budget)
    {
        if (--budget <= 0) return null;
        // Universal cube: no literals remain once eliminated variables are masked out
        foreach (var c in cover)
            if (EffectiveLiteralCount(c, eliminated) == 0) return true;
        if (cover.Count == 0) return false;

        // Find the most binate variable; a unate cover without the universal cube
        // (checked above) can never be a tautology
        var bestVariable = -1;
        var bestScore = -1;
        for (var v = 0; v < variableCount; v++)
        {
            if ((eliminated[v >> 6] & (1UL << (v & 63))) != 0) continue;
            int pos = 0, neg = 0;
            foreach (var c in cover)
            {
                if (c.HasPos(v)) pos++;
                if (c.HasNeg(v)) neg++;
            }

            if (pos == 0 || neg == 0) continue;
            var score = Math.Min(pos, neg) * 1000 + pos + neg;
            if (score > bestScore)
            {
                bestScore = score;
                bestVariable = v;
            }
        }

        if (bestVariable < 0) return false;

        // Both branches eliminate bestVariable, so set the bit once around both recursions
        eliminated[bestVariable >> 6] |= 1UL << (bestVariable & 63);

        // value = true: keep cubes without a negative literal on bestVariable.
        // The branch list is rented for THIS depth; the recursion below rents depth + 1, so the
        // child never writes to the list it is reading.
        var branch = pool.RentCleared(depth);
        foreach (var c in cover)
            if (!c.HasNeg(bestVariable)) branch.Add(c);
        var verdict = IsTautology(branch, variableCount, eliminated, pool, depth + 1, ref budget);

        if (verdict == true)
        {
            // value = false: keep cubes without a positive literal on bestVariable. Safe to reuse
            // the same list - the first branch's subtree is finished and nothing retained it.
            branch = pool.RentCleared(depth);
            foreach (var c in cover)
                if (!c.HasPos(bestVariable)) branch.Add(c);
            verdict = IsTautology(branch, variableCount, eliminated, pool, depth + 1, ref budget);
        }

        eliminated[bestVariable >> 6] &= ~(1UL << (bestVariable & 63));
        return verdict;
    }

    /// <summary>Literal count of a cube ignoring variables already fixed by cofactoring.</summary>
    private static int EffectiveLiteralCount(Cube c, ulong[] eliminated)
    {
        var count = 0;
        for (var w = 0; w < c.Pos.Length; w++)
        {
            count += BitOperations.PopCount(c.Pos[w] & ~eliminated[w]);
            count += BitOperations.PopCount(c.Neg[w] & ~eliminated[w]);
        }

        return count;
    }

    /// <summary>Parse a sum-of-products AST into cubes; null when the tree is not SOP.</summary>
    internal static (List<Cube> Cover, List<string> Variables)? TryParseSop(AstNode sop)
    {
        var variables = sop.GetVariables().OrderBy(v => v).ToList();
        var index = variables.Select((v, i) => (v, i)).ToDictionary(p => p.v, p => p.i);
        var words = Math.Max(1, (variables.Count + 63) / 64);

        var cover = new List<Cube>();
        foreach (var term in AstUtilitiesFlattenOr(sop))
        {
            if (term is ConstantNode { Value: true }) return null; // whole cover is 1
            if (term is ConstantNode { Value: false }) continue;
            var cube = new Cube(words);
            foreach (var literal in FlattenAnd(term))
                switch (literal)
                {
                    case VariableNode variable:
                        if (cube.HasNeg(index[variable.Name])) return null;
                        cube.SetPos(index[variable.Name]);
                        break;
                    case NotNode { Operand: VariableNode negated }:
                        if (cube.HasPos(index[negated.Name])) return null;
                        cube.SetNeg(index[negated.Name]);
                        break;
                    default:
                        return null; // not a flat SOP
                }

            cover.Add(cube);
        }

        return (cover, variables);
    }

    internal static AstNode BuildSop(IReadOnlyList<Cube> cover, IReadOnlyList<string> variables)
    {
        if (cover.Count == 0) return ConstantNode.False;

        var terms = new List<AstNode>(cover.Count);
        foreach (var cube in cover)
        {
            var literals = new List<AstNode>();
            for (var v = 0; v < variables.Count; v++)
                if (cube.HasPos(v)) literals.Add(new VariableNode(variables[v]));
                else if (cube.HasNeg(v)) literals.Add(new NotNode(new VariableNode(variables[v])));

            terms.Add(literals.Count switch
            {
                0 => ConstantNode.True,
                1 => literals[0],
                _ => new AndNode(literals)
            });
        }

        return terms.Count == 1 ? terms[0] : new OrNode(terms);
    }

    private static IEnumerable<AstNode> AstUtilitiesFlattenOr(AstNode node)
    {
        if (node is OrNode or)
        {
            foreach (var operand in or.Operands)
                foreach (var t in AstUtilitiesFlattenOr(operand))
                    yield return t;
        }
        else
        {
            yield return node;
        }
    }

    private static IEnumerable<AstNode> FlattenAnd(AstNode node)
    {
        if (node is AndNode and)
        {
            foreach (var operand in and.Operands)
                foreach (var t in FlattenAnd(operand))
                    yield return t;
        }
        else
        {
            yield return node;
        }
    }
}
