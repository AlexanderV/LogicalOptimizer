using System;
using System.Collections.Generic;

namespace LogicalOptimizer;

/// <summary>
/// Handles output formatting and display based on command line options
/// </summary>
internal class OutputFormatter
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
            if (result.DebugInfo.Length > 0)
                Console.WriteLine($"\n{result.DebugInfo}");
            Console.WriteLine(result); // Full output with metrics
            Console.WriteLine($"Minimality: {result.MinimizationStatus}");
        }
        else if (options.CnfOnly)
        {
            Console.WriteLine(result.CNF);
        }
        else if (options.DnfOnly)
        {
            Console.WriteLine(result.DNF);
        }
        else if (options.AnfOnly)
        {
            DisplayAnf(result.Original);
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
        catch (ArgumentException ex)
        {
            // Invalid expression: report and fail via exit code — no stack trace
            // after an already-printed error message
            Console.Error.WriteLine($"Error generating truth table: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private void DisplayAnf(string expression)
    {
        try
        {
            var ast = new Parser(new Lexer(expression).Tokenize()).Parse();
            var anf = Transformations.ToAlgebraicNormalForm(ast);
            Console.WriteLine(AstFormatter.Format(anf));
        }
        catch (ArgumentException ex)
        {
            // Invalid expression or too many variables: report and fail via exit code
            Console.Error.WriteLine($"Error computing ANF: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private void DisplayAdvancedForms(OptimizationResult result)
    {
        var patternRecognizer = new PatternRecognizer();
        Console.WriteLine(patternRecognizer.GenerateAdvancedLogicalForms(result.Original));
    }

    private void DisplayStandardOutput(OptimizationResult result)
    {
        Console.WriteLine($"Original: {result.Original}");
        Console.WriteLine($"Optimized: {result.Optimized}");

        // Proof report — the key differentiator over a plain expression simplifier:
        // report WHAT was proven about the smaller expression, not just the expression.
        Console.WriteLine($"Equivalent: {(result.IsEquivalent() ? "proven" : "unverified")}");
        Console.WriteLine($"Minimality: {DescribeMinimality(result.MinimizationStatus)}");
        var originalLiterals = CliExpressionMetrics.TryCountLiterals(result.Original);
        var optimizedLiterals = CliExpressionMetrics.TryCountLiterals(result.Optimized);
        if (originalLiterals is { } before && optimizedLiterals is { } after)
            Console.WriteLine($"Cost: {before} -> {after} literals");

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

    /// <summary>Friendly rendering of the minimality provenance for the standard proof report.</summary>
    private static string DescribeMinimality(MinimizationStatus status)
    {
        return status switch
        {
            MinimizationStatus.MinimalProven => "proven",
            MinimizationStatus.BudgetExceeded => "unproven (budget exceeded)",
            _ => "heuristic"
        };
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
