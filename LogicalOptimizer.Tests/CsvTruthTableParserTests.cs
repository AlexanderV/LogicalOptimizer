using System;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Tests for the CsvTruthTableParser component - CSV truth table parsing
/// </summary>
public class CsvTruthTableParserTests
{
    [Fact]
    public void ParseCsvToExpression_SimpleAndGate_ShouldReturnCorrectExpression()
    {
        // Arrange
        string csvContent = @"a,b,Result
0,0,0
0,1,0
1,0,0
1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("&", result);
    }

    [Fact]
    public void ParseCsvToExpression_SimpleOrGate_ShouldReturnCorrectExpression()
    {
        // Arrange
        string csvContent = @"x,y,Result
0,0,0
0,1,1
1,0,1
1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("x", result);
        Assert.Contains("y", result);
        Assert.Contains("|", result);
    }

    [Fact]
    public void ParseCsvToExpression_SingleVariable_ShouldReturnVariableName()
    {
        // Arrange
        string csvContent = @"a,Result
0,0
1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.Equal("a", result);
    }

    [Fact]
    public void ParseCsvToExpression_NegatedVariable_ShouldReturnNegation()
    {
        // Arrange
        string csvContent = @"a,Result
0,1
1,0";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.Contains("!", result);
        Assert.Contains("a", result);
    }

    [Fact]
    public void ParseCsvToExpression_AllZeros_ShouldReturnZero()
    {
        // Arrange
        string csvContent = @"a,b,Result
0,0,0
0,1,0
1,0,0
1,1,0";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.Equal("0", result);
    }

    [Fact]
    public void ParseCsvToExpression_AllOnes_ShouldReturnOne()
    {
        // Arrange
        string csvContent = @"a,b,Result
0,0,1
0,1,1
1,0,1
1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.Equal("1", result);
    }

    [Theory]
    [InlineData("Output")]
    [InlineData("Value")]
    [InlineData("result")]
    [InlineData("output")]
    [InlineData("value")]
    public void ParseCsvToExpression_DifferentResultColumnNames_ShouldWork(string resultColumnName)
    {
        // Arrange
        string csvContent = $@"a,b,{resultColumnName}
0,0,0
0,1,0
1,0,0
1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
    }

    [Fact]
    public void ParseCsvToExpression_ThreeVariables_ShouldHandleCorrectly()
    {
        // Arrange
        string csvContent = @"a,b,c,Result
0,0,0,0
0,0,1,1
0,1,0,1
0,1,1,0
1,0,0,1
1,0,1,0
1,1,0,0
1,1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("c", result);
    }

    [Fact]
    public void ParseCsvToExpression_WithWhitespace_ShouldTrimAndWork()
    {
        // Arrange
        string csvContent = @"  a  ,  b  ,  Result  
  0  ,  0  ,  0  
  0  ,  1  ,  0  
  1  ,  0  ,  0  
  1  ,  1  ,  1  ";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
    }

    [Fact]
    public void ParseCsvToExpression_WithLiteralNewlines_ShouldWork()
    {
        // Arrange
        string csvContent = "a,b,Result\\n0,0,0\\n0,1,0\\n1,0,0\\n1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseCsvToExpression_EmptyOrNullInput_ShouldThrowException(string csvContent)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent));
    }

    [Fact]
    public void ParseCsvToExpression_HeaderOnly_ShouldThrowException()
    {
        // Arrange
        string csvContent = "a,b,Result";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent));
    }

    [Fact]
    public void ParseCsvToExpression_NoResultColumn_ShouldThrowException()
    {
        // Arrange
        string csvContent = @"a,b,c
0,0,0
0,1,1
1,0,1
1,1,0";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent));
    }

    [Fact]
    public void ParseCsvToExpression_NoVariableColumns_ShouldThrowException()
    {
        // Arrange
        string csvContent = @"Result
0
1";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent));
    }

    [Fact]
    public void ParseCsvToExpression_InvalidBooleanValues_ShouldThrowException()
    {
        // Arrange
        string csvContent = @"a,b,Result
0,0,0
0,1,2
1,0,0
1,1,1";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent));
    }

    [Fact]
    public void ParseCsvToExpression_InconsistentColumnCount_ShouldThrowException()
    {
        // Arrange
        string csvContent = @"a,b,Result
0,0,0
0,1
1,0,0
1,1,1";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent));
    }

    [Fact]
    public void ParseCsvToExpression_LongVariableNames_ShouldWork()
    {
        // Arrange
        string csvContent = @"variable_one,variable_two,Result
0,0,0
0,1,0
1,0,0
1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("variable_one", result);
        Assert.Contains("variable_two", result);
    }

    [Fact]
    public void ParseCsvToExpression_XorPattern_ShouldReturnCorrectExpression()
    {
        // Arrange
        string csvContent = @"a,b,Result
0,0,0
0,1,1
1,0,1
1,1,0";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        // Should represent XOR pattern: (a & !b) | (!a & b)
        Assert.Contains("!", result);
        Assert.Contains("&", result);
        Assert.Contains("|", result);
    }
}
