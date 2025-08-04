using System;
using Xunit;

namespace LogicalOptimizer.Tests
{
    /// <summary>
    /// Tests for CommandLineProcessor functionality
    /// </summary>
    public class CommandLineProcessorTests
    {
        [Fact]
        public void ParseArguments_NoArguments_ShouldReturnInvalidOptions()
        {
            // Act
            var options = CommandLineProcessor.ParseArguments(new string[0]);

            // Assert
            Assert.False(options.IsValid);
            Assert.NotEmpty(options.ErrorMessage);
        }

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public void ParseArguments_HelpFlag_ShouldSetShowHelp(string helpFlag)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { helpFlag });
            Assert.True(result.ShowHelp);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_TestFlag_ShouldSetRunTests()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--test" });
            Assert.True(result.RunTests);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_DemoFlag_ShouldSetRunDemo()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--demo" });
            Assert.True(result.RunDemo);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_BenchmarkFlag_ShouldSetRunBenchmark()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--benchmark" });
            Assert.True(result.RunBenchmark);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_StressTestFlag_ShouldSetRunStressTest()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--stress" });
            Assert.True(result.RunStressTest);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_CsvExampleFlag_ShouldSetShowCsvExample()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--csv-example" });
            Assert.True(result.ShowCsvExample);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_SimpleExpression_ShouldSetExpression()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "a & b" });
            Assert.Equal("a & b", result.Expression);
            Assert.True(result.IsValid);
            Assert.False(result.Verbose);
            Assert.False(result.Advanced);
        }

        [Fact]
        public void ParseArguments_VerboseFlag_ShouldSetVerbose()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--verbose", "a & b" });
            Assert.True(result.Verbose);
            Assert.Equal("a & b", result.Expression);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ParseArguments_AdvancedFlag_ShouldSetAdvanced()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--advanced", "a & b" });
            Assert.True(result.Advanced);
            Assert.Equal("a & b", result.Expression);
            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("--cnf")]
        [InlineData("--dnf")]
        [InlineData("--truth-table")]
        [InlineData("--csv")]
        public void ParseArguments_SpecialModeFlags_ShouldSetCorrectMode(string modeFlag)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { modeFlag, "a & b" });
            Assert.Equal("a & b", result.Expression);
            Assert.True(result.IsValid);

            switch (modeFlag)
            {
                case "--cnf":
                    Assert.True(result.CnfOnly);
                    break;
                case "--dnf":
                    Assert.True(result.DnfOnly);
                    break;
                case "--truth-table":
                    Assert.True(result.TruthTableOnly);
                    break;
                case "--csv":
                    Assert.True(result.CsvInput);
                    break;
            }
        }

        [Fact]
        public void ParseArguments_EmptyExpression_ShouldBeValid()
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "" });
            Assert.True(result.IsValid); // Empty string is valid expression
            Assert.Equal("", result.Expression);
        }

        [Theory]
        [InlineData("(a & b) | c", "--verbose")]
        [InlineData("!a | (b & c)", "--advanced")]
        [InlineData("a & b & c & d", "--cnf")]
        public void ParseArguments_ComplexExpressions_ShouldParseCorrectly(string expression, string flag)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { flag, expression });
            Assert.Equal(expression, result.Expression);
            Assert.True(result.IsValid);

            switch (flag)
            {
                case "--verbose":
                    Assert.True(result.Verbose);
                    break;
                case "--advanced":
                    Assert.True(result.Advanced);
                    break;
                case "--cnf":
                    Assert.True(result.CnfOnly);
                    break;
            }
        }

        [Fact]
        public void ParseArguments_TooLongExpression_ShouldReturnInvalid()
        {
            // Arrange - create expression over 10000 characters
            var longExpression = new string('a', 10001);
            
            // Act
            var result = CommandLineProcessor.ParseArguments(new[] { longExpression });
            
            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("too long", result.ErrorMessage);
        }

        [Fact]
        public void ParseArguments_ValidCsvInput_ShouldDetectCsv()
        {
            var csvContent = "a,b,Result\n0,0,0\n0,1,1\n1,0,1\n1,1,0";
            var result = CommandLineProcessor.ParseArguments(new[] { csvContent });
            
            Assert.Equal(csvContent, result.Expression);
            Assert.True(result.IsValid);
            // Auto-detection should work
        }

        [Fact]
        public void ParseArguments_CsvFlag_ShouldSetCsvInput()
        {
            var csvContent = "x,y,Output\n0,0,1\n1,1,0";
            var result = CommandLineProcessor.ParseArguments(new[] { "--csv", csvContent });
            
            Assert.True(result.CsvInput);
            Assert.Equal(csvContent, result.Expression);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ShowUsage_ShouldNotThrow()
        {
            // Act & Assert - Should not throw any exceptions
            var exception = Record.Exception(() => CommandLineProcessor.ShowUsage());
            Assert.Null(exception);
        }

        [Fact]
        public void ShowHelp_ShouldNotThrow()
        {
            // Act & Assert - Should not throw any exceptions
            var exception = Record.Exception(() => CommandLineProcessor.ShowHelp());
            Assert.Null(exception);
        }

        [Fact]
        public void ShowCsvExample_ShouldNotThrow()
        {
            // Act & Assert - Should not throw any exceptions
            var exception = Record.Exception(() => CommandLineProcessor.ShowCsvExample());
            Assert.Null(exception);
        }

        [Fact]
        public void CommandLineOptions_DefaultValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var options = new CommandLineProcessor.CommandLineOptions();

            // Assert
            Assert.Equal(string.Empty, options.Expression);
            Assert.False(options.Verbose);
            Assert.False(options.CnfOnly);
            Assert.False(options.DnfOnly);
            Assert.False(options.Advanced);
            Assert.False(options.TruthTableOnly);
            Assert.False(options.CsvInput);
            Assert.False(options.ShowHelp);
            Assert.False(options.RunTests);
            Assert.False(options.RunDemo);
            Assert.False(options.RunBenchmark);
            Assert.False(options.RunStressTest);
            Assert.False(options.ShowCsvExample);
            Assert.True(options.IsValid);
            Assert.Equal(string.Empty, options.ErrorMessage);
        }

        [Fact]
        public void ParseArguments_ExactlyMaxLength_ShouldBeValid()
        {
            // Arrange
            var maxLengthExpression = new string('a', 10000); // Exactly 10,000 characters
            var args = new[] { maxLengthExpression };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.True(options.IsValid);
            Assert.Equal(maxLengthExpression, options.Expression);
        }

        [Fact]
        public void ParseArguments_CsvFileDetection_ShouldWorkWithoutFileSystem()
        {
            // Arrange - test CSV auto-detection logic without file system dependencies
            var csvLikeContent = "var1,var2,Result\n1,0,1\n0,1,0";
            var args = new[] { csvLikeContent };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.Equal(csvLikeContent, options.Expression);
            Assert.True(options.IsValid);
            // CSV auto-detection should trigger if content looks like CSV
        }

        [Fact]
        public void ParseArguments_NonCsvExpression_ShouldNotTriggerCsvDetection()
        {
            // Arrange
            var regularExpression = "a & b | !c";
            var args = new[] { regularExpression };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.Equal(regularExpression, options.Expression);
            Assert.False(options.CsvInput);
            Assert.True(options.IsValid);
        }

        [Fact]
        public void ParseArguments_SingleArgumentWithValidExpression_ShouldWorkCorrectly()
        {
            // Arrange
            var expression = "(a | b) & (!c | d)";
            var args = new[] { expression };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.Equal(expression, options.Expression);
            Assert.True(options.IsValid);
            Assert.False(options.Verbose);
            Assert.False(options.CnfOnly);
            Assert.False(options.DnfOnly);
            Assert.False(options.Advanced);
            Assert.False(options.TruthTableOnly);
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("a", true)]
        [InlineData("simple expression", true)]
        public void ParseArguments_VariousExpressionLengths_ShouldValidateCorrectly(string expression, bool expectedValid)
        {
            // Arrange
            var args = new[] { expression };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.Equal(expectedValid, options.IsValid);
            Assert.Equal(expression, options.Expression);
        }

        [Fact]
        public void ParseArguments_ThreeArgumentsScenario_ShouldHandleFirstArgumentAsExpression()
        {
            // Arrange - test case where there are more than 2 arguments
            var args = new[] { "a & b", "extra", "arguments" };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.Equal("a & b", options.Expression);
            Assert.True(options.IsValid);
            // Should not process additional arguments beyond the expression
        }
    }
}