using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Class for generating and working with truth tables
/// </summary>
public class TruthTable
{
    /// <summary>Hard cap: 2^n rows are materialized, so n must stay small.</summary>
    public const int MaxVariables = 20;

    private readonly List<string> _variables;
    private List<Dictionary<string, bool>>? _rows;
    private readonly List<bool> _results;

    public TruthTable(List<string> variables, List<bool> results)
    {
        if (variables == null) throw new ArgumentNullException(nameof(variables));
        if (results == null) throw new ArgumentNullException(nameof(results));

        _variables = variables.OrderBy(v => v).ToList();
        ValidateVariableCount(_variables.Count);

        if (_variables.Distinct().Count() != _variables.Count)
            throw new ArgumentException("Truth table variables must be unique");

        var expectedResults = 1 << _variables.Count;
        if (results.Count != expectedResults)
            throw new ArgumentException(
                $"Truth table for {_variables.Count} variables requires exactly {expectedResults} results, got {results.Count}");

        _results = new List<bool>(results);
    }

    public IReadOnlyList<string> Variables => _variables;

    /// <summary>
    ///     One assignment dictionary per row, materialized on first access.
    ///     <para>
    ///         Building these eagerly cost a <see cref="Dictionary{TKey,TValue}" /> per row - 1024
    ///         of them for a 10-variable table - on every truth table ever constructed, including
    ///         the ones the optimizer and its soundness guard build internally and then read only
    ///         through <see cref="Results" />. In production only the CSV exporter reads this, so
    ///         the rows are now built when they are actually asked for. The content is identical.
    ///     </para>
    /// </summary>
    public IReadOnlyList<Dictionary<string, bool>> Rows => _rows ??= GenerateRows();
    public IReadOnlyList<bool> Results => _results;

    private static void ValidateVariableCount(int count)
    {
        if (count > MaxVariables)
            throw new ArgumentException(
                $"Truth table would require 2^{count} rows; maximum supported is {MaxVariables} variables");
    }

    private List<Dictionary<string, bool>> GenerateRows()
    {
        var numVars = _variables.Count;
        var numRows = 1 << numVars;
        var rows = new List<Dictionary<string, bool>>(numRows);

        for (var i = 0; i < numRows; i++)
        {
            var row = new Dictionary<string, bool>(numVars);
            for (var j = 0; j < numVars; j++)
            {
                var value = (i & (1 << (numVars - 1 - j))) != 0;
                row[_variables[j]] = value;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    ///     Generates a truth table for a string expression
    /// </summary>
    public static TruthTable Generate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be empty", nameof(expression));

        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        return Generate(ast);
    }

    /// <summary>
    ///     Generates a truth table for an AST expression
    /// </summary>
    public static TruthTable Generate(AstNode expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        var allVariables = expression.GetVariables().OrderBy(v => v).ToList();
        ValidateVariableCount(allVariables.Count);
        var results = new List<bool>();

        if (!allVariables.Any())
        {
            var result = EvaluateExpression(expression, new Dictionary<string, bool>());
            results.Add(result);
        }
        else
        {
            var numRows = 1 << allVariables.Count;
            results.Capacity = numRows;

            // One assignment reused across rows: EvaluateExpression only reads it and never keeps
            // a reference, so a dictionary per row was pure garbage - 1024 of them at 10
            // variables, on top of the 1024 the row list used to build eagerly.
            var variableAssignment = new Dictionary<string, bool>(allVariables.Count);
            for (var i = 0; i < numRows; i++)
            {
                for (var j = 0; j < allVariables.Count; j++)
                {
                    var value = (i & (1 << (allVariables.Count - 1 - j))) != 0;
                    variableAssignment[allVariables[j]] = value;
                }

                var result = EvaluateExpression(expression, variableAssignment);
                results.Add(result);
            }
        }

        return new TruthTable(allVariables, results);
    }

    /// <summary>
    ///     Evaluates an expression for a given set of variable values
    /// </summary>
    public static bool Evaluate(AstNode node, Dictionary<string, bool> assignment)
    {
        return EvaluateExpression(node, assignment);
    }

    /// <summary>Conjunction over operands without allocating an enumerator or a closure.</summary>
    private static bool EvaluateAll(IReadOnlyList<AstNode> operands, Dictionary<string, bool> assignment)
    {
        for (var i = 0; i < operands.Count; i++)
            if (!EvaluateExpression(operands[i], assignment))
                return false;
        return true;
    }

    /// <summary>Disjunction over operands without allocating an enumerator or a closure.</summary>
    private static bool EvaluateAny(IReadOnlyList<AstNode> operands, Dictionary<string, bool> assignment)
    {
        for (var i = 0; i < operands.Count; i++)
            if (EvaluateExpression(operands[i], assignment))
                return true;
        return false;
    }

    /// <summary>
    ///     Evaluates the expression value for a given set of variable values
    /// </summary>
    private static bool EvaluateExpression(AstNode node, Dictionary<string, bool> assignment)
    {
        return node switch
        {
            ConstantNode constant => constant.Value,
            VariableNode varNode => assignment.TryGetValue(varNode.Name, out var value)
                ? value
                : throw new KeyNotFoundException($"No value provided for variable '{varNode.Name}'"),
            // Explicit loops, not All/Any: the lambdas captured `assignment`, so every n-ary node
            // of every row allocated a closure, a delegate and an enumerator. A 10-variable table
            // evaluates 1024 rows, which made this the bulk of one truth table's cost - and the
            // soundness guard builds two of them per check.
            AndNode andNode => EvaluateAll(andNode.Operands, assignment),
            OrNode orNode => EvaluateAny(orNode.Operands, assignment),
            NotNode notNode => !EvaluateExpression(notNode.Operand, assignment),
            XorNode xorNode => EvaluateExpression(xorNode.Left, assignment) ^
                               EvaluateExpression(xorNode.Right, assignment),
            ImpNode impNode => !EvaluateExpression(impNode.Left, assignment) ||
                               EvaluateExpression(impNode.Right, assignment),
            EqvNode eqvNode => EvaluateExpression(eqvNode.Left, assignment) ==
                               EvaluateExpression(eqvNode.Right, assignment),
            NandNode nandNode => !(EvaluateExpression(nandNode.Left, assignment) &&
                                   EvaluateExpression(nandNode.Right, assignment)),
            NorNode norNode => !(EvaluateExpression(norNode.Left, assignment) ||
                                 EvaluateExpression(norNode.Right, assignment)),
            _ => throw new ArgumentException($"Unknown node type: {node.GetType()}")
        };
    }

    /// <summary>
    ///     Compare two expressions for truth table equivalence
    /// </summary>
    public static bool AreEquivalent(string expression1, string expression2)
    {
        var table1 = Generate(expression1);
        var table2 = Generate(expression2);
        return AreEquivalent(table1, table2);
    }

    /// <summary>
    ///     Compare two ASTs for logical equivalence over the union of their variables.
    ///     Evaluates directly, without materializing table rows.
    /// </summary>
    public static bool AreEquivalent(AstNode expression1, AstNode expression2)
    {
        var variables = expression1.GetVariables();
        variables.UnionWith(expression2.GetVariables());
        var ordered = variables.OrderBy(v => v).ToList();
        ValidateVariableCount(ordered.Count);

        var assignment = new Dictionary<string, bool>();
        var numRows = 1 << ordered.Count;
        for (var i = 0; i < numRows; i++)
        {
            for (var j = 0; j < ordered.Count; j++)
                assignment[ordered[j]] = (i & (1 << j)) != 0;

            if (EvaluateExpression(expression1, assignment) != EvaluateExpression(expression2, assignment))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Compare two truth tables for equivalence
    /// </summary>
    public static bool AreEquivalent(TruthTable table1, TruthTable table2)
    {
        if (table1 == null || table2 == null)
            return false;

        var vars1 = table1.Variables.OrderBy(v => v).ToList();
        var vars2 = table2.Variables.OrderBy(v => v).ToList();

        // Special case: if both tables have no variables (constants)
        if (vars1.Count == 0 && vars2.Count == 0) return table1.Results.SequenceEqual(table2.Results);

        // Special case: one table is constant, other has variables
        if (vars1.Count == 0 || vars2.Count == 0)
        {
            var constantTable = vars1.Count == 0 ? table1 : table2;
            var variableTable = vars1.Count == 0 ? table2 : table1;

            // Check if all results in variable table match the constant value
            if (constantTable.Results.Count != 1)
                return false;

            var constantValue = constantTable.Results[0];
            return variableTable.Results.All(r => r == constantValue);
        }

        // Case where both tables have variables
        var allVars = vars1.Union(vars2).OrderBy(v => v).ToList();

        // If same variables, just compare results
        if (vars1.SequenceEqual(vars2)) return table1.Results.SequenceEqual(table2.Results);

        // Different variables - need to evaluate both expressions for all possible values
        ValidateVariableCount(allVars.Count);
        var numRows = 1 << allVars.Count;

        for (var i = 0; i < numRows; i++)
        {
            var assignment = new Dictionary<string, bool>();
            for (var j = 0; j < allVars.Count; j++)
            {
                var value = (i & (1 << (allVars.Count - 1 - j))) != 0;
                assignment[allVars[j]] = value;
            }

            // Evaluate table1 result for this assignment
            var result1 = EvaluateTableForAssignment(table1, assignment);
            var result2 = EvaluateTableForAssignment(table2, assignment);

            if (result1 != result2)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Evaluate a truth table result for a specific variable assignment
    /// </summary>
    private static bool EvaluateTableForAssignment(TruthTable table, Dictionary<string, bool> assignment)
    {
        // If table has no variables (constant), return the constant value
        if (table.Variables.Count == 0)
            return table.Results.Count > 0 && table.Results[0];

        // Find the row index for the given assignment
        var rowIndex = 0;
        for (var i = 0; i < table.Variables.Count; i++)
        {
            var varName = table.Variables[i];
            if (assignment.TryGetValue(varName, out var value) && value)
                rowIndex |= 1 << (table.Variables.Count - 1 - i);
        }

        return rowIndex < table.Results.Count && table.Results[rowIndex];
    }

    /// <summary>
    ///     Generate a side-by-side comparison of two truth tables
    /// </summary>
    public static string CompareExpressions(string expression1, string expression2)
    {
        var table1 = Generate(expression1);
        var table2 = Generate(expression2);

        var result = new StringBuilder();
        result.AppendLine("=== Truth Table Comparison ===");
        result.AppendLine($"Expression 1: {expression1}");
        result.AppendLine($"Expression 2: {expression2}");
        result.AppendLine();

        var allVars = table1.Variables.Union(table2.Variables).OrderBy(v => v).Distinct().ToList();

        // Calculate column widths
        var columnWidths = allVars.ToDictionary(v => v, v => Math.Max(v.Length, 1));
        const int expr1Width = 5; // "Expr1"
        const int expr2Width = 5; // "Expr2"
        const int matchWidth = 5; // "Match"

        // Header
        result.Append("| ");
        foreach (var variable in allVars) result.Append($"{variable.PadRight(columnWidths[variable])} | ");
        result.AppendLine(
            $"{"Expr1".PadRight(expr1Width)} | {"Expr2".PadRight(expr2Width)} | {"Match".PadRight(matchWidth)} |");

        // Separator
        result.Append("| ");
        foreach (var variable in allVars) result.Append($"{new string('-', columnWidths[variable])} | ");
        result.AppendLine(
            $"{new string('-', expr1Width)} | {new string('-', expr2Width)} | {new string('-', matchWidth)} |");

        // Rows
        var ast1 = new Parser(new Lexer(expression1).Tokenize()).Parse();
        var ast2 = new Parser(new Lexer(expression2).Tokenize()).Parse();

        ValidateVariableCount(allVars.Count);
        var numRows = 1 << allVars.Count;
        for (var i = 0; i < numRows; i++)
        {
            var assignment = new Dictionary<string, bool>();
            for (var j = 0; j < allVars.Count; j++) assignment[allVars[j]] = (i & (1 << (allVars.Count - 1 - j))) != 0;

            result.Append("| ");
            foreach (var variable in allVars)
            {
                var value = assignment[variable] ? "1" : "0";
                result.Append($"{value.PadRight(columnWidths[variable])} | ");
            }

            var result1 = EvaluateExpression(ast1, assignment);
            var result2 = EvaluateExpression(ast2, assignment);

            var match = result1 == result2;
            var result1Str = result1 ? "1" : "0";
            var result2Str = result2 ? "1" : "0";
            var matchStr = match ? "✓" : "✗";
            result.AppendLine(
                $"{result1Str.PadRight(expr1Width)} | {result2Str.PadRight(expr2Width)} | {matchStr.PadRight(matchWidth)} |");
        }

        result.AppendLine();
        result.AppendLine($"Equivalent: {AreEquivalent(expression1, expression2)}");

        return result.ToString();
    }

    /// <summary>
    ///     Returns a string representation of the truth table in proper tabular format
    /// </summary>
    public override string ToString()
    {
        if (!Variables.Any()) return $"Constant: {(Results.Any() && Results[0] ? "True" : "False")}";

        var result = new StringBuilder();

        // Calculate column widths
        var columnWidths = Variables.ToDictionary(v => v, v => Math.Max(v.Length, 1));
        const int resultWidth = 6; // "Result"

        // Header
        result.Append("| ");
        foreach (var variable in Variables) result.Append($"{variable.PadRight(columnWidths[variable])} | ");
        result.AppendLine($"{"Result".PadRight(resultWidth)} |");

        // Separator
        result.Append("| ");
        foreach (var variable in Variables) result.Append($"{new string('-', columnWidths[variable])} | ");
        result.AppendLine($"{new string('-', resultWidth)} |");

        // Rows
        for (var i = 0; i < Rows.Count && i < Results.Count; i++)
        {
            result.Append("| ");
            foreach (var variable in Variables)
            {
                var value = Rows[i][variable] ? "1" : "0";
                result.Append($"{value.PadRight(columnWidths[variable])} | ");
            }

            var resultValue = Results[i] ? "1" : "0";
            result.AppendLine($"{resultValue.PadRight(resultWidth)} |");
        }

        return result.ToString();
    }

    /// <summary>
    ///     Get results as binary string (for backward compatibility)
    /// </summary>
    public string GetResultsString()
    {
        return string.Join("", Results.Select(r => r ? "1" : "0"));
    }

    /// <summary>
    ///     Checks if this truth table is equivalent to another one
    /// </summary>
    public bool IsEquivalentTo(TruthTable other)
    {
        return AreEquivalent(this, other);
    }

    /// <summary>
    ///     Checks if the truth table represents a tautology (always true)
    /// </summary>
    public bool IsTautology()
    {
        return Results.All(r => r);
    }

    /// <summary>
    ///     Checks if the truth table represents a contradiction (always false)
    /// </summary>
    public bool IsContradiction()
    {
        return Results.All(r => !r);
    }

    /// <summary>
    ///     Checks if the truth table is satisfiable (at least one true result)
    /// </summary>
    public bool IsSatisfiable()
    {
        return Results.Any(r => r);
    }
}
