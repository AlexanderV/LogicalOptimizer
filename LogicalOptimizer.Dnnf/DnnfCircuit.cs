using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     A compiled d-DNNF (deterministic, decomposable Negation Normal Form) circuit for a
///     boolean formula. Compilation happens once (see <see cref="KnowledgeCompilation" />);
///     afterwards exact model counting, weighted model counting and model enumeration are all
///     linear in the circuit size.
///     <para>
///         The circuit is compiled over the full equisatisfiable Tseitin CNF of the input
///         formula (input variables plus functionally-determined gate auxiliaries). Because the
///         full biconditional Tseitin encoding is equi-count over the input variables — every
///         satisfying input assignment extends to exactly one auxiliary assignment — the model
///         count of the whole circuit equals the model count of the original formula over its
///         input variables, with no projection needed. <see cref="Variables" /> and
///         <see cref="EnumerateModels" /> expose only the original input variables; the
///         auxiliaries are projected away.
///     </para>
/// </summary>
public sealed partial class DnnfCircuit
{
    private readonly int _falseId;
    private readonly int _inputVariableCount;
    private readonly IReadOnlyList<string> _inputVariables;
    private readonly DnnfNode[] _nodes;
    private readonly int _root;

    internal DnnfCircuit(DnnfNode[] nodes, int root, IReadOnlyList<string> inputVariables,
        int inputVariableCount, int falseId)
    {
        _nodes = nodes;
        _root = root;
        _inputVariables = inputVariables;
        _inputVariableCount = inputVariableCount;
        _falseId = falseId;
    }

    /// <summary>The original input variables of the compiled formula (sorted by name).</summary>
    public IReadOnlyList<string> Variables => _inputVariables;

    /// <summary>Number of nodes in the compiled d-DNNF DAG.</summary>
    public int NodeCount => _nodes.Length;

    /// <summary>Whether the formula has at least one model.</summary>
    public bool IsSatisfiable => _root != _falseId;

    /// <summary>
    ///     Exact number of satisfying assignments (#SAT) of the original formula over its input
    ///     variables. A single bottom-up pass over the DAG: literal -&gt; 1, decomposable AND -&gt;
    ///     product of children, deterministic OR (decision on v) -&gt; sum of the two branches.
    ///     Because every variable in a node's scope is represented explicitly (the circuit is
    ///     smooth), no gap/smoothing correction is needed.
    /// </summary>
    public BigInteger CountModels()
    {
        var memo = new BigInteger[_nodes.Length];
        var done = new bool[_nodes.Length];
        return Count(_root, memo, done);
    }

    private BigInteger Count(int id, BigInteger[] memo, bool[] done)
    {
        if (done[id]) return memo[id];
        var node = _nodes[id];
        BigInteger result;
        switch (node.Kind)
        {
            case DnnfKind.False:
                result = BigInteger.Zero;
                break;
            case DnnfKind.True:
            case DnnfKind.Literal:
                result = BigInteger.One;
                break;
            case DnnfKind.Or:
                // A decision node's variable contributes its two polarities implicitly:
                // one model set for v=false (low), one for v=true (high).
                result = Count(node.Children[0], memo, done) + Count(node.Children[1], memo, done);
                break;
            case DnnfKind.And:
                result = BigInteger.One;
                foreach (var child in node.Children)
                    result *= Count(child, memo, done);
                break;
            default:
                throw new InvalidOperationException($"Unknown node kind: {node.Kind}");
        }

        memo[id] = result;
        done[id] = true;
        return result;
    }

    /// <summary>
    ///     Weighted model count: the sum over all models of the product of per-literal weights.
    ///     <paramref name="weights" /> maps each input variable name to its (positive, negative)
    ///     literal weights; a variable absent from the map defaults to (1, 1). With every weight
    ///     equal to (1, 1) this reproduces <see cref="CountModels()" /> as a floating-point value.
    ///     Functionally-determined Tseitin auxiliaries always carry weight (1, 1), so they do not
    ///     affect the result.
    /// </summary>
    public double WeightedModelCount(IReadOnlyDictionary<string, (double positive, double negative)> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var positive = new double[_inputVariableCount + 1];
        var negative = new double[_inputVariableCount + 1];
        for (var v = 1; v <= _inputVariableCount; v++)
        {
            if (weights.TryGetValue(_inputVariables[v - 1], out var w))
            {
                positive[v] = w.positive;
                negative[v] = w.negative;
            }
            else
            {
                positive[v] = 1.0;
                negative[v] = 1.0;
            }
        }

        var memo = new double[_nodes.Length];
        var done = new bool[_nodes.Length];
        return Weighted(_root, positive, negative, memo, done);
    }

    private double Weighted(int id, double[] positive, double[] negative, double[] memo, bool[] done)
    {
        if (done[id]) return memo[id];
        var node = _nodes[id];
        double result;
        switch (node.Kind)
        {
            case DnnfKind.False:
                result = 0.0;
                break;
            case DnnfKind.True:
                result = 1.0;
                break;
            case DnnfKind.Literal:
                result = LiteralWeight(node.Value, positive, negative);
                break;
            case DnnfKind.Or:
                // The decision variable's literal weight is applied here (it appears nowhere
                // else): negative polarity weights the low branch, positive the high branch.
                var v = node.Value;
                result = VariableWeight(v, negative)
                         * Weighted(node.Children[0], positive, negative, memo, done)
                         + VariableWeight(v, positive)
                         * Weighted(node.Children[1], positive, negative, memo, done);
                break;
            case DnnfKind.And:
                result = 1.0;
                foreach (var child in node.Children)
                    result *= Weighted(child, positive, negative, memo, done);
                break;
            default:
                throw new InvalidOperationException($"Unknown node kind: {node.Kind}");
        }

        memo[id] = result;
        done[id] = true;
        return result;
    }

    private double LiteralWeight(int literal, double[] positive, double[] negative)
    {
        var variable = Math.Abs(literal);
        return literal > 0 ? VariableWeight(variable, positive) : VariableWeight(variable, negative);
    }

    // Auxiliary Tseitin variables (index beyond the input count) are functionally determined
    // and carry a neutral weight of 1.0.
    private double VariableWeight(int variable, double[] table)
    {
        return variable <= _inputVariableCount ? table[variable] : 1.0;
    }

    /// <summary>
    ///     Condition (cofactor) the circuit on a partial assignment, returning a NEW circuit in
    ///     which every named variable is pinned to the given value; <c>this</c> is never mutated.
    ///     <para>
    ///         Semantics: the conditioned circuit keeps the SAME variable universe —
    ///         <see cref="Variables" /> is unchanged and every conditioned variable stays in the
    ///         model-count universe, now pinned to exactly one value. Consequently
    ///         <c>Condition(a).CountModels()</c> equals the number of <c>this</c>'s models that are
    ///         consistent with <paramref name="assignment" /> (a pinned variable contributes a
    ///         factor of one, not two). Repeatedly conditioning composes:
    ///         <c>Condition(a).Condition(b)</c> equals <c>Condition(a ∪ b)</c> when a and b agree.
    ///     </para>
    ///     <para>
    ///         Every key of <paramref name="assignment" /> must be one of the circuit's
    ///         <see cref="Variables" />; an unknown name is an <see cref="ArgumentException" />. An
    ///         empty assignment returns an equivalent circuit (same model count). The rewrite is a
    ///         single memoized pass over the shared DAG and allocates at most O(<see cref="NodeCount" />)
    ///         additional nodes, so it never exceeds the original compilation's node budget.
    ///     </para>
    /// </summary>
    /// <param name="assignment">Variables to pin, each mapped to the value it is fixed to.</param>
    /// <param name="cancellationToken">Cancels the rewrite on a large circuit.</param>
    /// <exception cref="ArgumentException">A key is not one of the circuit's variables.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public DnnfCircuit Condition(IReadOnlyDictionary<string, bool> assignment,
        CancellationToken cancellationToken = default)
    {
        var evidence = ResolveAssignment(assignment, nameof(assignment));

        // Fresh builder: ids 0 = False, 1 = True match the compiler's terminal convention so
        // the resulting circuit's CountModels()/IsSatisfiable use the same sentinels.
        const int falseId = 0;
        const int trueId = 1;
        var nodes = new List<DnnfNode>(_nodes.Length + 2)
        {
            new DnnfNode(DnnfKind.False, 0, Array.Empty<int>()),
            new DnnfNode(DnnfKind.True, 0, Array.Empty<int>())
        };
        var memo = new int[_nodes.Length];
        var mapped = new bool[_nodes.Length];
        var counter = 0;

        int Add(DnnfNode node)
        {
            nodes.Add(node);
            return nodes.Count - 1;
        }

        // Conjoin already-filtered operands (no True, no False among them); disjoint scopes.
        int MakeAnd(List<int> operands)
        {
            if (operands.Count == 0) return trueId;
            if (operands.Count == 1) return operands[0];
            return Add(new DnnfNode(DnnfKind.And, 0, operands.ToArray()));
        }

        int Rewrite(int id)
        {
            if (mapped[id]) return memo[id];
            if ((counter++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var node = _nodes[id];
            int result;
            switch (node.Kind)
            {
                case DnnfKind.False:
                    result = falseId;
                    break;
                case DnnfKind.True:
                    result = trueId;
                    break;
                case DnnfKind.Literal:
                    {
                        var variable = Math.Abs(node.Value);
                        var pinned = variable <= _inputVariableCount ? evidence[variable] : null;
                        if (pinned is null)
                            result = Add(new DnnfNode(DnnfKind.Literal, node.Value, Array.Empty<int>()));
                        else
                            result = node.Value > 0 == pinned.Value ? trueId : falseId;
                        break;
                    }
                case DnnfKind.Or:
                    {
                        var variable = node.Value;
                        var pinned = variable <= _inputVariableCount ? evidence[variable] : null;
                        if (pinned is null)
                        {
                            var low = Rewrite(node.Children[0]);
                            var high = Rewrite(node.Children[1]);
                            // Keep the decision even if one branch is False; the variable stays in
                            // scope (smoothness), and a False branch contributes zero to the count.
                            result = low == falseId && high == falseId
                                ? falseId
                                : Add(new DnnfNode(DnnfKind.Or, variable, new[] { low, high }));
                        }
                        else
                        {
                            // Pin the decision variable and keep only the consistent branch, but keep
                            // the variable represented: Lit(v = pinned) AND the restricted branch. The
                            // literal's scope {v} is disjoint from the branch's, so the AND stays
                            // decomposable and the circuit stays smooth.
                            var branch = Rewrite(pinned.Value ? node.Children[1] : node.Children[0]);
                            if (branch == falseId)
                            {
                                result = falseId;
                            }
                            else
                            {
                                var literal = Add(new DnnfNode(DnnfKind.Literal,
                                    pinned.Value ? variable : -variable, Array.Empty<int>()));
                                result = branch == trueId
                                    ? literal
                                    : MakeAnd(new List<int> { literal, branch });
                            }
                        }

                        break;
                    }
                case DnnfKind.And:
                    {
                        var operands = new List<int>(node.Children.Length);
                        var conflict = false;
                        foreach (var child in node.Children)
                        {
                            var rewritten = Rewrite(child);
                            if (rewritten == falseId)
                            {
                                conflict = true;
                                break;
                            }

                            if (rewritten == trueId) continue; // True has empty scope; drop it
                            operands.Add(rewritten);
                        }

                        result = conflict ? falseId : MakeAnd(operands);
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unknown node kind: {node.Kind}");
            }

            memo[id] = result;
            mapped[id] = true;
            return result;
        }

        var root = Rewrite(_root);
        return new DnnfCircuit(nodes.ToArray(), root, _inputVariables, _inputVariableCount, falseId);
    }

    /// <summary>
    ///     Exact number of models consistent with <paramref name="evidence" />: the count over the
    ///     circuit's input variables (see <see cref="CountModels()" />) restricted to assignments
    ///     that agree with the given partial assignment. Equivalent to
    ///     <c>Condition(evidence).CountModels()</c> but computed in a single bottom-up pass without
    ///     building a new circuit. Empty evidence reproduces <see cref="CountModels()" /> exactly;
    ///     a full assignment yields 0 or 1.
    ///     <para>
    ///         Every key of <paramref name="evidence" /> must be one of the circuit's
    ///         <see cref="Variables" />; an unknown name is an <see cref="ArgumentException" />.
    ///     </para>
    /// </summary>
    /// <exception cref="ArgumentException">A key is not one of the circuit's variables.</exception>
    public BigInteger CountModels(IReadOnlyDictionary<string, bool> evidence)
    {
        var table = ResolveAssignment(evidence, nameof(evidence));
        var memo = new BigInteger[_nodes.Length];
        var done = new bool[_nodes.Length];
        return CountWithEvidence(_root, table, memo, done);
    }

    private BigInteger CountWithEvidence(int id, bool?[] evidence, BigInteger[] memo, bool[] done)
    {
        if (done[id]) return memo[id];
        var node = _nodes[id];
        BigInteger result;
        switch (node.Kind)
        {
            case DnnfKind.False:
                result = BigInteger.Zero;
                break;
            case DnnfKind.True:
                result = BigInteger.One;
                break;
            case DnnfKind.Literal:
                result = LiteralConsistent(node.Value, evidence) ? BigInteger.One : BigInteger.Zero;
                break;
            case DnnfKind.Or:
                {
                    var pinned = node.Value <= _inputVariableCount ? evidence[node.Value] : null;
                    if (pinned is null)
                        result = CountWithEvidence(node.Children[0], evidence, memo, done)
                                 + CountWithEvidence(node.Children[1], evidence, memo, done);
                    else
                        // Only the branch consistent with the evidence contributes.
                        result = CountWithEvidence(node.Children[pinned.Value ? 1 : 0], evidence, memo, done);
                    break;
                }
            case DnnfKind.And:
                result = BigInteger.One;
                foreach (var child in node.Children)
                    result *= CountWithEvidence(child, evidence, memo, done);
                break;
            default:
                throw new InvalidOperationException($"Unknown node kind: {node.Kind}");
        }

        memo[id] = result;
        done[id] = true;
        return result;
    }

    /// <summary>
    ///     Weighted model count restricted to the models consistent with <paramref name="evidence" />:
    ///     the sum over those models of the product of per-literal weights (see
    ///     <see cref="WeightedModelCount(IReadOnlyDictionary{string, ValueTuple{double, double}})" />).
    ///     Equivalent to weighting <c>Condition(evidence)</c>; empty evidence reproduces the plain
    ///     weighted count. The floating-point contract of the unconditioned overload carries over:
    ///     the result is exact when the weights and intermediate sums are representable, and
    ///     accumulates the usual IEEE-754 rounding otherwise (validated to a documented tolerance).
    ///     <para>
    ///         Every key of <paramref name="evidence" /> must be one of the circuit's
    ///         <see cref="Variables" />; an unknown name is an <see cref="ArgumentException" />.
    ///         Unknown <paramref name="weights" /> keys are ignored, exactly as in the
    ///         unconditioned overload.
    ///     </para>
    /// </summary>
    /// <exception cref="ArgumentException">An evidence key is not one of the circuit's variables.</exception>
    public double WeightedModelCount(IReadOnlyDictionary<string, (double positive, double negative)> weights,
        IReadOnlyDictionary<string, bool> evidence)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var table = ResolveAssignment(evidence, nameof(evidence));

        var positive = new double[_inputVariableCount + 1];
        var negative = new double[_inputVariableCount + 1];
        for (var v = 1; v <= _inputVariableCount; v++)
        {
            if (weights.TryGetValue(_inputVariables[v - 1], out var w))
            {
                positive[v] = w.positive;
                negative[v] = w.negative;
            }
            else
            {
                positive[v] = 1.0;
                negative[v] = 1.0;
            }
        }

        var memo = new double[_nodes.Length];
        var done = new bool[_nodes.Length];
        return WeightedWithEvidence(_root, positive, negative, table, memo, done);
    }

    private double WeightedWithEvidence(int id, double[] positive, double[] negative, bool?[] evidence,
        double[] memo, bool[] done)
    {
        if (done[id]) return memo[id];
        var node = _nodes[id];
        double result;
        switch (node.Kind)
        {
            case DnnfKind.False:
                result = 0.0;
                break;
            case DnnfKind.True:
                result = 1.0;
                break;
            case DnnfKind.Literal:
                result = LiteralConsistent(node.Value, evidence)
                    ? LiteralWeight(node.Value, positive, negative)
                    : 0.0;
                break;
            case DnnfKind.Or:
                {
                    var v = node.Value;
                    var pinned = v <= _inputVariableCount ? evidence[v] : null;
                    if (pinned is null)
                        result = VariableWeight(v, negative)
                                 * WeightedWithEvidence(node.Children[0], positive, negative, evidence, memo, done)
                                 + VariableWeight(v, positive)
                                 * WeightedWithEvidence(node.Children[1], positive, negative, evidence, memo, done);
                    else
                        // Only the consistent branch survives, weighted by the pinned literal.
                        result = VariableWeight(v, pinned.Value ? positive : negative)
                                 * WeightedWithEvidence(node.Children[pinned.Value ? 1 : 0], positive, negative,
                                     evidence, memo, done);
                    break;
                }
            case DnnfKind.And:
                result = 1.0;
                foreach (var child in node.Children)
                    result *= WeightedWithEvidence(child, positive, negative, evidence, memo, done);
                break;
            default:
                throw new InvalidOperationException($"Unknown node kind: {node.Kind}");
        }

        memo[id] = result;
        done[id] = true;
        return result;
    }

    // True when a literal is consistent with the evidence: unconstrained input variables and all
    // functionally-determined auxiliaries always pass; a pinned variable passes only in its sign.
    private bool LiteralConsistent(int literal, bool?[] evidence)
    {
        var variable = Math.Abs(literal);
        if (variable > _inputVariableCount) return true;
        var pinned = evidence[variable];
        return pinned is null || literal > 0 == pinned.Value;
    }

    // Resolve a partial assignment keyed by variable name into a 1-based table indexed by input
    // variable id (null = unconstrained). Every key must name one of the circuit's variables.
    private bool?[] ResolveAssignment(IReadOnlyDictionary<string, bool> assignment, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(assignment, parameterName);
        var table = new bool?[_inputVariableCount + 1];
        if (assignment.Count == 0) return table;

        var index = new Dictionary<string, int>(_inputVariableCount);
        for (var v = 1; v <= _inputVariableCount; v++) index[_inputVariables[v - 1]] = v;

        foreach (var (name, value) in assignment)
        {
            if (!index.TryGetValue(name, out var id))
                throw new ArgumentException(
                    $"Variable '{name}' is not one of the circuit's variables.", parameterName);
            table[id] = value;
        }

        return table;
    }

    /// <summary>
    ///     The weighted marginal probability that <paramref name="variable" /> is <c>true</c>: the
    ///     weighted model count restricted to models in which the variable is true, divided by the
    ///     unrestricted weighted model count —
    ///     <c>WeightedModelCount(weights, {variable = true}) / WeightedModelCount(weights)</c>,
    ///     both computed with the evidence overload. The result lies in <c>[0, 1]</c>.
    ///     <para>
    ///         With uniform weights — every entry <c>(1, 1)</c>, or a variable simply omitted from
    ///         <paramref name="weights" /> (which defaults to <c>(1, 1)</c>) — this is exactly the
    ///         fraction of the formula's models in which <paramref name="variable" /> is true.
    ///     </para>
    ///     <para>
    ///         Floating-point: computed in <see cref="double" /> as the ratio of two weighted
    ///         counts (see
    ///         <see cref="WeightedModelCount(IReadOnlyDictionary{string, ValueTuple{double, double}})" />),
    ///         so it inherits that overload's IEEE-754 accumulation behaviour — exact when the
    ///         weights and intermediate sums are representable, otherwise carrying the usual
    ///         rounding.
    ///     </para>
    /// </summary>
    /// <param name="variable">One of the circuit's <see cref="Variables" />.</param>
    /// <param name="weights">
    ///     Per-variable (positive, negative) literal weights; a variable absent from the map
    ///     defaults to (1, 1). Every weight must be finite and non-negative.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="variable" /> is not one of the circuit's variables, or a weight is
    ///     negative, NaN or infinite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     The total weighted model count is zero (an unsatisfiable formula or all-zero weights),
    ///     so the marginal is undefined — never a fabricated value.
    /// </exception>
    public double MarginalProbability(string variable,
        IReadOnlyDictionary<string, (double positive, double negative)> weights)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(weights);
        EnsureKnownVariable(variable);
        ValidateWeights(weights);

        var total = WeightedModelCount(weights);
        if (!(total > 0.0))
            throw new InvalidOperationException(
                "MarginalProbability is undefined: the total weighted model count is zero " +
                "(an unsatisfiable formula or all-zero weights).");

        var withTrue = WeightedModelCount(weights, new Dictionary<string, bool>(1) { [variable] = true });
        return withTrue / total;
    }

    /// <summary>
    ///     Draw one model at random, projected onto the circuit's <see cref="Variables" /> — every
    ///     variable is assigned exactly one value. Standard top-down d-DNNF sampling and the mirror
    ///     of the bottom-up weighted pass: at a deterministic decision a branch is taken with
    ///     probability proportional to its (weighted) share of the model count, at a decomposable
    ///     conjunction every child is sampled independently, and a literal fixes its variable. With
    ///     <paramref name="weights" /> <c>null</c> the draw is uniform over the formula's satisfying
    ///     assignments; otherwise each model is drawn with probability proportional to the product
    ///     of its per-literal weights (its weighted-model-count share).
    ///     <para>
    ///         Randomness comes solely from <paramref name="random" />: the distribution is only as
    ///         good as that source, and the method makes NO cryptographic guarantee. Branch
    ///         probabilities are evaluated in <see cref="double" /> and inherit the weighted pass's
    ///         IEEE-754 behaviour.
    ///     </para>
    /// </summary>
    /// <param name="random">The random source consumed by the draw.</param>
    /// <param name="weights">
    ///     Optional per-variable (positive, negative) literal weights; a variable absent from the
    ///     map defaults to (1, 1) and every weight must be finite and non-negative. <c>null</c>
    ///     samples uniformly.
    /// </param>
    /// <exception cref="ArgumentException">A weight is negative, NaN or infinite.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The circuit has zero total weight (an unsatisfiable formula or all-zero weights): there
    ///     is no model to draw, and none is fabricated.
    /// </exception>
    public IReadOnlyDictionary<string, bool> SampleModel(Random random,
        IReadOnlyDictionary<string, (double positive, double negative)>? weights = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        var (positive, negative, memo) = PrepareSampling(weights);
        return SampleOnce(random, positive, negative, memo);
    }

    /// <summary>
    ///     Draw <paramref name="count" /> models deterministically from <paramref name="seed" />:
    ///     the same seed produces the exact same sequence on every run and platform (a fresh
    ///     <see cref="Random" /> seeded with <paramref name="seed" /> drives the draws). Each model
    ///     is produced exactly as by <see cref="SampleModel" /> — uniform when
    ///     <paramref name="weights" /> is <c>null</c>, weighted otherwise, and every model assigns
    ///     the full set of <see cref="Variables" />. The sequence is lazy; combine with LINQ
    ///     (Take/Where/…) as needed.
    ///     <para>No cryptographic guarantee is made about the distribution.</para>
    /// </summary>
    /// <param name="count">Number of models to draw (non-negative).</param>
    /// <param name="seed">Seed for the deterministic pseudo-random sequence.</param>
    /// <param name="weights">
    ///     Optional per-variable (positive, negative) literal weights (see
    ///     <see cref="SampleModel" />); <c>null</c> samples uniformly.
    /// </param>
    /// <param name="cancellationToken">Cancels the (potentially long) enumeration between draws.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> is negative.</exception>
    /// <exception cref="ArgumentException">A weight is negative, NaN or infinite.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The circuit has zero total weight (unsatisfiable or all-zero weights); no model is
    ///     fabricated.
    /// </exception>
    public IEnumerable<IReadOnlyDictionary<string, bool>> SampleModels(int count, int seed,
        IReadOnlyDictionary<string, (double positive, double negative)>? weights = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        // Validate the weights and the zero-weight/UNSAT contract eagerly (before the first draw)
        // and compute the weighted node values once; every draw reuses that single bottom-up pass.
        var (positive, negative, memo) = PrepareSampling(weights);
        return Draw(count, seed, positive, negative, memo, cancellationToken);
    }

    private IEnumerable<IReadOnlyDictionary<string, bool>> Draw(int count, int seed,
        double[] positive, double[] negative, double[] memo, CancellationToken cancellationToken)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return SampleOnce(random, positive, negative, memo);
        }
    }

    // Build the (positive, negative) weight tables and the memoized weighted node values used for
    // sampling, validating the weights and enforcing the zero-weight/UNSAT contract. A null weight
    // map (or a variable absent from it) means uniform weight (1, 1).
    private (double[] positive, double[] negative, double[] memo) PrepareSampling(
        IReadOnlyDictionary<string, (double positive, double negative)>? weights)
    {
        var positive = new double[_inputVariableCount + 1];
        var negative = new double[_inputVariableCount + 1];
        for (var v = 1; v <= _inputVariableCount; v++)
            if (weights is not null && weights.TryGetValue(_inputVariables[v - 1], out var w))
            {
                CheckWeight(w.positive, _inputVariables[v - 1]);
                CheckWeight(w.negative, _inputVariables[v - 1]);
                positive[v] = w.positive;
                negative[v] = w.negative;
            }
            else
            {
                positive[v] = 1.0;
                negative[v] = 1.0;
            }

        var memo = new double[_nodes.Length];
        var done = new bool[_nodes.Length];
        var total = Weighted(_root, positive, negative, memo, done);
        if (!(total > 0.0))
            throw new InvalidOperationException(
                "Cannot sample: the circuit has zero total weight (an unsatisfiable formula or " +
                "all-zero weights), so there is no model to draw.");
        return (positive, negative, memo);
    }

    private IReadOnlyDictionary<string, bool> SampleOnce(Random random, double[] positive,
        double[] negative, double[] memo)
    {
        var assignment = new Dictionary<int, bool>(_inputVariableCount);
        SampleNode(_root, random, positive, negative, memo, assignment);
        // Smoothness guarantees the sampled path assigns every variable in the root's scope, which
        // is exactly the input variables plus the functionally-determined auxiliaries; project the
        // auxiliaries away so the result assigns precisely the circuit's Variables.
        var projected = new Dictionary<string, bool>(_inputVariableCount);
        foreach (var (variable, value) in assignment)
            if (variable <= _inputVariableCount)
                projected[_inputVariables[variable - 1]] = value;
        return projected;
    }

    // Top-down mirror of the bottom-up weighted pass: descend from the root, at each decision
    // choosing a branch with probability proportional to its share of the node's weighted count.
    private void SampleNode(int id, Random random, double[] positive, double[] negative,
        double[] memo, Dictionary<int, bool> assignment)
    {
        var node = _nodes[id];
        switch (node.Kind)
        {
            case DnnfKind.True:
                break; // empty scope: nothing to assign
            case DnnfKind.Literal:
                assignment[Math.Abs(node.Value)] = node.Value > 0;
                break;
            case DnnfKind.Or:
                {
                    var v = node.Value;
                    var high = VariableWeight(v, positive) * memo[node.Children[1]];
                    var low = VariableWeight(v, negative) * memo[node.Children[0]];
                    // high + low == memo[id] > 0 on every reachable node, so the draw is well defined.
                    // A zero-value branch is never taken: if high == 0 the strict inequality fails and
                    // the low branch is chosen; if low == 0 then NextDouble() * high < high always holds.
                    var takeHigh = random.NextDouble() * (low + high) < high;
                    assignment[v] = takeHigh;
                    SampleNode(node.Children[takeHigh ? 1 : 0], random, positive, negative, memo, assignment);
                    break;
                }
            case DnnfKind.And:
                foreach (var child in node.Children)
                    SampleNode(child, random, positive, negative, memo, assignment);
                break;
            default:
                // False is unreachable here: the root is checked for positive weight and no chosen
                // decision branch ever points at a zero-weight (False) child.
                throw new InvalidOperationException($"Cannot sample node kind: {node.Kind}");
        }
    }

    // Every key of a weight map that names an input variable must be finite and non-negative;
    // unknown keys are ignored, exactly as the weighted-count overloads treat them.
    private void ValidateWeights(IReadOnlyDictionary<string, (double positive, double negative)> weights)
    {
        for (var v = 1; v <= _inputVariableCount; v++)
            if (weights.TryGetValue(_inputVariables[v - 1], out var w))
            {
                CheckWeight(w.positive, _inputVariables[v - 1]);
                CheckWeight(w.negative, _inputVariables[v - 1]);
            }
    }

    private static void CheckWeight(double weight, string variable)
    {
        if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0.0)
            throw new ArgumentException(
                $"Weight for variable '{variable}' must be finite and non-negative, but was {weight}.",
                "weights");
    }

    private void EnsureKnownVariable(string variable)
    {
        for (var v = 1; v <= _inputVariableCount; v++)
            if (_inputVariables[v - 1] == variable)
                return;
        throw new ArgumentException(
            $"Variable '{variable}' is not one of the circuit's variables.", nameof(variable));
    }

    /// <summary>
    ///     Lazily enumerate every model, projected onto the original input variables. Free
    ///     variables (unconstrained in a subtree) expand to both polarities. The count can be
    ///     exponential — combine with Take/TakeWhile or call <see cref="CountModels()" /> first.
    /// </summary>
    public IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateModels(
        CancellationToken cancellationToken = default)
    {
        foreach (var full in Enumerate(_root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projected = new Dictionary<string, bool>(_inputVariableCount);
            foreach (var (variable, value) in full)
                if (variable <= _inputVariableCount)
                    projected[_inputVariables[variable - 1]] = value;
            yield return projected;
        }
    }

    // Yields assignments keyed by 1-based variable index over the node's full (smooth) scope.
    private IEnumerable<Dictionary<int, bool>> Enumerate(int id, CancellationToken cancellationToken)
    {
        var node = _nodes[id];
        switch (node.Kind)
        {
            case DnnfKind.False:
                yield break;
            case DnnfKind.True:
                yield return new Dictionary<int, bool>();
                break;
            case DnnfKind.Literal:
                yield return new Dictionary<int, bool> { [Math.Abs(node.Value)] = node.Value > 0 };
                break;
            case DnnfKind.Or:
                var variable = node.Value;
                foreach (var model in Enumerate(node.Children[0], cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    model[variable] = false;
                    yield return model;
                }

                foreach (var model in Enumerate(node.Children[1], cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    model[variable] = true;
                    yield return model;
                }

                break;
            case DnnfKind.And:
                var lists = new List<List<Dictionary<int, bool>>>(node.Children.Length);
                foreach (var child in node.Children)
                {
                    var models = Enumerate(child, cancellationToken).ToList();
                    if (models.Count == 0) yield break; // a false conjunct kills the product
                    lists.Add(models);
                }

                foreach (var combination in CartesianProduct(lists, 0, cancellationToken))
                    yield return combination;

                break;
            default:
                throw new InvalidOperationException($"Unknown node kind: {node.Kind}");
        }
    }

    private static IEnumerable<Dictionary<int, bool>> CartesianProduct(
        List<List<Dictionary<int, bool>>> lists, int index, CancellationToken cancellationToken)
    {
        if (index == lists.Count)
        {
            yield return new Dictionary<int, bool>();
            yield break;
        }

        foreach (var head in lists[index])
            foreach (var tail in CartesianProduct(lists, index + 1, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var merged = new Dictionary<int, bool>(head);
                foreach (var (variable, value) in tail)
                    merged[variable] = value;
                yield return merged;
            }
    }
}
