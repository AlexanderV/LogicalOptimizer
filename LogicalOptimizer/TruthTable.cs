using System.Text;

namespace LogicalOptimizer
{
    /// <summary>
    /// Class for generating and working with truth tables
    /// </summary>
    public class TruthTable
    {
        public List<string> Variables { get; }
        public List<Dictionary<string, bool>> Rows { get; private set; }
        public List<bool> Results { get; }

        public TruthTable(List<string> variables, List<bool> results)
        {
            Variables = variables?.OrderBy(v => v).ToList() ?? new List<string>();
            Results = results ?? new List<bool>();
            Rows = new List<Dictionary<string, bool>>();
            GenerateRows();
        }

        private void GenerateRows()
        {
            var numVars = Variables.Count;
            var numRows = (int)Math.Pow(2, numVars);

            for (var i = 0; i < numRows; i++)
            {
                var row = new Dictionary<string, bool>();
                for (var j = 0; j < numVars; j++)
                {
                    var value = (i & (1 << (numVars - 1 - j))) != 0;
                    row[Variables[j]] = value;
                }
                Rows.Add(row);
            }
        }

        /// <summary>
        /// Generates a truth table for a string expression
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
        /// Generates a truth table for an AST expression
        /// </summary>
        public static TruthTable Generate(AstNode expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));

            var allVariables = expression.GetVariables().Where(v => v != "0" && v != "1").OrderBy(v => v).ToList();
            var results = new List<bool>();

            if (!allVariables.Any())
            {
                var result = EvaluateExpression(expression, new Dictionary<string, bool>());
                results.Add(result);
            }
            else
            {
                var numRows = (int)Math.Pow(2, allVariables.Count);
                for (var i = 0; i < numRows; i++)
                {
                    var variableAssignment = new Dictionary<string, bool>();
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
        /// Evaluates the expression value for a given set of variable values
        /// </summary>
        private static bool EvaluateExpression(AstNode node, Dictionary<string, bool> assignment)
        {
            return node switch
            {
                VariableNode varNode => varNode.Name switch
                {
                    "1" => true,
                    "0" => false,
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
        /// Compare two expressions for truth table equivalence
        /// </summary>
        public static bool AreEquivalent(string expression1, string expression2)
        {
            try
            {
                var table1 = Generate(expression1);
                var table2 = Generate(expression2);
                return AreEquivalent(table1, table2);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Compare two truth tables for equivalence
        /// </summary>
        public static bool AreEquivalent(TruthTable table1, TruthTable table2)
        {
            if (table1 == null || table2 == null)
                return false;

            var vars1 = table1.Variables.OrderBy(v => v).ToList();
            var vars2 = table2.Variables.OrderBy(v => v).ToList();
            
            // Special case: if both tables have no variables (constants)
            if (vars1.Count == 0 && vars2.Count == 0)
            {
                return table1.Results.SequenceEqual(table2.Results);
            }
            
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
            if (vars1.SequenceEqual(vars2))
            {
                return table1.Results.SequenceEqual(table2.Results);
            }
            
            // Different variables - need to evaluate both expressions for all possible values
            var numRows = (int)Math.Pow(2, allVars.Count);
            
            for (int i = 0; i < numRows; i++)
            {
                var assignment = new Dictionary<string, bool>();
                for (int j = 0; j < allVars.Count; j++)
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
        /// Evaluate a truth table result for a specific variable assignment
        /// </summary>
        private static bool EvaluateTableForAssignment(TruthTable table, Dictionary<string, bool> assignment)
        {
            // If table has no variables (constant), return the constant value
            if (table.Variables.Count == 0)
                return table.Results.Count > 0 && table.Results[0];
            
            // Find the row index for the given assignment
            var rowIndex = 0;
            for (int i = 0; i < table.Variables.Count; i++)
            {
                var varName = table.Variables[i];
                if (assignment.TryGetValue(varName, out var value) && value)
                {
                    rowIndex |= (1 << (table.Variables.Count - 1 - i));
                }
            }
            
            return rowIndex < table.Results.Count && table.Results[rowIndex];
        }

        /// <summary>
        /// Generate a side-by-side comparison of two truth tables
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
            
            // Header
            result.Append("| ");
            foreach (var variable in allVars)
            {
                result.Append($"{variable} | ");
            }
            result.AppendLine("Expr1 | Expr2 | Match |");
            
            // Separator
            result.Append("| ");
            foreach (var variable in allVars)
            {
                result.Append("--- | ");
            }
            result.AppendLine("----- | ----- | ----- |");
            
            // Rows
            var numRows = (int)Math.Pow(2, allVars.Count);
            for (int i = 0; i < numRows; i++)
            {
                var assignment = new Dictionary<string, bool>();
                for (int j = 0; j < allVars.Count; j++)
                {
                    assignment[allVars[j]] = (i & (1 << (allVars.Count - 1 - j))) != 0;
                }
                
                result.Append("| ");
                foreach (var variable in allVars)
                {
                    result.Append($"{(assignment[variable] ? "T" : "F")} | ");
                }
                
                // Evaluate expressions
                var ast1 = new Parser(new Lexer(expression1).Tokenize()).Parse();
                var ast2 = new Parser(new Lexer(expression2).Tokenize()).Parse();
                
                bool result1 = EvaluateExpression(ast1, assignment);
                bool result2 = EvaluateExpression(ast2, assignment);
                
                bool match = result1 == result2;
                result.AppendLine($"{(result1 ? "T" : "F")} | {(result2 ? "T" : "F")} | {(match ? "✓" : "✗")} |");
            }
            
            result.AppendLine();
            result.AppendLine($"Equivalent: {AreEquivalent(expression1, expression2)}");
            
            return result.ToString();
        }

        /// <summary>
        /// Returns a string representation of the truth table in proper tabular format
        /// </summary>
        public override string ToString()
        {
            if (!Variables.Any())
            {
                return $"Constant: {(Results.Any() && Results[0] ? "True" : "False")}";
            }

            var result = new StringBuilder();
            
            // Header
            result.Append("| ");
            foreach (var variable in Variables)
            {
                result.Append($"{variable} | ");
            }
            result.AppendLine("Result |");
            
            // Separator
            result.Append("| ");
            foreach (var variable in Variables)
            {
                result.Append("- | ");
            }
            result.AppendLine("------ |");
            
            // Rows
            for (int i = 0; i < Rows.Count && i < Results.Count; i++)
            {
                result.Append("| ");
                foreach (var variable in Variables)
                {
                    result.Append($"{(Rows[i][variable] ? "T" : "F")} | ");
                }
                result.AppendLine($"{(Results[i] ? "T" : "F")} |");
            }
            
            return result.ToString();
        }

        /// <summary>
        /// Get results as binary string (for backward compatibility)
        /// </summary>
        public string GetResultsString()
        {
            return string.Join("", Results.Select(r => r ? "1" : "0"));
        }

        /// <summary>
        /// Checks if this truth table is equivalent to another one
        /// </summary>
        public bool IsEquivalentTo(TruthTable other)
        {
            return AreEquivalent(this, other);
        }

        /// <summary>
        /// Checks if the truth table represents a tautology (always true)
        /// </summary>
        public bool IsTautology()
        {
            return Results.All(r => r);
        }

        /// <summary>
        /// Checks if the truth table represents a contradiction (always false)
        /// </summary>
        public bool IsContradiction()
        {
            return Results.All(r => !r);
        }

        /// <summary>
        /// Checks if the truth table is satisfiable (at least one true result)
        /// </summary>
        public bool IsSatisfiable()
        {
            return Results.Any(r => r);
        }
    }
}