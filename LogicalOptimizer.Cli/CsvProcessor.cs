using System;
using System.IO;
using LogicalOptimizer.Optimizers;

namespace LogicalOptimizer;

/// <summary>
/// Handles CSV input processing and validation
/// </summary>
internal class CsvProcessor
{
    public static string ProcessCsvInput(string expression)
    {
        // Status goes to stderr so stdout stays machine-parseable (--cnf, --dnf, ...)
        Console.Error.WriteLine("Detected CSV truth table input");

        string csvContent;
        if (File.Exists(expression))
        {
            Console.Error.WriteLine($"Reading CSV from file: {expression}");
            csvContent = File.ReadAllText(expression);
        }
        else
        {
            Console.Error.WriteLine("Parsing CSV content directly");
            csvContent = expression;
        }

        var csvExpression = BuildExpression(csvContent);

        Console.Error.WriteLine($"Generated expression from CSV: {csvExpression}");
        Console.Error.WriteLine();

        return csvExpression;
    }

    /// <summary>
    ///     Multi-output minimization: every declared output column is minimized over the
    ///     shared inputs (unspecified rows are don't-cares for all outputs). Returns one
    ///     (name, minimal expression) pair per output.
    /// </summary>
    public static List<(string Name, string Expression)> ProcessCsvMultiOutput(string input,
        IReadOnlyCollection<string> outputColumns)
    {
        var csvContent = File.Exists(input) ? File.ReadAllText(input) : input;
        var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(csvContent, outputColumns);
        var qmBudget = table.Variables.Count <= PerformanceValidator.EXACT_GUARANTEE_VARIABLES
            ? (long?)null
            : PerformanceValidator.QM_PAIR_COMPARISON_LIMIT;

        try
        {
            // Joint minimization: independent minimal covers, then PLA-style cube sharing
            var commutativity = new CommutativityOptimizer();
            return MultiOutputMinimizer
                .Minimize(table, qmBudget, ResourceBudget.DefaultCoverStepLimit,
                    canonicalize: node => commutativity.Optimize(node))
                .Select(r => (r.Name, r.Expression.ToString()))
                .ToList();
        }
        catch (InvalidOperationException)
        {
            // Budget exceeded on a dense output: fall back to the raw specified rows
            Console.Error.WriteLine("Exact multi-output minimization budget exceeded; using raw rows");
            return table.Outputs
                .Select(o => (o.Name, BuildRawSop(table.Variables, o.OnSet).ToString()))
                .ToList();
        }
    }

    private static AstNode BuildRawSop(IReadOnlyList<string> variables, IReadOnlyCollection<int> onSet)
    {
        if (onSet.Count == 0) return ConstantNode.False;

        AstNode? sop = null;
        foreach (var minterm in onSet.OrderBy(m => m))
        {
            AstNode? term = null;
            for (var j = 0; j < variables.Count; j++)
            {
                AstNode literal = (minterm & (1 << j)) != 0
                    ? new VariableNode(variables[j])
                    : new NotNode(new VariableNode(variables[j]));
                term = term == null ? literal : new AndNode(term, literal);
            }

            sop = sop == null ? term : new OrNode(sop, term!);
        }

        return sop!;
    }

    private static string BuildExpression(string csvContent)
    {
        var (variables, onSet, dontCareSet) = CsvTruthTableParser.ParseCsvToPartialTable(csvContent);

        // Incomplete tables: unspecified rows are don't-cares, which the exact
        // minimizer can exploit for a cheaper cover
        if (dontCareSet.Count > 0 && variables.Count <= PerformanceValidator.MAX_EXACT_MINIMIZATION_VARIABLES)
            try
            {
                Console.Error.WriteLine($"Unspecified rows treated as don't-care: {dontCareSet.Count}");
                var minimal = new CommutativityOptimizer()
                    .Optimize(TruthTableMinimizer.MinimalSop(variables, onSet, dontCareSet,
                        PerformanceValidator.QM_PAIR_COMPARISON_LIMIT));
                return minimal.ToString();
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine("Exact minimization budget exceeded; using the specified rows directly");
            }

        return CsvTruthTableParser.ParseCsvToExpression(csvContent);
    }
}
