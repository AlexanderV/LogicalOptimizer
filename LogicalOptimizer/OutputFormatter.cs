using System;
using System.Collections.Generic;

namespace LogicalOptimizer;

/// <summary>
/// Handles output formatting and display based on command line options
/// </summary>
public class OutputFormatter
{
    private readonly AdvancedPatternDetector _patternDetector;

    public OutputFormatter()
    {
        _patternDetector = new AdvancedPatternDetector();
    }

    public void DisplayResult(OptimizationResult result, CommandLineProcessor.CommandLineOptions options)
    {
        if (options.TruthTableOnly)
        {
            DisplayTruthTableOnly(result.Original);
        }
        else if (options.Verbose)
        {
            Console.WriteLine(result); // Full output with metrics
        }
        else if (options.CnfOnly)
        {
            Console.WriteLine(result.CNF);
        }
        else if (options.DnfOnly)
        {
            Console.WriteLine(result.DNF);
        }
        else if (options.Advanced)
        {
            DisplayAdvancedForms(result);
        }
        else
        {
            DisplayStandardOutput(result);
        }
    }

    private void DisplayTruthTableOnly(string expression)
    {
        try
        {
            var truthTable = TruthTable.Generate(expression);
            Console.WriteLine(truthTable);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error generating truth table: {ex.Message}");
            throw;
        }
    }

    private void DisplayAdvancedForms(OptimizationResult result)
    {
        var patternRecognizer = new PatternRecognizer();
        var originalAdvanced = patternRecognizer.GenerateAdvancedLogicalForms(result.Original);
        var optimizedAdvanced = patternRecognizer.GenerateAdvancedLogicalForms(result.Optimized);

        // Use whichever found actual optimizations (not just "Optimized: ...")
        string advancedForms;
        if (!originalAdvanced.StartsWith("Optimized:"))
            advancedForms = originalAdvanced;
        else if (!optimizedAdvanced.StartsWith("Optimized:"))
            advancedForms = optimizedAdvanced;
        else
            advancedForms = optimizedAdvanced; // Default to optimized result

        Console.WriteLine(advancedForms);
    }

    private void DisplayStandardOutput(OptimizationResult result)
    {
        Console.WriteLine($"Original: {result.Original}");
        Console.WriteLine($"Optimized: {result.Optimized}");
        Console.WriteLine($"CNF: {result.CNF}");
        Console.WriteLine($"DNF: {result.DNF}");
        Console.WriteLine($"Variables: [{string.Join(", ", result.Variables)}]");

        // Check for advanced forms (XOR, IMP, etc.)
        var advancedFromOptimized = _patternDetector.ConvertToAdvancedForms(result.Optimized);
        
        // Only show advanced patterns if they are found in the optimized expression
        // and are meaningfully different from the optimized form
        if (!string.IsNullOrEmpty(advancedFromOptimized) && 
            advancedFromOptimized != result.Optimized &&
            advancedFromOptimized.Length < result.Optimized.Length * 2) // Don't show if much longer
        {
            Console.WriteLine($"Advanced: {advancedFromOptimized}");
        }

        DisplayTruthTableIfSmall(result);
    }

    private void DisplayTruthTableIfSmall(OptimizationResult result)
    {
        // Show truth table only for small expressions (≤6 variables) to avoid performance issues
        if (result.Variables.Count <= 6)
        {
            try
            {
                var truthTable = TruthTable.Generate(result.Original);
                Console.WriteLine();
                Console.WriteLine("Truth Table:");
                Console.WriteLine(truthTable);
            }
            catch (Exception)
            {
                Console.WriteLine($"Truth table skipped: too many variables ({result.Variables.Count})");
            }
        }
        else
        {
            Console.WriteLine($"\nTruth table skipped: too many variables ({result.Variables.Count})");
        }
    }
}
