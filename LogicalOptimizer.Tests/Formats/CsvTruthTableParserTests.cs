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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - parsed expression must compute the AND function
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "a & b"),
            $"Parsed expression '{result}' is not equivalent to 'a & b'");
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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - parsed expression must compute the OR function
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "x | y"),
            $"Parsed expression '{result}' is not equivalent to 'x | y'");
    }

    [Fact]
    public void ParseCsvToExpression_SingleVariable_ShouldReturnVariableName()
    {
        // Arrange
        string csvContent = @"a,Result
0,0
1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - parsed expression must compute NOT a
        Assert.Equal("!a", result);
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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - result column name must not change the parsed function (AND table)
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "a & b"),
            $"Parsed expression '{result}' is not equivalent to 'a & b'");
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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - the table above is the odd-parity (3-input XOR) function
        var parity = "(!a & !b & c) | (!a & b & !c) | (a & !b & !c) | (a & b & c)";
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, parity),
            $"Parsed expression '{result}' is not equivalent to the parity function '{parity}'");
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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - whitespace must be trimmed and the AND function preserved
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "a & b"),
            $"Parsed expression '{result}' is not equivalent to 'a & b'");
    }

    [Fact]
    public void ParseCsvToExpression_WithLiteralNewlines_ShouldWork()
    {
        // Arrange
        string csvContent = "a,b,Result\\n0,0,0\\n0,1,0\\n1,0,0\\n1,1,1";

        // Act
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - literal \n separators must parse to the same AND function
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "a & b"),
            $"Parsed expression '{result}' is not equivalent to 'a & b'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseCsvToExpression_EmptyOrNullInput_ShouldThrowException(string? csvContent)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent!));
    }

    [Fact]
    public void ParseCsvToExpression_HeaderOnly_ShouldThrowException()
    {
        // Arrange
        string csvContent = "a,b,Result";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent!));
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
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent!));
    }

    [Fact]
    public void ParseCsvToExpression_NoVariableColumns_ShouldThrowException()
    {
        // Arrange
        string csvContent = @"Result
0
1";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent!));
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
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent!));
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
        Assert.Throws<ArgumentException>(() => CsvTruthTableParser.ParseCsvToExpression(csvContent!));
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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - long identifiers must survive and the AND function must be preserved
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "variable_one & variable_two"),
            $"Parsed expression '{result}' is not equivalent to 'variable_one & variable_two'");
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
        var result = CsvTruthTableParser.ParseCsvToExpression(csvContent!);

        // Assert - parsed expression must compute XOR: (a & !b) | (!a & b)
        Assert.NotNull(result);
        Assert.True(TruthTable.AreEquivalent(result, "a & !b | !a & b"),
            $"Parsed expression '{result}' is not equivalent to XOR 'a & !b | !a & b'");
    }
}
