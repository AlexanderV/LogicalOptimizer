using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Parser for CSV truth tables that converts them to boolean expressions
/// </summary>
public class CsvTruthTableParser
{
    /// <summary>
    ///     Parse CSV truth table and convert to DNF (Disjunctive Normal Form) expression
    /// </summary>
    public static string ParseCsvToExpression(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            throw new ArgumentException("CSV content cannot be empty", nameof(csvContent));

        // Replace literal \n with actual newlines
        csvContent = csvContent.Replace("\\n", "\n");

        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();

        if (lines.Length < 2)
            throw new ArgumentException("CSV must have at least header and one data row");

        // Parse header
        var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var resultColumnIndex = -1;
        var variableColumns = new List<(string name, int index)>();

        for (var i = 0; i < header.Length; i++)
        {
            var columnName = header[i];
            if (columnName.Equals("Result", StringComparison.OrdinalIgnoreCase) ||
                columnName.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
                columnName.Equals("Value", StringComparison.OrdinalIgnoreCase))
                resultColumnIndex = i;
            else
                variableColumns.Add((columnName, i));
        }

        if (resultColumnIndex == -1)
            throw new ArgumentException("CSV must have a 'Result', 'Output', or 'Value' column");

        if (variableColumns.Count == 0)
            throw new ArgumentException("CSV must have at least one variable column");

        // Parse data rows
        var truthRows = new List<TruthRow>();
        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var values = lines[lineIndex].Split(',').Select(v => v.Trim()).ToArray();

            if (values.Length != header.Length)
                throw new ArgumentException(
                    $"Row {lineIndex + 1} has {values.Length} columns, expected {header.Length}");

            var row = new TruthRow();

            // Parse variable values
            foreach (var (name, index) in variableColumns)
            {
                var value = ParseBooleanValue(values[index], lineIndex + 1, name);
                row.Variables[name] = value;
            }

            // Parse result value
            row.Result = ParseBooleanValue(values[resultColumnIndex], lineIndex + 1, "Result");
            truthRows.Add(row);
        }

        // Convert to DNF expression
        return ConvertToDnfExpression(truthRows, variableColumns.Select(v => v.name).ToList());
    }

    /// <summary>
    ///     Parse CSV truth table from file path
    /// </summary>
    public static string ParseCsvFileToExpression(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"CSV file not found: {filePath}");

        var csvContent = File.ReadAllText(filePath);
        return ParseCsvToExpression(csvContent);
    }

    /// <summary>
    ///     Check if input looks like CSV content (contains commas and multiple lines)
    /// </summary>
    public static bool LooksLikeCsv(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Replace literal \n with actual newlines
        var normalizedInput = input.Replace("\\n", "\n");
        var lines = normalizedInput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Must have at least 2 lines (header + data)
        if (lines.Length < 2)
            return false;

        // First line should contain commas (header)
        if (!lines[0].Contains(','))
            return false;

        // Should contain 'Result', 'Output', or 'Value' column
        var header = lines[0].ToLower();
        if (!header.Contains("result") && !header.Contains("output") && !header.Contains("value"))
            return false;

        // At least one data line should contain commas
        return lines.Skip(1).Any(line => line.Contains(','));
    }

    private static bool ParseBooleanValue(string value, int row, string column)
    {
        value = value.Trim().ToLower();

        return value switch
        {
            "1" or "true" or "t" or "yes" or "y" => true,
            "0" or "false" or "f" or "no" or "n" => false,
            _ => throw new ArgumentException(
                $"Invalid boolean value '{value}' in row {row}, column '{column}'. Expected: 0/1, true/false, t/f, yes/no, y/n")
        };
    }

    private static string ConvertToDnfExpression(List<TruthRow> truthRows, List<string> variables)
    {
        var trueCases = truthRows.Where(row => row.Result).ToList();

        if (trueCases.Count == 0) return "0"; // Always false (contradiction)

        if (trueCases.Count == truthRows.Count) return "1"; // Always true (tautology)

        var terms = new List<string>();

        foreach (var trueCase in trueCases)
        {
            var literals = new List<string>();

            foreach (var variable in variables.OrderBy(v => v))
                if (trueCase.Variables[variable])
                    literals.Add(variable);
                else
                    literals.Add($"!{variable}");

            if (literals.Count == 1)
                terms.Add(literals[0]);
            else
                terms.Add($"({string.Join(" & ", literals)})");
        }

        if (terms.Count == 1) return terms[0];

        return string.Join(" | ", terms);
    }

    /// <summary>
    ///     Generate example CSV content for demonstration
    /// </summary>
    public static string GenerateExampleCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("a,b,Result");
        sb.AppendLine("0,0,0");
        sb.AppendLine("0,1,1");
        sb.AppendLine("1,0,1");
        sb.AppendLine("1,1,0");
        return sb.ToString();
    }

    private class TruthRow
    {
        public Dictionary<string, bool> Variables { get; } = new();
        public bool Result { get; set; }
    }
}