namespace LogicalOptimizer;

/// <summary>
///     Two-level (SOP) minimization beyond the truth-table range, without enumerating 2^n
///     minterms. BOOM-style prime cover through the incremental SAT solver:
///     repeatedly find an uncovered ON-assignment, shrink its cube to a prime implicant
///     (unsat-core jump + greedy literal drops against ¬F), block the covered region,
///     then remove redundant cubes (IRREDUNDANT). The result is a sound equivalent SOP —
///     usually near-minimal, reported as Heuristic, never claimed proven.
/// </summary>
internal static class SatTwoLevelMinimizer
{
    /// <summary>
    ///     Attempt a prime-cover SOP for the formula. Returns null when a budget was
    ///     exhausted (callers keep their existing result).
    /// </summary>
    public static AstNode? TryMinimize(AstNode formula, int cubeLimit, int maxConflictsPerQuery,
        CancellationToken cancellationToken = default)
    {
        var variables = formula.GetVariables().OrderBy(v => v).ToList();
        var n = variables.Count;
        if (n == 0 || n > 30) return null;

        var onSolver = SatSolver.FromCnf(TseitinConverter.Convert(formula));
        var offSolver = SatSolver.FromCnf(TseitinConverter.Convert(new NotNode(formula)));
        var cubes = new List<(int Mask, int Value)>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (onSolver.Solve(maxConflictsPerQuery, cancellationToken))
            {
                case SatResult.Unknown:
                    return null;
                case SatResult.Unsatisfiable:
                    goto covered;
            }

            if (cubes.Count >= cubeLimit) return null;

            // Uncovered ON-point → full cube → prime implicant
            var value = 0;
            for (var v = 1; v <= n; v++)
                if (onSolver.GetValue(v))
                    value |= 1 << (v - 1);

            var cube = ShrinkToPrime(offSolver, n, (1 << n) - 1, value, maxConflictsPerQuery, cancellationToken);
            if (cube == null) return null;
            if (cube.Value.Mask == 0) return ConstantNode.True; // empty cube: tautology

            cubes.Add(cube.Value);

            // Block the newly covered region in the ON-search
            var blocking = new List<int>();
            for (var j = 0; j < n; j++)
            {
                if ((cube.Value.Mask & (1 << j)) == 0) continue;
                blocking.Add((cube.Value.Value & (1 << j)) != 0 ? -(j + 1) : j + 1);
            }

            onSolver.AddClause(blocking.ToArray());
        }

    covered:
        if (cubes.Count == 0) return ConstantNode.False;

        RemoveRedundantCubes(cubes, n, maxConflictsPerQuery, cancellationToken);
        return BuildSop(variables, cubes);
    }

    /// <summary>
    ///     Shrink a cube (an implicant of F, i.e. cube ∧ ¬F is UNSAT) to a prime one:
    ///     the unsat core drops many literals at once, then single-literal drops finish.
    /// </summary>
    private static (int Mask, int Value)? ShrinkToPrime(SatSolver offSolver, int n, int mask, int value,
        int maxConflictsPerQuery, CancellationToken cancellationToken)
    {
        var current = (Mask: mask, Value: value);

        // Core jump: assumptions that the solver actually used form a smaller implicant
        switch (offSolver.Solve(CubeAssumptions(current, n), maxConflictsPerQuery, cancellationToken))
        {
            case SatResult.Unsatisfiable:
                var core = offSolver.UnsatCore!;
                var coreMask = 0;
                var coreValue = 0;
                foreach (var literal in core)
                {
                    var bit = 1 << (Math.Abs(literal) - 1);
                    coreMask |= bit;
                    if (literal > 0) coreValue |= bit;
                }

                current = (coreMask, coreValue);
                break;

            case SatResult.Satisfiable:
                return null; // should not happen for a genuine ON-point; treat as budget failure
            default:
                return null;
        }

        // Greedy single-literal drops to primality
        for (var j = 0; j < n; j++)
        {
            var bit = 1 << j;
            if ((current.Mask & bit) == 0) continue;

            var candidate = (Mask: current.Mask & ~bit, current.Value & ~bit);
            if (candidate.Mask == 0)
            {
                // Dropping the last literal asks whether F is a tautology
                if (offSolver.Solve(Array.Empty<int>(), maxConflictsPerQuery, cancellationToken) ==
                    SatResult.Unsatisfiable)
                    return (0, 0);
                continue;
            }

            if (offSolver.Solve(CubeAssumptions(candidate, n), maxConflictsPerQuery, cancellationToken) ==
                SatResult.Unsatisfiable)
                current = candidate;
        }

        return current;
    }

    private static int[] CubeAssumptions((int Mask, int Value) cube, int n)
    {
        var assumptions = new List<int>();
        for (var j = 0; j < n; j++)
        {
            if ((cube.Mask & (1 << j)) == 0) continue;
            assumptions.Add((cube.Value & (1 << j)) != 0 ? j + 1 : -(j + 1));
        }

        return assumptions.ToArray();
    }

    /// <summary>
    ///     IRREDUNDANT: a cube is redundant when the union of the remaining cubes covers it
    ///     (its literals ∧ the negations of every other cube is UNSAT — pure clausal check).
    /// </summary>
    private static void RemoveRedundantCubes(List<(int Mask, int Value)> cubes, int n,
        int maxConflictsPerQuery, CancellationToken cancellationToken)
    {
        // Try widest cubes' victims first: check high-literal cubes for removal first
        foreach (var candidate in cubes.OrderByDescending(c =>
                     System.Numerics.BitOperations.PopCount((uint)c.Mask)).ToList())
        {
            if (cubes.Count == 1) break;
            cancellationToken.ThrowIfCancellationRequested();

            var solver = new SatSolver(n);
            foreach (var other in cubes)
            {
                if (other == candidate) continue;
                var clause = new List<int>();
                for (var j = 0; j < n; j++)
                {
                    if ((other.Mask & (1 << j)) == 0) continue;
                    clause.Add((other.Value & (1 << j)) != 0 ? -(j + 1) : j + 1);
                }

                solver.AddClause(clause.ToArray());
            }

            if (solver.Solve(CubeAssumptions(candidate, n), maxConflictsPerQuery, cancellationToken) ==
                SatResult.Unsatisfiable)
                cubes.Remove(candidate);
        }
    }

    private static AstNode BuildSop(IReadOnlyList<string> variables, List<(int Mask, int Value)> cubes)
    {
        AstNode? sop = null;
        foreach (var cube in cubes)
        {
            AstNode? term = null;
            for (var j = 0; j < variables.Count; j++)
            {
                if ((cube.Mask & (1 << j)) == 0) continue;
                AstNode literal = (cube.Value & (1 << j)) != 0
                    ? new VariableNode(variables[j])
                    : new NotNode(new VariableNode(variables[j]));
                term = term == null ? literal : new AndNode(term, literal);
            }

            sop = sop == null ? term! : new OrNode(sop, term!);
        }

        return sop!;
    }
}
