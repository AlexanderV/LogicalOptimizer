using System.Globalization;
using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     A CNF satisfiability problem parsed from DIMACS: a fixed variable count and a list of
///     clauses over 1-based signed literals (a positive literal <c>v</c> asserts variable
///     <c>v</c>, a negative literal <c>-v</c> its negation). Hands off directly to the
///     in-house <see cref="SatSolver" />; also convertible to an <see cref="AstNode" /> for
///     the BDD / d-DNNF engines.
/// </summary>
public sealed class CnfProblem
{
    private readonly int[][] _clauses;

    internal CnfProblem(int variableCount, int[][] clauses)
    {
        VariableCount = variableCount;
        _clauses = clauses;
    }

    /// <summary>Declared number of variables (indices 1..VariableCount).</summary>
    public int VariableCount { get; }

    /// <summary>Clauses as arrays of non-zero signed literals; an empty array is the empty clause.</summary>
    public IReadOnlyList<int[]> Clauses => _clauses;

    /// <summary>Build a <see cref="SatSolver" /> loaded with every clause of this problem.</summary>
    public SatSolver ToSolver()
    {
        var solver = new SatSolver(VariableCount);
        foreach (var clause in _clauses) solver.AddClause(clause);
        return solver;
    }

    /// <summary>Solve this instance with the in-house CDCL solver.</summary>
    public SatResult Solve(int maxConflicts = 1_000_000, CancellationToken cancellationToken = default)
    {
        return ToSolver().Solve(maxConflicts, cancellationToken);
    }

    /// <summary>
    ///     Convert to a canonicalized boolean formula over variables named <c>x1..xN</c>
    ///     (conjunction of clause disjunctions), suitable for the BDD or d-DNNF engines.
    ///     An empty clause becomes the constant false; an instance with no clauses is the
    ///     constant true. Variables that never appear in a clause are absent from the tree.
    /// </summary>
    public AstNode ToFormula()
    {
        var factory = new FormulaFactory();
        if (_clauses.Length == 0) return factory.True;

        var clauseNodes = new List<AstNode>(_clauses.Length);
        foreach (var clause in _clauses)
        {
            if (clause.Length == 0) return factory.False; // empty clause: unsatisfiable
            var literals = clause.Select(l =>
            {
                var variable = factory.Variable("x" + Math.Abs(l));
                return l > 0 ? variable : factory.Not(variable);
            });
            clauseNodes.Add(factory.Or(literals));
        }

        return factory.And(clauseNodes);
    }

    /// <summary>Write this problem back out as DIMACS CNF text (round-trips through the parser).</summary>
    public void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("p cnf ");
        writer.Write(VariableCount.ToString(CultureInfo.InvariantCulture));
        writer.Write(' ');
        writer.Write(_clauses.Length.ToString(CultureInfo.InvariantCulture));
        writer.Write('\n');
        var builder = new StringBuilder();
        foreach (var clause in _clauses)
        {
            builder.Clear();
            foreach (var literal in clause)
            {
                builder.Append(literal.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
            }

            builder.Append('0');
            writer.Write(builder.ToString());
            writer.Write('\n');
        }
    }
}
