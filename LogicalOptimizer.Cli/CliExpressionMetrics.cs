using System;

namespace LogicalOptimizer;

/// <summary>CLI-side expression helpers shared by the text and JSON formatters.</summary>
internal static class CliExpressionMetrics
{
    /// <summary>
    ///     Literal count of an expression for the cost report, or <c>null</c> when the string
    ///     cannot be parsed (a display helper must never fail the whole run).
    /// </summary>
    public static int? TryCountLiterals(string expression)
    {
        try
        {
            var ast = new Parser(new Lexer(expression).Tokenize()).Parse();
            return AstMetrics.CountLiterals(ast);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
