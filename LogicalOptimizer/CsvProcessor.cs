using System;
using System.IO;

namespace LogicalOptimizer;

/// <summary>
/// Handles CSV input processing and validation
/// </summary>
public class CsvProcessor
{
    public static string ProcessCsvInput(string expression)
    {
        Console.WriteLine("Detected CSV truth table input");

        string csvExpression;
        if (File.Exists(expression))
        {
            Console.WriteLine($"Reading CSV from file: {expression}");
            csvExpression = CsvTruthTableParser.ParseCsvFileToExpression(expression);
        }
        else
        {
            Console.WriteLine("Parsing CSV content directly");
            csvExpression = CsvTruthTableParser.ParseCsvToExpression(expression);
        }

        Console.WriteLine($"Generated expression from CSV: {csvExpression}");
        Console.WriteLine();

        return csvExpression;
    }
}
