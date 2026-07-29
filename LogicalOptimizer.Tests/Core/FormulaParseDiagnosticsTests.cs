using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Covers the structured parsing diagnostics: <see cref="FormulaFactory.TryParse" /> returns a
///     <see cref="ParseDiagnostic" /> (position, length, expected tokens, machine-readable code and a
///     caret snippet) instead of throwing, and <see cref="FormulaFactory.Parse(string)" /> throws a
///     <see cref="FormulaParseException" /> carrying the same diagnostic.
/// </summary>
public class FormulaParseDiagnosticsTests
{
    [Fact]
    public void TryParse_ValidExpression_ReturnsTrueWithFormulaAndNoDiagnostic()
    {
        var ok = new FormulaFactory().TryParse("a & (b | c)", out var formula, out var diagnostic);

        Assert.True(ok);
        Assert.NotNull(formula);
        Assert.Null(diagnostic);
        Assert.Equal("a & (b | c)", formula!.ToString());
    }

    [Theory]
    [InlineData("", ParseErrorCode.EmptyExpression, 0)]
    [InlineData("a & & b", ParseErrorCode.UnexpectedToken, 4)]
    [InlineData("a @ b", ParseErrorCode.UnexpectedCharacter, 2)]
    [InlineData("1a", ParseErrorCode.InvalidConstant, 0)]
    [InlineData("2", ParseErrorCode.VariableStartsWithDigit, 0)]
    [InlineData("a b", ParseErrorCode.UnexpectedToken, 2)]
    [InlineData("((a)", ParseErrorCode.UnexpectedEndOfInput, 4)]
    public void TryParse_InvalidExpression_ReturnsFalseWithCodedDiagnostic(
        string input, ParseErrorCode expectedCode, int expectedPosition)
    {
        var ok = new FormulaFactory().TryParse(input, out var formula, out var diagnostic);

        Assert.False(ok);
        Assert.Null(formula);
        Assert.NotNull(diagnostic);
        Assert.Equal(expectedCode, diagnostic!.Code);
        Assert.Equal(expectedPosition, diagnostic.Position);
        Assert.Equal(input, diagnostic.Source);
    }

    [Fact]
    public void TryParse_MissingClosingParen_ReportsExpectedToken()
    {
        var ok = new FormulaFactory().TryParse("((a)", out _, out var diagnostic);

        Assert.False(ok);
        Assert.Contains(")", diagnostic!.Expected);
    }

    [Fact]
    public void TryParse_Diagnostic_RendersCaretSnippet()
    {
        new FormulaFactory().TryParse("a & & b", out _, out var diagnostic);

        // The caret points at the offending second '&' (index 4).
        Assert.Equal("a & & b\n    ^", diagnostic!.Snippet);
        Assert.Equal(1, diagnostic.Length);
    }

    [Fact]
    public void Parse_InvalidExpression_ThrowsFormulaParseExceptionCarryingDiagnostic()
    {
        var ex = Assert.Throws<FormulaParseException>(() => new FormulaFactory().Parse("a @ b"));

        Assert.Equal(ParseErrorCode.UnexpectedCharacter, ex.Diagnostic.Code);
        Assert.Equal(2, ex.Diagnostic.Position);
        Assert.Equal(ex.Message, ex.Diagnostic.Message);
    }

    [Fact]
    public void FormulaParseException_IsArgumentException_ForBackwardCompatibility()
    {
        // Existing callers that catch ArgumentException keep working.
        Assert.ThrowsAny<System.ArgumentException>(() => new FormulaFactory().Parse("a & & b"));
    }
}
