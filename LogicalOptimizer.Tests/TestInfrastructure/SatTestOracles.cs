namespace LogicalOptimizer.Tests;

/// <summary>Shared test oracles and helpers for the SAT-related test suites.</summary>
internal static class SatTestOracles
{
    public static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    /// <summary>Exhaustive 2^n satisfiability oracle over DIMACS-style clauses.</summary>
    public static bool BruteForceSatisfiable(int variableCount, List<int[]> clauses)
    {
        for (var mask = 0; mask < 1 << variableCount; mask++)
        {
            var allSatisfied = clauses.All(clause =>
                clause.Any(l => ((mask >> (Math.Abs(l) - 1)) & 1) == 1 == l > 0));
            if (allSatisfied) return true;
        }

        return false;
    }
}
