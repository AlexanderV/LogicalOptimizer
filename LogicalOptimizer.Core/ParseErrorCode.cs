namespace LogicalOptimizer;

/// <summary>Stable, machine-readable classification of a formula parse failure.</summary>
public enum ParseErrorCode
{
    /// <summary>The input was empty or whitespace only.</summary>
    EmptyExpression,

    /// <summary>A character that is not part of the grammar was found.</summary>
    UnexpectedCharacter,

    /// <summary>A constant was malformed (constants are the single digits 0 or 1).</summary>
    InvalidConstant,

    /// <summary>A variable name started with a digit.</summary>
    VariableStartsWithDigit,

    /// <summary>A token was valid on its own but not allowed at this position.</summary>
    UnexpectedToken,

    /// <summary>A specific token was required here (see <see cref="ParseDiagnostic.Expected" />).</summary>
    ExpectedToken,

    /// <summary>The input ended while more was required (e.g. an unclosed parenthesis).</summary>
    UnexpectedEndOfInput,

    /// <summary>The expression nested more deeply than the parser allows.</summary>
    NestingTooDeep
}
