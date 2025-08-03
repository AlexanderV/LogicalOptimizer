using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicalOptimizer;

/// <summary>
/// Enhanced truth table generator using compiled C# expressions
/// </summary>
public class CompiledTruthTable
{
    public class TruthTableRow
    {
        public Dictionary<string, bool> Variables { get; set; } = new();
        public bool Result { get; set; }
        
        public override string ToString()
        {
            var vars = string.Join(", ", Variables.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"[{vars}] => {Result}";
        }
    }

    public List<TruthTableRow> Rows { get; set; } = new();
    public List<string> Variables { get; set; } = new();
    public string Expression { get; set; } = "";

    /// <summary>
    /// Generate truth table using compiled expression evaluation
    /// </summary>
    public static CompiledTruthTable Generate(AstNode node, string expressionText = "")
    {
        var evaluator = new CompiledExpressionEvaluator(node);
        var variables = evaluator.GetVariables();
        var truthTable = new CompiledTruthTable
        {
            Variables = variables,
            Expression = expressionText
        };

        // Generate all possible combinations
        var numCombinations = (int)Math.Pow(2, variables.Count);
        
        for (int i = 0; i < numCombinations; i++)
        {
            var combination = new Dictionary<string, bool>();
            
            // Generate boolean values for each variable
            for (int j = 0; j < variables.Count; j++)
            {
                combination[variables[j]] = (i & (1 << j)) != 0;
            }
            
            // Evaluate using compiled expression
            var result = evaluator.Evaluate(combination);
            
            truthTable.Rows.Add(new TruthTableRow
            {
                Variables = combination,
                Result = result
            });
        }

        return truthTable;
    }

    /// <summary>
    /// Check if two truth tables are equivalent
    /// </summary>
    public static bool AreEquivalent(CompiledTruthTable table1, CompiledTruthTable table2)
    {
        if (table1.Variables.Count != table2.Variables.Count)
            return false;

        if (!table1.Variables.OrderBy(v => v).SequenceEqual(table2.Variables.OrderBy(v => v)))
            return false;

        if (table1.Rows.Count != table2.Rows.Count)
            return false;

        // Sort rows by variable values for comparison
        var rows1 = table1.Rows.OrderBy(r => string.Join(",", r.Variables.OrderBy(kv => kv.Key).Select(kv => kv.Value))).ToList();
        var rows2 = table2.Rows.OrderBy(r => string.Join(",", r.Variables.OrderBy(kv => kv.Key).Select(kv => kv.Value))).ToList();

        for (int i = 0; i < rows1.Count; i++)
        {
            if (rows1[i].Result != rows2[i].Result)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Generate truth table comparison
    /// </summary>
    public static string CompareExpressions(AstNode original, AstNode optimized, string originalText = "", string optimizedText = "")
    {
        var originalTable = Generate(original, originalText);
        var optimizedTable = Generate(optimized, optimizedText);
        
        var result = new StringBuilder();
        result.AppendLine("Truth Table Comparison:");
        result.AppendLine($"Original: {originalText}");
        result.AppendLine($"Optimized: {optimizedText}");
        result.AppendLine();
        
        // Header
        var variables = originalTable.Variables;
        result.Append("| ");
        foreach (var variable in variables)
        {
            result.Append($"{variable} | ");
        }
        result.AppendLine("Original | Optimized | Match |");
        
        // Separator
        result.Append("| ");
        foreach (var variable in variables)
        {
            result.Append("- | ");
        }
        result.AppendLine("-------- | --------- | ----- |");
        
        // Rows
        for (int i = 0; i < originalTable.Rows.Count; i++)
        {
            var originalRow = originalTable.Rows[i];
            var optimizedRow = optimizedTable.Rows[i];
            
            result.Append("| ");
            foreach (var variable in variables)
            {
                result.Append($"{(originalRow.Variables[variable] ? "T" : "F")} | ");
            }
            
            var match = originalRow.Result == optimizedRow.Result;
            result.AppendLine($"{(originalRow.Result ? "T" : "F")} | {(optimizedRow.Result ? "T" : "F")} | {(match ? "✓" : "✗")} |");
        }
        
        result.AppendLine();
        result.AppendLine($"Equivalent: {AreEquivalent(originalTable, optimizedTable)}");
        
        return result.ToString();
    }

    /// <summary>
    /// Export to CSV format
    /// </summary>
    public string ToCsv()
    {
        var result = new StringBuilder();
        
        // Header
        result.Append(string.Join(",", Variables));
        result.AppendLine(",Result");
        
        // Rows
        foreach (var row in Rows)
        {
            var values = Variables.Select(v => row.Variables[v] ? "1" : "0");
            result.Append(string.Join(",", values));
            result.AppendLine($",{(row.Result ? "1" : "0")}");
        }
        
        return result.ToString();
    }

    /// <summary>
    /// Display formatted truth table
    /// </summary>
    public override string ToString()
    {
        var result = new StringBuilder();
        
        if (!string.IsNullOrEmpty(Expression))
        {
            result.AppendLine($"Expression: {Expression}");
        }
        
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
        foreach (var row in Rows)
        {
            result.Append("| ");
            foreach (var variable in Variables)
            {
                result.Append($"{(row.Variables[variable] ? "T" : "F")} | ");
            }
            result.AppendLine($"{(row.Result ? "T" : "F")} |");
        }
        
        return result.ToString();
    }
}
