namespace LogicalOptimizer;

/// <summary>
///     Equivalence of two boolean expressions at any scale. Small expressions are compared
///     by truth table; larger ones through a miter (XOR of both sides) put through the
///     Tseitin transformation and the built-in SAT solver — UNSAT proves equivalence
///     without enumerating 2^n rows, SAT yields a concrete counterexample.
/// </summary>
public static class EquivalenceChecker
{
    /// <summary>Default conflict budget for one SAT-based equivalence query.</summary>
    public const int DefaultMaxConflicts = 200_000;

    public static EquivalenceCheckResult Check(string left, string right, int maxConflicts = DefaultMaxConflicts,
        CancellationToken cancellationToken = default)
    {
        return Check(Parse(left), Parse(right), maxConflicts, cancellationToken);
    }

    public static EquivalenceCheckResult Check(AstNode left, AstNode right, int maxConflicts = DefaultMaxConflicts,
        CancellationToken cancellationToken = default)
    {
        var variables = left.GetVariables();
        variables.UnionWith(right.GetVariables());

        return variables.Count <= PerformanceValidator.MAX_EQUIVALENCE_CHECK_VARIABLES
            ? CheckWithTruthTable(left, right, variables)
            : CheckWithSat(left, right, maxConflicts, cancellationToken);
    }

    private static EquivalenceCheckResult CheckWithTruthTable(AstNode left, AstNode right, HashSet<string> variables)
    {
        var ordered = variables.OrderBy(v => v).ToList();
        var assignment = new Dictionary<string, bool>();

        for (var mask = 0; mask < 1 << ordered.Count; mask++)
        {
            for (var j = 0; j < ordered.Count; j++)
                assignment[ordered[j]] = (mask & (1 << j)) != 0;

            if (TruthTable.Evaluate(left, assignment) != TruthTable.Evaluate(right, assignment))
                return EquivalenceCheckResult.NotEquivalent(new Dictionary<string, bool>(assignment));
        }

        return EquivalenceCheckResult.Equivalent();
    }

    /// <summary>
    ///     SAT-miter equivalence that also returns a DRAT proof when the verdict is
    ///     "equivalent" (UNSAT of the miter). The proof is checkable against the miter's
    ///     Tseitin CNF (also returned) by any DRAT/RUP checker, e.g. drat-trim — the
    ///     equivalence claim becomes externally verifiable rather than trusted.
    /// </summary>
    public static (EquivalenceCheckResult Result, TseitinCnf MiterCnf, string? DratProof) CheckWithProof(
        AstNode left, AstNode right, int maxConflicts = DefaultMaxConflicts,
        CancellationToken cancellationToken = default)
    {
        var miter = TseitinConverter.Convert(new XorNode(left, right));
        var solver = SatSolver.FromCnf(miter);
        solver.EnableProofLogging();

        switch (solver.Solve(maxConflicts, cancellationToken))
        {
            case SatResult.Unsatisfiable:
                return (EquivalenceCheckResult.Equivalent(), miter, solver.ToDrat());

            case SatResult.Satisfiable:
                var counterexample = new Dictionary<string, bool>();
                for (var i = 0; i < miter.InputVariables.Count; i++)
                    counterexample[miter.InputVariables[i]] = solver.GetValue(i + 1);
                return (EquivalenceCheckResult.NotEquivalent(counterexample), miter, null);

            default:
                return (EquivalenceCheckResult.Unknown(), miter, null);
        }
    }

    internal static EquivalenceCheckResult CheckWithSat(AstNode left, AstNode right, int maxConflicts,
        CancellationToken cancellationToken = default)
    {
        // Miter: left XOR right is satisfiable exactly when the sides differ somewhere
        var miter = TseitinConverter.Convert(new XorNode(left, right));
        var solver = SatSolver.FromCnf(miter);

        switch (solver.Solve(maxConflicts, cancellationToken))
        {
            case SatResult.Unsatisfiable:
                return EquivalenceCheckResult.Equivalent();

            case SatResult.Satisfiable:
                var counterexample = new Dictionary<string, bool>();
                for (var i = 0; i < miter.InputVariables.Count; i++)
                    counterexample[miter.InputVariables[i]] = solver.GetValue(i + 1);
                return EquivalenceCheckResult.NotEquivalent(counterexample);

            default:
                return EquivalenceCheckResult.Unknown();
        }
    }

    private static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }
}

/// <summary>
///     Verdict of an equivalence query. <see cref="AreEquivalent" /> is null when the
///     conflict budget ran out before a proof either way (only possible far beyond the
///     truth-table range).
/// </summary>
public sealed class EquivalenceCheckResult
{
    private EquivalenceCheckResult(bool? areEquivalent, IReadOnlyDictionary<string, bool>? counterexample)
    {
        AreEquivalent = areEquivalent;
        Counterexample = counterexample;
    }

    public bool? AreEquivalent { get; }

    /// <summary>Assignment where the two expressions differ; set exactly when AreEquivalent is false.</summary>
    public IReadOnlyDictionary<string, bool>? Counterexample { get; }

    internal static EquivalenceCheckResult Equivalent()
    {
        return new EquivalenceCheckResult(true, null);
    }

    internal static EquivalenceCheckResult NotEquivalent(IReadOnlyDictionary<string, bool> counterexample)
    {
        return new EquivalenceCheckResult(false, counterexample);
    }

    internal static EquivalenceCheckResult Unknown()
    {
        return new EquivalenceCheckResult(null, null);
    }
}
