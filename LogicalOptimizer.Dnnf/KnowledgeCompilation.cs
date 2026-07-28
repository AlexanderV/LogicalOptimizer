namespace LogicalOptimizer;

/// <summary>
///     Knowledge compilation entry point: turn a boolean formula into a d-DNNF circuit that
///     answers model-counting and enumeration queries in time linear in the circuit size.
/// </summary>
public static class KnowledgeCompilation
{
    /// <summary>Default node budget for <see cref="CompileToDnnf" />.</summary>
    public const int DefaultNodeBudget = 1_000_000;

    /// <summary>
    ///     Compile <paramref name="formula" /> into a <see cref="DnnfCircuit" /> using a top-down
    ///     decision-DNNF compiler with component caching. Compilation can blow up on hard CNF, so
    ///     <paramref name="nodeBudget" /> caps the DAG size (an <see cref="InvalidOperationException" />
    ///     is thrown when it is exceeded) and <paramref name="cancellationToken" /> interrupts a
    ///     long compile. Both are heuristic safety limits, not guarantees of tractability.
    /// </summary>
    /// <param name="formula">The formula to compile.</param>
    /// <param name="nodeBudget">Maximum number of DAG nodes before compilation aborts.</param>
    /// <param name="cancellationToken">Cancels a long-running compilation.</param>
    /// <exception cref="InvalidOperationException">The node budget was exceeded.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static DnnfCircuit CompileToDnnf(AstNode formula, int nodeBudget = DefaultNodeBudget,
        CancellationToken cancellationToken = default)
    {
        return DnnfCompiler.Compile(formula, nodeBudget, cancellationToken);
    }
}
