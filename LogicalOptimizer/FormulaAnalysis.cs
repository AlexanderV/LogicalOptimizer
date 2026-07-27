namespace LogicalOptimizer;

/// <summary>
///     Semantic queries over a formula, built on the incremental SAT solver: backbone
///     (literals forced in every model), projected model enumeration, and backbone-based
///     simplification. All queries work at any scale — no 2^n enumeration is involved.
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
