using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Class for generating and working with truth tables
/// </summary>
public class TruthTable
{
    public TruthTable(List<string> variables, List<bool> results)
    {
        Variables = variables?.OrderBy(v => v).ToList() ?? new List<string>();
        Results = results ?? new List<bool>();
        GenerateRows();
    }

    public List<string> Variables { get; }
    public List<Dictionary<string, bool>> Rows { get; private set; }
    public List<bool> Results { get; }

    private void GenerateRows()
    {
        Rows = new List<Dictionary<string, bool>>();
        var numVars = Variables.Count;
        var numRows = (int) Math.Pow(2, numVars);

        for (var i = 0; i < numRows; i++)
        {
            var row = new Dictionary<string, bool>();
            for (var j = 0; j < numVars; j++)
            {
                // Generate all possible combinations of variable values
                var value = (i & (1 << (numVars - 1 - j))) != 0;
                row[Variables[j]] = value;
            }

            Rows.Add(row);
        }
    }

    /// <summary>
    ///     Generates a truth table for an AST expression
    /// </summary>
    public static TruthTable Generate(AstNode expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        var allVariables = expression.GetVariables().Where(v => v != "0" && v != "1").OrderBy(v => v).ToList();
        var results = new List<bool>();

        if (!allVariables.Any())
        {
            // Constant expression
            var result = EvaluateExpression(expression, new Dictionary<string, bool>());
            results.Add(result);
        }
        else
        {
            var numVars = allVariables.Count;
            var numRows = (int) Math.Pow(2, numVars);

            for (var i = 0; i < numRows; i++)
            {
                var assignment = new Dictionary<string, bool>();
                for (var j = 0; j < numVars; j++)
                {
                    var value = (i & (1 << (numVars - 1 - j))) != 0;
                    assignment[allVariables[j]] = value;
                }

                var result = EvaluateExpression(expression, assignment);
                results.Add(result);
            }
        }

        return new TruthTable(allVariables, results);
    }

    /// <summary>
    ///     Generates a truth table for a string expression
    /// </summary>
    public static TruthTable Generate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be null or empty", nameof(expression));

        try
        {
            var lexer = new Lexer(expression);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();
            return Generate(ast);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to parse expression '{expression}': {ex.Message}", nameof(expression),
                ex);
        }
    }

    /// <summary>
    ///     Evaluates the expression value for a given set of variable values
    /// </summary>
    private static bool EvaluateExpression(AstNode node, Dictionary<string, bool> assignment)
    {
        return node switch
        {
            VariableNode varNode => varNode.Name switch
            {
                "1" => true, // True constant
                "0" => false, // False constant
                _ => assignment.TryGetValue(varNode.Name, out var value) ? value : false
            },
            AndNode andNode => EvaluateExpression(andNode.Left, assignment) &&
                               EvaluateExpression(andNode.Right, assignment),
            OrNode orNode => EvaluateExpression(orNode.Left, assignment) ||
                             EvaluateExpression(orNode.Right, assignment),
            NotNode notNode => !EvaluateExpression(notNode.Operand, assignment),
            _ => throw new ArgumentException($"Unknown node type: {node.GetType()}")
        };
    }

    /// <summary>
    ///     Checks equivalence of two truth tables
    /// </summary>
    public bool IsEquivalentTo(TruthTable other)
    {
        if (other == null) return false;

        // Get all variables from both tables
        var allVars = Variables.Union(other.Variables).OrderBy(v => v).ToList();

        // If no variables in both tables, compare constants
        if (!allVars.Any()) return Results.SequenceEqual(other.Results);

        // Generate normalized truth tables for all variables
        var normalizedThis = GenerateNormalizedTable(allVars);
        var normalizedOther = other.GenerateNormalizedTable(allVars);

        return normalizedThis.SequenceEqual(normalizedOther);
    }

    /// <summary>
    ///     Generates a normalized truth table for the given set of variables
    /// </summary>
    private List<bool> GenerateNormalizedTable(List<string> allVariables)
    {
        if (!allVariables.Any()) return Results.ToList();

        var numRows = (int) Math.Pow(2, allVariables.Count);
        var normalizedResults = new List<bool>();

        for (var i = 0; i < numRows; i++)
        {
            var assignment = new Dictionary<string, bool>();
            for (var j = 0; j < allVariables.Count; j++)
            {
                var value = (i & (1 << (allVariables.Count - 1 - j))) != 0;
                assignment[allVariables[j]] = value;
            }

            // If this is a constant (no variables), result is the same for all rows
            if (!Variables.Any())
            {
                normalizedResults.Add(Results.Any() ? Results[0] : false);
            }
            else
            {
                // Find corresponding row in the original table
                var myRowIndex = 0;
                for (var k = 0; k < Variables.Count; k++)
                    if (assignment[Variables[k]])
                        myRowIndex |= 1 << (Variables.Count - 1 - k);

                normalizedResults.Add(Results[myRowIndex]);
            }
        }

        return normalizedResults;
    }

    /// <summary>
    ///     Checks expression equivalence through truth table comparison
    /// </summary>
    public static bool AreEquivalent(string expression1, string expression2)
    {
        try
        {
            var table1 = Generate(expression1);
            var table2 = Generate(expression2);
            return table1.IsEquivalentTo(table2);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Checks AST expression equivalence through truth table comparison
    /// </summary>
    public static bool AreEquivalent(AstNode expression1, AstNode expression2)
    {
        try
        {
            var table1 = Generate(expression1);
            var table2 = Generate(expression2);
            return table1.IsEquivalentTo(table2);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Returns a string representation of the truth table
    /// </summary>
    public override string ToString()
    {
        if (!Variables.Any())
            return "Empty truth table";

        var sb = new StringBuilder();

        // Header
        foreach (var variable in Variables) sb.Append($"{variable}\t");
        sb.AppendLine("Result");

        // Separator
        for (var i = 0; i < Variables.Count + 1; i++) sb.Append("---\t");
        sb.AppendLine();

        // Table rows
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            foreach (var variable in Variables) sb.Append($"{(row[variable] ? "T" : "F")}\t");
            sb.AppendLine(Results[i] ? "T" : "F");
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Returns a compact string representation of truth table results
    /// </summary>
    public string GetResultsString()
    {
        return string.Join("", Results.Select(r => r ? "1" : "0"));
    }

    /// <summary>
    ///     Checks if the expression is a tautology (always true)
    /// </summary>
    public bool IsTautology()
    {
        return Results.All(r => r);
    }

    /// <summary>
    ///     Checks if the expression is a contradiction (always false)
    /// </summary>
    public bool IsContradiction()
    {
        return Results.All(r => !r);
    }

    /// <summary>
    ///     Checks if the expression is satisfiable (has at least one true value)
    /// </summary>
    public bool IsSatisfiable()
    {
        return Results.Any(r => r);
    }
}