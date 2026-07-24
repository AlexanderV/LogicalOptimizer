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

        [Theory]
        [InlineData("a & b")]
        [InlineData("(a | b) & (!c | d)")]
        public void ParseArguments_SimpleExpression_ShouldSetExpressionOnly(string expression)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { expression });
            Assert.Equal(expression, result.Expression);
            Assert.True(result.IsValid);
            Assert.False(result.Verbose);
            Assert.False(result.Advanced);
            Assert.False(result.CnfOnly);
            Assert.False(result.DnfOnly);
            Assert.False(result.TruthTableOnly);
        }

        [Theory]
        [InlineData("a & b")]
        [InlineData("(a & b) | c")]
        public void ParseArguments_VerboseFlag_ShouldSetVerbose(string expression)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--verbose", expression });
            Assert.True(result.Verbose);
            Assert.Equal(expression, result.Expression);
            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("a & b")]
        [InlineData("!a | (b & c)")]
        public void ParseArguments_AdvancedFlag_ShouldSetAdvanced(string expression)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { "--advanced", expression });
            Assert.True(result.Advanced);
            Assert.Equal(expression, result.Expression);
            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("--cnf", "a & b & c & d")]
        [InlineData("--dnf", "a & b")]
        [InlineData("--truth-table", "a & b")]
        [InlineData("--csv", "a & b")]
        public void ParseArguments_SpecialModeFlags_ShouldSetCorrectMode(string modeFlag, string expression)
        {
            var result = CommandLineProcessor.ParseArguments(new[] { modeFlag, expression });
            Assert.Equal(expression, result.Expression);
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
            // Inline CSV content (header with Result column + data rows) is auto-detected
            // via CsvTruthTableParser.LooksLikeCsv even without the --csv flag
            var csvContent = "a,b,Result\n0,0,0\n0,1,1\n1,0,1\n1,1,0";
            var result = CommandLineProcessor.ParseArguments(new[] { csvContent });

            Assert.Equal(csvContent, result.Expression);
            Assert.True(result.IsValid);
            Assert.True(result.CsvInput);
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
        public void ParseArguments_MultipleExpressions_ShouldBeInvalid()
        {
            // Arrange - more than one positional (non-flag) argument is an error
            var args = new[] { "a & b", "extra", "arguments" };

            // Act
            var options = CommandLineProcessor.ParseArguments(args);

            // Assert
            Assert.False(options.IsValid);
            Assert.Contains("Multiple expressions", options.ErrorMessage);
        }

        [Fact]
        public void ParseArguments_CnfModeFlag_Parses()
        {
            var options = CommandLineProcessor.ParseArguments(new[] { "--cnf-mode=tseitin", "a & b" });
            Assert.True(options.IsValid);
            Assert.Equal(CnfMode.Tseitin, options.CnfMode);

            var invalid = CommandLineProcessor.ParseArguments(new[] { "--cnf-mode=banana", "a & b" });
            Assert.False(invalid.IsValid);
        }

        [Fact]
        public void ParseArguments_OutputsFlag_Parses()
        {
            const string halfAdderCsv = "a,b,Sum,Carry\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1";
            var options = CommandLineProcessor.ParseArguments(new[] { "--outputs=Sum,Carry", halfAdderCsv });

            Assert.True(options.IsValid);
            Assert.Equal(new List<string> { "Sum", "Carry" }, options.OutputColumns);

            var empty = CommandLineProcessor.ParseArguments(new[] { "--outputs=", "x" });
            Assert.False(empty.IsValid);
        }
    }
}
