using Xunit;

namespace LogicalOptimizer.Tests;

public class PerformanceValidatorTests
{
    [Fact]
    public void ValidateExpression_NullExpression_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerformanceValidator.ValidateExpression(null!));
    }

    [Fact]
    public void ValidateExpression_EmptyExpression_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerformanceValidator.ValidateExpression(""));
    }

    [Fact]
    public void ValidateExpression_TooLongExpression_ThrowsArgumentException()
    {
        // Arrange
        var longExpression = new string('a', 10001);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => PerformanceValidator.ValidateExpression(longExpression));
        Assert.Contains("too long", exception.Message);
        Assert.Contains("10000", exception.Message);
    }

    [Fact]
    public void ValidateExpression_MaxLengthExpression_DoesNotThrow()
    {
        // Arrange
        var maxExpression = new string('a', 10000);

        // Act & Assert
        PerformanceValidator.ValidateExpression(maxExpression); // Should not throw
    }

    [Fact]
    public void ValidateExpression_TooDeepParentheses_ThrowsArgumentException()
    {
        // Arrange - create expression with deep nesting (51 levels)
        var deepExpression = new string('(', 51) + "a" + new string(')', 51);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => PerformanceValidator.ValidateExpression(deepExpression));
        Assert.Contains("deep nesting", exception.Message);
        Assert.Contains("50", exception.Message);
    }

    [Fact]
    public void ValidateExpression_MaxDepthParentheses_DoesNotThrow()
    {
        // Arrange - create expression with 50 levels of nesting
        var maxDepthExpression = new string('(', 50) + "a" + new string(')', 50);

        // Act & Assert
        PerformanceValidator.ValidateExpression(maxDepthExpression); // Should not throw
    }

    [Fact]
    public void ValidateExpression_UnbalancedParentheses_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => PerformanceValidator.ValidateExpression("((a)"));
        Assert.Contains("Unbalanced parentheses", exception.Message);
    }

    [Fact]
    public void ValidateIterations_TooManyIterations_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => PerformanceValidator.ValidateIterations(51));
        Assert.Contains("optimization iterations", exception.Message);
        Assert.Contains("50", exception.Message);
    }

    [Fact]
    public void ValidateProcessingTime_TooLongTime_ThrowsTimeoutException()
    {
        // Arrange
        var tooLongTime = TimeSpan.FromSeconds(31);

        // Act & Assert
        var exception = Assert.Throws<TimeoutException>(() => PerformanceValidator.ValidateProcessingTime(tooLongTime));
        Assert.Contains("processing time", exception.Message);
        Assert.Contains("30", exception.Message);
    }

    [Fact]
    public void ValidateAst_TooManyVariables_ThrowsArgumentException()
    {
        // Arrange - create AST with many variables
        var lexer = new Lexer("a1 | a2 | a3"); // Simple expression for testing
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Mock GetVariables to return 101 variables
        var variableNode = new TestVariableNodeWithManyVariables();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => PerformanceValidator.ValidateAst(variableNode));
        Assert.Contains("variables", exception.Message);
        Assert.Contains("100", exception.Message);
    }

    // Test class for simulating a node with many variables
    private class TestVariableNodeWithManyVariables : AstNode
    {
        public override AstNode Clone()
        {
            return new TestVariableNodeWithManyVariables();
        }

        public override bool Equals(object obj)
        {
            return false;
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public override string ToString()
        {
            return "test";
        }

        public override HashSet<string> GetVariables()
        {
            // Return 101 variables for validation testing
            var variables = new HashSet<string>();
            for (var i = 1; i <= 101; i++) variables.Add($"var{i}");
            return variables;
        }
    }
}