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
        var (variables, truthRows) = ParseCsvRows(csvContent);
        return ConvertToDnfExpression(truthRows, variables);
    }

    /// <summary>
    ///     Parse a (possibly partial) CSV truth table. Rows absent from the CSV are
    ///     reported as don't-care minterms. Bit convention: bit j of a minterm index
    ///     is the value of Variables[j], variables sorted alphabetically.
    /// </summary>
    public static PartialTruthTable ParseCsvToPartialTable(string csvContent)
    {
        var (variableNames, truthRows) = ParseCsvRows(csvContent);
        var variables = variableNames.OrderBy(v => v).ToList();

        var onSet = new HashSet<int>();
        var specified = new HashSet<int>();
        foreach (var row in truthRows)
        {
            var minterm = 0;
            for (var j = 0; j < variables.Count; j++)
                if (row.Variables[variables[j]])
                    minterm |= 1 << j;

            specified.Add(minterm);
            if (row.Result) onSet.Add(minterm);
        }

        var dontCareSet = new HashSet<int>();
        var totalMinterms = 1 << variables.Count;
        for (var m = 0; m < totalMinterms; m++)
            if (!specified.Contains(m))
                dontCareSet.Add(m);

        return new PartialTruthTable(variables, onSet, dontCareSet);
    }

    /// <summary>
    ///     Parse a CSV table with several output columns (e.g. a,b,Sum,Carry). Every column
    ///     listed in <paramref name="outputColumns" /> is an output; all other columns are
    ///     input variables. Rows absent from the CSV count as don't-care for every output.
    /// </summary>
    public static MultiOutputTable ParseCsvToMultiOutputTable(string csvContent,
        IReadOnlyCollection<string> outputColumns)
    {
        if (outputColumns.Count == 0)
            throw new ArgumentException("At least one output column must be specified", nameof(outputColumns));

        if (string.IsNullOrWhiteSpace(csvContent))
            throw new ArgumentException("CSV content cannot be empty", nameof(csvContent));

        csvContent = csvContent.Replace("\\n", "\n");
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();

        if (lines.Length < 2)
            throw new ArgumentException("CSV must have at least header and one data row");

        var header = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var outputSet = new HashSet<string>(outputColumns, StringComparer.OrdinalIgnoreCase);
        var variableColumns = new List<(string Name, int Index)>();
        var outputColumnList = new List<(string Name, int Index)>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < header.Length; i++)
        {
            var columnName = header[i];
            if (!seenNames.Add(columnName))
                throw new ArgumentException($"Duplicate column '{columnName}' in CSV header");

            if (outputSet.Contains(columnName))
            {
                outputColumnList.Add((columnName, i));
            }
            else
            {
                ValidateVariableName(columnName, i + 1);
                variableColumns.Add((columnName, i));
            }
        }

        var missing = outputSet.Where(o => !outputColumnList.Any(c =>
            c.Name.Equals(o, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Output column(s) not found in CSV header: {string.Join(", ", missing)}");

        if (variableColumns.Count == 0)
            throw new ArgumentException("CSV must have at least one variable column");
        if (variableColumns.Count > TruthTable.MaxVariables)
            throw new ArgumentException(
                $"CSV has {variableColumns.Count} variable columns; maximum supported is {TruthTable.MaxVariables}");

        var variables = variableColumns.Select(v => v.Name).OrderBy(v => v).ToList();
        var onSets = outputColumnList.ToDictionary(o => o.Name, _ => new HashSet<int>());
        var specified = new HashSet<int>();
        var rowValues = new Dictionary<int, Dictionary<string, bool>>();

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var values = lines[lineIndex].Split(',').Select(v => v.Trim()).ToArray();
            if (values.Length != header.Length)
                throw new ArgumentException(
                    $"Row {lineIndex + 1} has {values.Length} columns, expected {header.Length}");

            var minterm = 0;
            foreach (var (name, index) in variableColumns)
                if (ParseBooleanValue(values[index], lineIndex + 1, name))
                    minterm |= 1 << variables.IndexOf(name);

            var outputs = outputColumnList.ToDictionary(
                o => o.Name,
                o => ParseBooleanValue(values[o.Index], lineIndex + 1, o.Name));

            if (specified.Contains(minterm))
            {
                if (outputColumnList.Any(o => onSets[o.Name].Contains(minterm) != outputs[o.Name]))
                    throw new ArgumentException(
                        $"Row {lineIndex + 1} contradicts an earlier row: same variable values, different outputs");
                continue;
            }

            specified.Add(minterm);
            rowValues[minterm] = outputs;
            foreach (var (name, _) in outputColumnList)
                if (outputs[name])
                    onSets[name].Add(minterm);
        }

        var dontCareSet = new HashSet<int>();
        for (var m = 0; m < 1 << variables.Count; m++)
            if (!specified.Contains(m))
                dontCareSet.Add(m);

        return new MultiOutputTable(
            variables,
            outputColumnList.Select(o => new MultiOutputFunction(o.Name, onSets[o.Name], dontCareSet)).ToList());
    }

    private static (List<string> Variables, List<TruthRow> Rows) ParseCsvRows(string csvContent)
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
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < header.Length; i++)
        {
            var columnName = header[i];
            if (IsResultColumnName(columnName))
            {
                if (resultColumnIndex != -1)
                    throw new ArgumentException("CSV must have exactly one 'Result', 'Output', or 'Value' column");
                resultColumnIndex = i;
            }
            else
            {
                ValidateVariableName(columnName, i + 1);
                if (!seenNames.Add(columnName))
                    throw new ArgumentException($"Duplicate variable column '{columnName}' in CSV header");
                variableColumns.Add((columnName, i));
            }
        }

        if (resultColumnIndex == -1)
            throw new ArgumentException("CSV must have a 'Result', 'Output', or 'Value' column");

        if (variableColumns.Count == 0)
            throw new ArgumentException("CSV must have at least one variable column");

        if (variableColumns.Count > TruthTable.MaxVariables)
            throw new ArgumentException(
                $"CSV has {variableColumns.Count} variable columns; maximum supported is {TruthTable.MaxVariables}");

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

            var duplicate = truthRows.FirstOrDefault(existing =>
                existing.Variables.All(kvp => row.Variables[kvp.Key] == kvp.Value));
            if (duplicate != null)
            {
                if (duplicate.Result != row.Result)
                    throw new ArgumentException(
                        $"Row {lineIndex + 1} contradicts an earlier row: same variable values, different result");
                continue; // identical duplicate row, ignore
            }

            truthRows.Add(row);
        }

        return (variableColumns.Select(v => v.name).ToList(), truthRows);
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

        // Should contain a 'Result', 'Output', or 'Value' column (whole-token match,
        // consistent with the parser's header handling)
        var hasResultColumn = lines[0].Split(',')
            .Select(h => h.Trim())
            .Any(h => h.Equals("Result", StringComparison.OrdinalIgnoreCase) ||
                      h.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
                      h.Equals("Value", StringComparison.OrdinalIgnoreCase));
        if (!hasResultColumn)
            return false;

        // At least one data line should contain commas
        return lines.Skip(1).Any(line => line.Contains(','));
    }

    private static bool IsResultColumnName(string columnName)
    {
        return columnName.Equals("Result", StringComparison.OrdinalIgnoreCase) ||
               columnName.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
               columnName.Equals("Value", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateVariableName(string name, int columnNumber)
    {
        if (name.Length == 0)
            throw new ArgumentException($"CSV header column {columnNumber} is empty");

        if (name == "0" || name == "1")
            throw new ArgumentException(
                $"CSV variable name '{name}' (column {columnNumber}) conflicts with a boolean constant");

        // Same grammar as the lexer: [letter_][letter/digit/_]*
        if (!(char.IsLetter(name[0]) || name[0] == '_') ||
            !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new ArgumentException(
                $"CSV variable name '{name}' (column {columnNumber}) is not a valid identifier");
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

        // Tautology only when every possible variable combination is present and true
        if (trueCases.Count == 1 << variables.Count) return "1";

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

/// <summary>
///     A partially specified single-output truth table parsed from CSV. Rows absent from
///     the CSV are don't-care minterms. Bit convention: bit j of a minterm index is the
///     value of Variables[j], variables sorted alphabetically.
/// </summary>
public sealed class PartialTruthTable
{
    internal PartialTruthTable(List<string> variables, HashSet<int> onSet, HashSet<int> dontCareSet)
    {
        Variables = variables;
        OnSet = onSet;
        DontCareSet = dontCareSet;
    }

    /// <summary>Sorted input variable names; bit j of a minterm is Variables[j].</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Minterms where the function is 1.</summary>
    public IReadOnlyCollection<int> OnSet { get; }

    /// <summary>Minterms the CSV leaves unspecified (free for the minimizer).</summary>
    public IReadOnlyCollection<int> DontCareSet { get; }
}

/// <summary>A multi-output truth table: shared inputs, one ON-set per output.</summary>
public sealed class MultiOutputTable
{
    internal MultiOutputTable(List<string> variables, List<MultiOutputFunction> outputs)
    {
        Variables = variables;
        Outputs = outputs;
    }

    /// <summary>Sorted input variable names; bit j of a minterm is Variables[j].</summary>
    public IReadOnlyList<string> Variables { get; }

    public IReadOnlyList<MultiOutputFunction> Outputs { get; }
}

/// <summary>One output of a multi-output table. The don't-care set is shared (unspecified rows).</summary>
public sealed class MultiOutputFunction
{
    internal MultiOutputFunction(string name, HashSet<int> onSet, HashSet<int> dontCareSet)
    {
        Name = name;
        OnSet = onSet;
        DontCareSet = dontCareSet;
    }

    public string Name { get; }
    public IReadOnlyCollection<int> OnSet { get; }
    public IReadOnlyCollection<int> DontCareSet { get; }
}
