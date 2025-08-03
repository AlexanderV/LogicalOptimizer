using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TruthTableTestUpdater;

/// <summary>
/// Utility for automatic test updates with addition of truth table verification
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        string testsFilePath = @"d:\Prototype\LogicalOptimizer\LogicalOptimizer.Tests\Tests.cs";
        
        if (!File.Exists(testsFilePath))
        {
            Console.WriteLine($"Test file not found: {testsFilePath}");
            return;
        }

        string content = File.ReadAllText(testsFilePath);
        
        // Pattern for finding optimization methods with Assert.Equal
        string pattern = @"(public void \w*Optimizer\w*_\w+_\w+\([^)]+\))\s*\{\s*//\s*Act\s*var result = _optimizer\.OptimizeExpression\(input\);\s*//\s*Assert\s*Assert\.Equal\(expected, result\.Optimized\);\s*\}";
        
        // Replace with new code using TruthTableAssert
        string replacement = @"$1
        {
            // Act & Assert
            TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
        }";

        string updatedContent = Regex.Replace(content, pattern, replacement, RegexOptions.Multiline | RegexOptions.Singleline);
        
        if (updatedContent != content)
        {
            File.WriteAllText(testsFilePath, updatedContent);
            Console.WriteLine("Tests successfully updated with truth table verification!");
        }
        else
        {
            Console.WriteLine("Pattern not found or file already updated.");
        }
    }
}
