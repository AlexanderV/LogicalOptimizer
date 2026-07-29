using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Semantic queries over a formula, built on the incremental SAT solver: backbone
///     (literals forced in every model), projected model counting and enumeration, and
///     backbone-based simplification. All queries work at any scale — no 2^n enumeration
///     is involved.
/// </summary>
public static class FormulaAnalysis
{
    /// <summary>
    ///     Compute the backbone: variables that take the same value in every model.
    ///     Uses one SAT call per surviving candidate (incremental, with unsat-core-free
    ///     filtering by counter-models).
    /// </summary>
    public static BackboneResult ComputeBackbone(AstNode formula,
        int maxConflicts = EquivalenceChecker.DefaultMaxConflicts,
        CancellationToken cancellationToken = default)
    {
        var cnf = TseitinConverter.Convert(formula);
        var solver = SatSolver.FromCnf(cnf);

        switch (solver.Solve(maxConflicts, cancellationToken))
        {
            case SatResult.Unsatisfiable:
                return BackboneResult.Unsatisfiable();
            case SatResult.Unknown:
                return BackboneResult.Unknown();
        }

        // Candidates start at the first model's literals; every counter-model prunes
        var inputCount = cnf.InputVariables.Count;
        var candidates = new Dictionary<int, bool>();
        for (var v = 1; v <= inputCount; v++)
            candidates[v] = solver.GetValue(v);

        var forced = new Dictionary<string, bool>();
        foreach (var variable in Enumerable.Range(1, inputCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidates.TryGetValue(variable, out var value)) continue;

            var literal = value ? variable : -variable;
            switch (solver.Solve(new[] { -literal }, maxConflicts, cancellationToken))
            {
                case SatResult.Unsatisfiable:
                    forced[cnf.InputVariables[variable - 1]] = value;
                    break;

                case SatResult.Satisfiable:
                    // Counter-model: any candidate it contradicts is not backbone
                    foreach (var v in candidates.Keys.ToList())
                        if (solver.GetValue(v) != candidates[v])
                            candidates.Remove(v);
                    break;

                default:
                    return BackboneResult.Unknown();
            }
        }

        return BackboneResult.Satisfiable(forced);
    }

    /// <summary>
    ///     Enumerate models projected onto the input variables (each assignment appears
    ///     once; Tseitin auxiliaries are functionally determined). Lazily yields up to
    ///     <paramref name="maxModels" /> models via incremental solving with blocking clauses.
    /// </summary>
    public static IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateModels(AstNode formula,
        int maxModels = 1_000_000, int maxConflicts = EquivalenceChecker.DefaultMaxConflicts,
        CancellationToken cancellationToken = default)
    {
        var cnf = TseitinConverter.Convert(formula);
        var solver = SatSolver.FromCnf(cnf);
        var inputCount = cnf.InputVariables.Count;

        for (var found = 0; found < maxModels; found++)
        {
            if (solver.Solve(maxConflicts, cancellationToken) != SatResult.Satisfiable) yield break;

            var model = new Dictionary<string, bool>();
            var blockingClause = new int[Math.Max(inputCount, 1)];
            for (var v = 1; v <= inputCount; v++)
            {
                var value = solver.GetValue(v);
                model[cnf.InputVariables[v - 1]] = value;
                blockingClause[v - 1] = value ? -v : v;
            }

            yield return model;

            // A variable-free tautology has exactly one (empty) model
            if (inputCount == 0) yield break;
            solver.AddClause(blockingClause);
        }
    }

    /// <summary>
    ///     Count the number of DISTINCT assignments over <paramref name="projectedVariables" />
    ///     (the projection scope <c>P</c>) that extend to some model of the formula; the
    ///     remaining variables are existentially forgotten. Sound against the overcount trap:
    ///     different full models that agree on <c>P</c> are counted once.
    ///     <para>
    ///         Scope semantics: <paramref name="projectedVariables" /> MAY include names not
    ///         occurring in the formula — each such free variable multiplies the count by 2 and
    ///         is never an error. An empty projection returns <c>1</c> for a satisfiable formula
    ///         and <c>0</c> for an unsatisfiable one; projecting onto all of the formula's
    ///         variables equals <c>CountModels()</c>.
    ///     </para>
    ///     <para>
    ///         Engine: SAT blocking enumeration — one incremental solve per distinct projected
    ///         model, blocking over the projection literals only (output-sensitive). The shared
    ///         <see cref="ResourceBudget" /> maps to two honest bounds: the number of enumerated
    ///         projected models (<see cref="ResourceBudget.CoverStepLimit" />) and the SAT
    ///         conflicts allowed per solve (<see cref="ResourceBudget.SatConflictLimit" />).
    ///         Exhausting either yields <see cref="ProjectedCountStatus.BudgetExhausted" /> with
    ///         a null <c>Count</c> — a partial run is NEVER reported as exact. A null budget is
    ///         unbounded, subject only to <paramref name="cancellationToken" />, which throws
    ///         <see cref="OperationCanceledException" /> when signalled.
    ///     </para>
    /// </summary>
    public static ProjectedModelCountResult CountProjectedModels(AstNode formula,
        IReadOnlyCollection<string> projectedVariables,
        ResourceBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(projectedVariables);

        // A null budget is unbounded (subject only to the cancellation token).
        var maxModels = budget?.CoverStepLimit ?? int.MaxValue;
        var maxConflicts = budget?.SatConflictLimit ?? int.MaxValue;

        // Tseitin CNF: input variables occupy DIMACS indices 1..k (sorted by name), which is
        // exactly the formula's variables; auxiliaries follow. The encoding is model-projecting,
        // so blocking on the input-variable indices alone is sound.
        var cnf = TseitinConverter.Convert(formula);
        var indexOf = new Dictionary<string, int>(cnf.InputVariables.Count);
        for (var i = 0; i < cnf.InputVariables.Count; i++)
            indexOf[cnf.InputVariables[i]] = i + 1;

        // Split the projection into variables the formula constrains (blocked through the CNF)
        // and free variables it does not. A free variable cannot be blocked — it has no index —
        // so each independently doubles every projected model; we fold that 2^(free) factor in
        // at the end. Duplicate names collapse to one (a HashSet for free vars, indices for bound).
        var boundProjection = new List<int>();
        var boundSeen = new HashSet<int>();
        var freeProjectionVars = new HashSet<string>();
        foreach (var name in projectedVariables)
            if (indexOf.TryGetValue(name, out var index))
            {
                if (boundSeen.Add(index)) boundProjection.Add(index);
            }
            else
            {
                freeProjectionVars.Add(name);
            }

        var solver = SatSolver.FromCnf(cnf);
        BigInteger count = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (solver.Solve(maxConflicts, cancellationToken))
            {
                case SatResult.Unsatisfiable:
                    // Enumeration ran to completion within budget: the count is exact. Free
                    // projection variables fold in as a 2^(free) factor (0 stays 0 for UNSAT).
                    return ProjectedModelCountResult.Exact(count << freeProjectionVars.Count);
                case SatResult.Unknown:
                    // A conflict budget tripped before a verdict: withhold the count.
                    return ProjectedModelCountResult.BudgetExhausted();
            }

            count++;
            if (count > maxModels)
                return ProjectedModelCountResult.BudgetExhausted();

            // Blocking clause over the projection scope ONLY: the OR of the negated current
            // literals forbids the entire fibre of full models above this projected point, so
            // each distinct projection is produced exactly once — the crux that keeps the count
            // immune to the overcount trap. An empty bound projection yields the empty clause,
            // making the next solve UNSAT: an empty projection has one model iff the formula is SAT.
            var blocking = new int[boundProjection.Count];
            for (var k = 0; k < boundProjection.Count; k++)
            {
                var index = boundProjection[k];
                blocking[k] = solver.GetValue(index) ? -index : index;
            }

            solver.AddClause(blocking);
        }
    }

    /// <summary>
    ///     Backbone-based simplification: forced variables become constants, then constant
    ///     folding runs. Most useful beyond the exact-minimization range, where the rewrite
    ///     pipeline has no truth-table support. Returns the input when nothing is forced or
    ///     the backbone query exhausted its budget; an unsatisfiable formula becomes 0.
    /// </summary>
    public static AstNode SimplifyWithBackbone(AstNode formula,
        int maxConflicts = EquivalenceChecker.DefaultMaxConflicts,
        CancellationToken cancellationToken = default)
    {
        var backbone = ComputeBackbone(formula, maxConflicts, cancellationToken);
        if (backbone.IsSatisfiable == false) return ConstantNode.False;
        if (backbone.IsSatisfiable == null || backbone.ForcedVariables!.Count == 0) return formula;

        // Substitution builds through a factory, so constant folding happens as the
        // tree is reconstructed (no separate folding pass needed)
        var factory = new FormulaFactory();
        var folded = Substitute(formula, backbone.ForcedVariables, factory);

        // Re-attach the forced literals: x & f[x:=1] is equivalent to the original
        // (the factory drops a constant-true remainder automatically)
        var literals = backbone.ForcedVariables
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value
                ? factory.Variable(kv.Key)
                : factory.Not(factory.Variable(kv.Key)));

        return factory.And(literals.Append(folded));
    }

    private static AstNode Substitute(AstNode node, IReadOnlyDictionary<string, bool> values,
        FormulaFactory factory)
    {
        switch (node)
        {
            case VariableNode variable when values.TryGetValue(variable.Name, out var value):
                return value ? factory.True : factory.False;
            case VariableNode variable:
                return factory.Variable(variable.Name);
            case ConstantNode constant:
                return constant.Value ? factory.True : factory.False;
            case NotNode not:
                return factory.Not(Substitute(not.Operand, values, factory));
            case AndNode and:
                return factory.And(and.Operands.Select(o => Substitute(o, values, factory)));
            case OrNode or:
                return factory.Or(or.Operands.Select(o => Substitute(o, values, factory)));
            case XorNode xor:
                return factory.Xor(Substitute(xor.Left, values, factory), Substitute(xor.Right, values, factory));
            case EqvNode eqv:
                return factory.Equivalence(Substitute(eqv.Left, values, factory),
                    Substitute(eqv.Right, values, factory));
            case ImpNode imp:
                return factory.Implication(Substitute(imp.Left, values, factory),
                    Substitute(imp.Right, values, factory));
            case NandNode nand:
                return factory.Not(factory.And(Substitute(nand.Left, values, factory),
                    Substitute(nand.Right, values, factory)));
            case NorNode nor:
                return factory.Not(factory.Or(Substitute(nor.Left, values, factory),
                    Substitute(nor.Right, values, factory)));
            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
    }
}

/// <summary>Result of a backbone query.</summary>
public sealed class BackboneResult
{
    private BackboneResult(bool? isSatisfiable, IReadOnlyDictionary<string, bool>? forced)
    {
        IsSatisfiable = isSatisfiable;
        ForcedVariables = forced;
    }

    /// <summary>Null when the conflict budget ran out before a verdict.</summary>
    public bool? IsSatisfiable { get; }

    /// <summary>Variables with a forced polarity; set exactly when IsSatisfiable is true.</summary>
    public IReadOnlyDictionary<string, bool>? ForcedVariables { get; }

    internal static BackboneResult Satisfiable(IReadOnlyDictionary<string, bool> forced)
    {
        return new BackboneResult(true, forced);
    }

    internal static BackboneResult Unsatisfiable()
    {
        return new BackboneResult(false, null);
    }

    internal static BackboneResult Unknown()
    {
        return new BackboneResult(null, null);
    }
}

/// <summary>Outcome kind of a <see cref="FormulaAnalysis.CountProjectedModels" /> query.</summary>
public enum ProjectedCountStatus
{
    /// <summary>The returned <see cref="ProjectedModelCountResult.Count" /> is exact.</summary>
    Exact,

    /// <summary>
    ///     A budget (enumerated-model or per-solve conflict bound) stopped the computation
    ///     before an exact count was reached; the count is withheld.
    /// </summary>
    BudgetExhausted
}

/// <summary>
///     Result of <see cref="FormulaAnalysis.CountProjectedModels" />. <see cref="Count" /> is
///     non-null exactly when <see cref="Status" /> is <see cref="ProjectedCountStatus.Exact" />:
///     a partial (budget-limited) run never carries a count that could be mistaken for exact.
/// </summary>
public sealed class ProjectedModelCountResult
{
    private ProjectedModelCountResult(BigInteger? count, ProjectedCountStatus status)
    {
        Count = count;
        Status = status;
    }

    /// <summary>
    ///     The exact number of distinct projected models, or null when the computation ran out
    ///     of budget. Non-null iff <see cref="Status" /> is <see cref="ProjectedCountStatus.Exact" />.
    /// </summary>
    public BigInteger? Count { get; }

    /// <summary>Whether the count is exact or was cut short by a budget.</summary>
    public ProjectedCountStatus Status { get; }

    internal static ProjectedModelCountResult Exact(BigInteger count)
    {
        return new ProjectedModelCountResult(count, ProjectedCountStatus.Exact);
    }

    internal static ProjectedModelCountResult BudgetExhausted()
    {
        return new ProjectedModelCountResult(null, ProjectedCountStatus.BudgetExhausted);
    }
}
