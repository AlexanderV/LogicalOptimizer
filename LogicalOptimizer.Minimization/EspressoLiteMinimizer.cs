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

        var bestCost = Cost(current);
        for (var pass = 0; pass < 6; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Expand(current, variableCount, ref budget, cancellationToken);
            Irredundant(current, variableCount, ref budget, cancellationToken);
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

            Reduce(current, variableCount, ref budget, cancellationToken);
            if (budget <= 0) break;
        }

        // REDUCE may have left shrunk cubes behind; one final grow-and-prune
        Expand(current, variableCount, ref budget, cancellationToken);
        Irredundant(current, variableCount, ref budget, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        foreach (var cube in cover.OrderByDescending(c => c.LiteralCount()).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var v = 0; v < variableCount && budget > 0; v++)
            {
                if (!cube.HasPos(v) && !cube.HasNeg(v)) continue;
                var wasPos = cube.HasPos(v);
                var before = cube.Clone();
                var reference = cover.Where(c => !ReferenceEquals(c, cube)).Append(before).ToList();
                cube.Clear(v);
                if (Covers(reference, cube, variableCount, ref budget) != true)
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
        CancellationToken cancellationToken)
    {
        for (var i = cover.Count - 1; i >= 0 && budget > 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = cover[i];
            cover.RemoveAt(i);
            if (Covers(cover, candidate, variableCount, ref budget) != true)
                cover.Insert(i, candidate);
        }
    }

    /// <summary>
    ///     REDUCE: shrink each cube by re-adding literals whose lost half is already
    ///     covered elsewhere — freeing the next EXPAND to grow in a different direction.
    /// </summary>
    private static void Reduce(List<Cube> cover, int variableCount, ref int budget,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < cover.Count && budget > 0; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cube = cover[i];
            var rest = cover.Where((_, k) => k != i).ToList();
            for (var v = 0; v < variableCount && budget > 0; v++)
            {
                if (cube.HasPos(v) || cube.HasNeg(v)) continue;

                // Adding literal v keeps the cover iff rest covers (cube & !v)
                var lost = cube.Clone();
                lost.SetNeg(v);
                if (Covers(rest, lost, variableCount, ref budget) == true)
                {
                    cube.SetPos(v);
                    continue;
                }

                lost = cube.Clone();
                lost.SetPos(v);
                if (Covers(rest, lost, variableCount, ref budget) == true)
                    cube.SetNeg(v);
            }
        }
    }

    /// <summary>F ⊇ c ⟺ the cofactor of F by c is a tautology. Null = budget ran out.</summary>
    internal static bool? Covers(IReadOnlyList<Cube> cover, Cube cube, int variableCount, ref int budget)
    {
        var cofactored = new List<Cube>();
        foreach (var f in cover)
        {
            var reduced = CofactorByCube(f, cube);
            if (reduced != null) cofactored.Add(reduced);
        }

        return IsTautology(cofactored, variableCount, ref budget);
    }

    /// <summary>Cofactor one cube by all literals of another; null when they conflict.</summary>
    private static Cube? CofactorByCube(Cube f, Cube by)
    {
        for (var w = 0; w < f.Pos.Length; w++)
            if ((f.Pos[w] & by.Neg[w]) != 0 || (f.Neg[w] & by.Pos[w]) != 0)
                return null;

        var result = new Cube(f.Pos.Length);
        for (var w = 0; w < f.Pos.Length; w++)
        {
            result.Pos[w] = f.Pos[w] & ~by.Pos[w];
            result.Neg[w] = f.Neg[w] & ~by.Neg[w];
        }

        return result;
    }

    /// <summary>
    ///     Exact tautology check on a cube list: unate shortcut plus recursion on the
    ///     most binate variable. Null = step budget exhausted (treat as "don't know").
    /// </summary>
    internal static bool? IsTautology(List<Cube> cover, int variableCount, ref int budget)
    {
        if (--budget <= 0) return null;
        if (cover.Any(c => c.LiteralCount() == 0)) return true;
        if (cover.Count == 0) return false;

        // Find the most binate variable; a unate cover without the universal cube
        // (checked above) can never be a tautology
        var bestVariable = -1;
        var bestScore = -1;
        for (var v = 0; v < variableCount; v++)
        {
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

        foreach (var value in new[] { true, false })
        {
            var cofactor = new List<Cube>();
            foreach (var c in cover)
            {
                if (value ? c.HasNeg(bestVariable) : c.HasPos(bestVariable)) continue;
                var reduced = c.Clone();
                reduced.Clear(bestVariable);
                cofactor.Add(reduced);
            }

            var verdict = IsTautology(cofactor, variableCount, ref budget);
            if (verdict != true) return verdict;
        }

        return true;
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

        AstNode? sum = null;
        foreach (var cube in cover)
        {
            AstNode? product = null;
            for (var v = 0; v < variables.Count; v++)
            {
                AstNode? literal = cube.HasPos(v) ? new VariableNode(variables[v])
                    : cube.HasNeg(v) ? new NotNode(new VariableNode(variables[v]))
                    : null;
                if (literal == null) continue;
                product = product == null ? literal : new AndNode(product, literal);
            }

            product ??= ConstantNode.True;
            sum = sum == null ? product : new OrNode(sum, product);
        }

        return sum!;
    }

    private static IEnumerable<AstNode> AstUtilitiesFlattenOr(AstNode node)
    {
        if (node is OrNode or)
        {
            foreach (var t in AstUtilitiesFlattenOr(or.Left)) yield return t;
            foreach (var t in AstUtilitiesFlattenOr(or.Right)) yield return t;
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
            foreach (var t in FlattenAnd(and.Left)) yield return t;
            foreach (var t in FlattenAnd(and.Right)) yield return t;
        }
        else
        {
            yield return node;
        }
    }
}
