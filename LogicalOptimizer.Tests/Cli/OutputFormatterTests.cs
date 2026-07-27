using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the OutputFormatter component - output formatting and display
/// </summary>
public class OutputFormatterTests
{
    private readonly OutputFormatter _formatter;

    public OutputFormatterTests()
    {
        _formatter = new OutputFormatter();
    }

    [Fact]
    public void DisplayResult_StandardOutput_ShouldDisplayAllFields()
    {
        // Arrange
        var result = CreateSampleOptimizationResult();
        var options = new CommandLineProcessor.CommandLineOptions();

        // Capture console output
        var output = CaptureConsoleOutput(() =>
        {
            _formatter.DisplayResult(result, options);
        });

        // Assert
        Assert.Contains("Original:", output);
        Assert.Contains("Optimized:", output);
        Assert.Contains("CNF:", output);
        Assert.Contains("DNF:", output);
        Assert.Contains("Variables:", output);
        Assert.Contains("a & b", output);
    }

    [Fact]
    public void DisplayResult_CnfOnlyMode_ShouldDisplayOnlyCnf()
    {
        // Arrange
        var result = CreateSampleOptimizationResult();
        var options = new CommandLineProcessor.CommandLineOptions { CnfOnly = true };

        // Capture console output
        var output = CaptureConsoleOutput(() =>
        {
            _formatter.DisplayResult(result, options);
        });

        // Assert: prints the CNF field specifically (distinct from DNF), so a swap fails
        Assert.Equal($"(a | b) & c{Environment.NewLine}", output);
    }

    [Fact]
    public void DisplayResult_DnfOnlyMode_ShouldDisplayOnlyDnf()
    {
        // Arrange
        var result = CreateSampleOptimizationResult();
        var options = new CommandLineProcessor.CommandLineOptions { DnfOnly = true };

        // Capture console output
        var output = CaptureConsoleOutput(() =>
        {
            _formatter.DisplayResult(result, options);
        });

        // Assert: prints the DNF field specifically (distinct from CNF), so a swap fails
        Assert.Equal($"a | b{Environment.NewLine}", output);
    }

    [Fact]
    public void DisplayResult_TruthTableOnlyMode_ShouldDisplayTruthTable()
    {
        // Arrange
        var result = CreateSampleOptimizationResult();
        var options = new CommandLineProcessor.CommandLineOptions { TruthTableOnly = true };

        // Capture console output
        var output = CaptureConsoleOutput(() =>
        {
            _formatter.DisplayResult(result, options);
        });

        // Assert: pin the real truth table of "a & b" — the header row (column labels)
        // and specific data rows — instead of the tautological Contains("0")/Contains("1").
        Assert.Contains("| a | b | Result |", output); // header with variable labels
        Assert.Contains("| 0 | 0 | 0      |", output); // a=0,b=0 -> 0
        Assert.Contains("| 1 | 1 | 1      |", output); // a=1,b=1 -> 1 (only satisfying row)
    }

    [Fact]
    public void DisplayResult_StandardMode_WithSmallExpression_ShouldIncludeTruthTable()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            CNF = "a & b",
            DNF = "a & b",
            Variables = new List<string> { "a", "b" }, // 2 variables - should show truth table
            Metrics = new OptimizationMetrics()
        };
        var options = new CommandLineProcessor.CommandLineOptions();

        // Capture console output
        var output = CaptureConsoleOutput(() =>
        {
            _formatter.DisplayResult(result, options);
        });

        // Assert
        Assert.Contains("Truth Table:", output);
    }

    [Fact]
    public void DisplayResult_StandardMode_WithLargeExpression_ShouldSkipTruthTable()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "a & b & c & d & e & f & g",
            Optimized = "a & b & c & d & e & f & g",
            CNF = "a & b & c & d & e & f & g",
            DNF = "a & b & c & d & e & f & g",
            Variables = new List<string> { "a", "b", "c", "d", "e", "f", "g" }, // 7 variables - should skip
            Metrics = new OptimizationMetrics()
        };
        var options = new CommandLineProcessor.CommandLineOptions();

        // Capture console output
        var output = CaptureConsoleOutput(() =>
        {
            _formatter.DisplayResult(result, options);
        });

        // Assert
        Assert.Contains("Truth table skipped: too many variables", output);
    }

    [Fact]
    public void DisplayResult_TruthTableOnlyMode_WithInvalidExpression_FailsGracefully()
    {
        // Arrange
        var result = new OptimizationResult
        {
            Original = "invalid @#$ expression",
            Optimized = "invalid @#$ expression",
            CNF = "invalid @#$ expression",
            DNF = "invalid @#$ expression",
            Variables = new List<string>(),
            Metrics = new OptimizationMetrics()
        };
        var options = new CommandLineProcessor.CommandLineOptions { TruthTableOnly = true };

        // Act & Assert: invalid input must NOT crash with a stack trace — the error is
        // reported once on stderr and the process failure is signaled via ExitCode
        var previousExitCode = Environment.ExitCode;
        try
        {
            CaptureConsoleOutput(() => _formatter.DisplayResult(result, options));
            Assert.Equal(1, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    private static OptimizationResult CreateSampleOptimizationResult()
    {
        // CNF and DNF are DISTINCT so a field mix-up (printing DNF where CNF is expected,
        // or vice versa) is caught by CnfOnly/DnfOnly. Original stays "a & b" so the
        // truth-table-only path renders the a & b table.
        return new OptimizationResult
        {
            Original = "a & b",
            Optimized = "a & b",
            CNF = "(a | b) & c",
            DNF = "a | b",
            Variables = new List<string> { "a", "b" },
            Metrics = new OptimizationMetrics()
        };
    }

    private static string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        try
        {
            using var stringWriter = new StringWriter();
            Console.SetOut(stringWriter);
            action();
            return stringWriter.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
