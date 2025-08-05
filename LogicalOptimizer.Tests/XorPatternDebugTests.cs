using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace LogicalOptimizer.Tests
{
    public class XorPatternDebugTests
    {
        private readonly ITestOutputHelper _output;

        public XorPatternDebugTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Debug_XorPatternDetection()
        {
            _output.WriteLine("=== Testing XOR Pattern Detection ===");
            
            var result = new OptimizationResult
            {
                Original = "(a & !b) | (!a & b)",
                Optimized = "(a & !b) | (!a & b)",
                CNF = "(a & !b) | (!a & b)",
                DNF = "(a & !b) | (!a & b)",
                Variables = new List<string> { "a", "b" },
                Metrics = new OptimizationMetrics()
            };
            var options = new CommandLineProcessor.CommandLineOptions();
            
            var formatter = new OutputFormatter();
            
            // Capture output
            var originalOut = Console.Out;
            using var stringWriter = new System.IO.StringWriter();
            Console.SetOut(stringWriter);
            
            formatter.DisplayResult(result, options);
            
            Console.SetOut(originalOut);
            var output = stringWriter.ToString();
            
            _output.WriteLine("--- Formatter Output ---");
            _output.WriteLine(output);
            _output.WriteLine("--- End Output ---");
            
            _output.WriteLine("\n=== Testing AdvancedPatternDetector directly ===");
            var detector = new AdvancedPatternDetector();
            var advancedForm = detector.ConvertToAdvancedForms("(a & !b) | (!a & b)");
            _output.WriteLine($"ConvertToAdvancedForms result: '{advancedForm}'");
            
            // Parse and test individual methods
            var lexer = new Lexer("(a & !b) | (!a & b)");
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();
            
            var xorPattern = detector.DetectXorPattern(ast);
            _output.WriteLine($"DetectXorPattern result: '{xorPattern}'");
            
            var impPattern = detector.DetectImplicationPattern(ast);
            _output.WriteLine($"DetectImplicationPattern result: '{impPattern}'");
            
            // Check what specifically is in the output
            _output.WriteLine($"\nContains 'Advanced:': {output.Contains("Advanced:")}");
            _output.WriteLine($"Contains 'Original:': {output.Contains("Original:")}");
            _output.WriteLine($"Contains 'Optimized:': {output.Contains("Optimized:")}");
        }
    }
}
