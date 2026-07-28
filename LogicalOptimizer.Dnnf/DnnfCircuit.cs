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
public sealed class DnnfCircuit
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
    ///     equal to (1, 1) this reproduces <see cref="CountModels" /> as a floating-point value.
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
    ///     Lazily enumerate every model, projected onto the original input variables. Free
    ///     variables (unconstrained in a subtree) expand to both polarities. The count can be
    ///     exponential — combine with Take/TakeWhile or call <see cref="CountModels" /> first.
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
