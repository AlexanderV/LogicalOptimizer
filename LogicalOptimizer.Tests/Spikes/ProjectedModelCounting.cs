using System.Numerics;

namespace LogicalOptimizer.Tests.Spikes;

// ---------------------------------------------------------------------------------------------
//  P1.4-spike: projected model counting DESIGN SPIKE.
//
//  Projected model counting = count the number of DISTINCT assignments over a chosen subset
//  of variables (the "projection scope") that can be extended to a satisfying assignment of
//  the formula. The remaining variables are "projected out" (existentially forgotten).
//
//  The overcount trap: after projecting variables out
//  you MUST NOT simply drop their literals, sum OR-branch counts, or assume that d-DNNF
//  determinism survives forgetting. Different full models can collapse onto ONE projected
//  model, so naive summation OVERCOUNTS. See the worked example in
//  doc/spikes/projected-model-counting.md.
//
//  This is SPIKE code: it lives in the test project only (internal access is granted through
//  InternalsVisibleTo on .Sat / .Bdd / .Dnnf) and introduces NO public API. Nothing here is
//  shipped; the deliverable is a validated recommendation.
// ---------------------------------------------------------------------------------------------

/// <summary>
///     Honest status contract mirroring the roadmap's <c>ProjectedModelCountResult{Count?, Status}</c>.
///     A partial (budget-limited) result NEVER carries a count that could be mistaken for exact.
/// </summary>
internal enum ProjectedCountStatus
{
    /// <summary>The returned count is the exact number of distinct projected models.</summary>
    Exact,

    /// <summary>A model/step budget was hit before enumeration finished; count is withheld.</summary>
    BudgetExhausted,

    /// <summary>A conflict budget on a single solve was hit (verdict unknown); count is withheld.</summary>
    Unknown
}

/// <summary>Spike analogue of the sketched public <c>ProjectedModelCountResult</c>.</summary>
internal readonly record struct ProjectedCount(BigInteger? Count, ProjectedCountStatus Status)
{
    public static ProjectedCount Exact(BigInteger count) => new(count, ProjectedCountStatus.Exact);

    public static ProjectedCount Partial(ProjectedCountStatus status)
    {
        if (status == ProjectedCountStatus.Exact)
            throw new ArgumentException("A partial result cannot have Exact status", nameof(status));
        return new ProjectedCount(null, status);
    }
}

/// <summary>
///     Prototype strategies plus an independent brute-force oracle for projected model counting.
///     Two strategies are prototyped end to end:
///     <list type="bullet">
///         <item>(a) SAT blocking enumeration — the sound, budgeted MVP candidate.</item>
///         <item>(b) BDD existential abstraction — exact where the BDD is affordable.</item>
///     </list>
///     A third strategy (projected d-DNNF compilation) is discussed in prose in the design doc.
/// </summary>
internal static class ProjectedModelCounting
{
    // -----------------------------------------------------------------------------------------
    //  Independent oracle: brute-force projected-and-deduplicated enumeration.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    ///     Enumerate all 2^|universe| assignments, keep the satisfying ones, project each onto
    ///     <paramref name="projection" />, and return the SIZE of the resulting set of DISTINCT
    ///     projected assignments. This is the ground truth every prototype must match.
    ///     <paramref name="projection" /> must be a subset of <paramref name="universe" />.
    /// </summary>
    public static BigInteger BruteForceProjected(
        AstNode formula, IReadOnlyList<string> universe, IReadOnlyCollection<string> projection)
    {
        var projectionOrder = projection.ToList();
        var distinct = new HashSet<string>();
        var assignment = new Dictionary<string, bool>(universe.Count);
        var n = universe.Count;

        for (var mask = 0; mask < 1 << n; mask++)
        {
            for (var j = 0; j < n; j++)
                assignment[universe[j]] = (mask & (1 << j)) != 0;
            if (!TruthTable.Evaluate(formula, assignment)) continue;

            // Ordered key over the fixed projection order uniquely identifies each projected
            // assignment (two different projected tuples differ in at least one position).
            var key = string.Create(projectionOrder.Count, projectionOrder,
                (span, order) =>
                {
                    for (var i = 0; i < order.Count; i++) span[i] = assignment[order[i]] ? '1' : '0';
                });
            distinct.Add(key);
        }

        return distinct.Count;
    }

    // -----------------------------------------------------------------------------------------
    //  Strategy (a): SAT blocking enumeration — sound, budgeted MVP candidate.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    ///     Enumerate distinct projected models by repeatedly solving and, after each model,
    ///     adding a blocking clause built ONLY from the projection variables' current literals.
    ///     Blocking on the projection scope alone forbids every full model that shares that
    ///     projection, so each distinct projection is produced exactly once — this is precisely
    ///     what makes the strategy immune to the overcount trap.
    ///     <para>
    ///         The enumeration is bounded two ways: <paramref name="maxModels" /> caps how many
    ///         distinct projections may be enumerated, and <paramref name="maxConflicts" /> caps
    ///         each individual solve. Hitting either returns a partial result (never a count
    ///         passed off as exact), honouring the roadmap status contract.
    ///     </para>
    /// </summary>
    public static ProjectedCount SatBlockingEnumeration(
        AstNode formula, IReadOnlyList<string> universe, IReadOnlyCollection<string> projection,
        long maxModels = long.MaxValue, int maxConflicts = 1_000_000, CancellationToken cancellationToken = default)
    {
        // Tseitin CNF: input variables occupy DIMACS indices 1..k (sorted by name), which is
        // exactly `universe` sorted; auxiliaries follow. The encoding is model-projecting, so
        // blocking on the input-variable indices is sound.
        var cnf = TseitinConverter.Convert(formula);
        var indexOf = new Dictionary<string, int>(cnf.InputVariables.Count);
        for (var i = 0; i < cnf.InputVariables.Count; i++)
            indexOf[cnf.InputVariables[i]] = i + 1;

        // Projection variables that never appear in the formula are unconstrained. They cannot
        // be blocked through the CNF (they have no index), so each independently doubles every
        // projected model. We fold that 2^(free projection vars) factor in at the end.
        var boundProjection = new List<int>();
        var freeProjectionVars = 0;
        foreach (var name in projection)
            if (indexOf.TryGetValue(name, out var index)) boundProjection.Add(index);
            else freeProjectionVars++;

        var solver = SatSolver.FromCnf(cnf);
        BigInteger count = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = solver.Solve(maxConflicts, cancellationToken);

            if (result == SatResult.Unsatisfiable)
                return ProjectedCount.Exact(count << freeProjectionVars);
            if (result == SatResult.Unknown)
                return ProjectedCount.Partial(ProjectedCountStatus.Unknown);

            count++;
            if (count > maxModels)
                return ProjectedCount.Partial(ProjectedCountStatus.BudgetExhausted);

            // Blocking clause over the projection scope only: OR of the negated current literals.
            // When the bound projection is empty this is the empty clause, which makes the next
            // solve UNSAT — correct: an empty projection has one model (the empty tuple) iff SAT.
            var blocking = new int[boundProjection.Count];
            for (var k = 0; k < boundProjection.Count; k++)
            {
                var index = boundProjection[k];
                blocking[k] = solver.GetValue(index) ? -index : index;
            }

            solver.AddClause(blocking);
        }
    }

    // -----------------------------------------------------------------------------------------
    //  Strategy (b): BDD existential abstraction — exact where the BDD is affordable.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    ///     Build a ROBDD, existentially quantify away every non-projection variable
    ///     (<c>Exists</c> = the sound projection operator: distinct full models that agree on
    ///     the projection collapse into the same path), then count. Because the quantified
    ///     variables become free in the manager, their 2^k multiplicity is divided back out to
    ///     recover the number of distinct projected assignments.
    /// </summary>
    public static ProjectedCount BddExistentialAbstraction(
        AstNode formula, IReadOnlyList<string> universe, IReadOnlyCollection<string> projection,
        int nodeBudget = BinaryDecisionDiagram.DefaultNodeBudget)
    {
        var projectionSet = new HashSet<string>(projection);

        try
        {
            var bdd = BinaryDecisionDiagram.BuildWithBestOrder(formula, nodeBudget);
            var managerVars = new HashSet<string>(bdd.Variables);

            // Quantify away only the manager variables that are NOT projected. Forgetting a
            // variable the function does not depend on is a no-op, so universe variables absent
            // from the manager are simply skipped here.
            var toForget = bdd.Variables.Where(v => !projectionSet.Contains(v)).ToArray();
            var quantified = bdd.Exists(bdd.Root, toForget);

            var raw = bdd.CountSatisfyingAssignments(quantified);
            // raw counts over all |manager| variables; the quantified ones are now free, so
            // dividing by 2^|forgotten| yields the distinct in-formula projected assignments.
            var projectedCount = raw / BigInteger.Pow(2, toForget.Length);

            // Projection variables absent from the formula are unconstrained: each independently
            // doubles the projected-model set (mirrors the SAT path's free-variable handling).
            var freeProjectionVars = projection.Count(v => !managerVars.Contains(v));
            return ProjectedCount.Exact(projectedCount * BigInteger.Pow(2, freeProjectionVars));
        }
        catch (NodeBudgetExceededException)
        {
            return ProjectedCount.Partial(ProjectedCountStatus.BudgetExhausted);
        }
    }

    // -----------------------------------------------------------------------------------------
    //  Helper: build a canonical DNF AstNode from a truth table, mentioning EVERY universe
    //  variable (so the engines' variable scope equals the universe even for degenerate
    //  functions). Used by the exhaustive all-functions validation.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    ///     Build an <see cref="AstNode" /> whose model set is exactly the minterms flagged true
    ///     in <paramref name="table" /> (index m, bit j = <paramref name="variables" />[j]).
    ///     The result always mentions all variables: each minterm fixes every variable, and the
    ///     all-false function is rendered as an explicit contradiction over the whole scope.
    /// </summary>
    public static AstNode TruthTableToAst(bool[] table, IReadOnlyList<string> variables)
    {
        var n = variables.Count;
        var minterms = new List<AstNode>();
        for (var mask = 0; mask < table.Length; mask++)
        {
            if (!table[mask]) continue;
            var literals = new List<AstNode>(n);
            for (var j = 0; j < n; j++)
            {
                AstNode literal = new VariableNode(variables[j]);
                literals.Add((mask & (1 << j)) != 0 ? literal : new NotNode(literal));
            }

            minterms.Add(Conjoin(literals));
        }

        if (minterms.Count == 0)
        {
            // Unsatisfiable, yet mentioning every variable: x0 & !x0 & x1 & ... & x(n-1).
            var operands = new List<AstNode>
            {
                new VariableNode(variables[0]),
                new NotNode(new VariableNode(variables[0]))
            };
            for (var j = 1; j < n; j++) operands.Add(new VariableNode(variables[j]));
            return new AndNode(operands);
        }

        return Disjoin(minterms);
    }

    private static AstNode Conjoin(IReadOnlyList<AstNode> nodes) =>
        nodes.Count == 1 ? nodes[0] : new AndNode(nodes);

    private static AstNode Disjoin(IReadOnlyList<AstNode> nodes) =>
        nodes.Count == 1 ? nodes[0] : new OrNode(nodes);
}
