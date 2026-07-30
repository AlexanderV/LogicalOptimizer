namespace LogicalOptimizer;

/// <summary>
///     Recursive-descent parser for the boolean expression grammar (Or → And → Not →
///     Primary). All nodes are built through a <see cref="FormulaFactory" />, so the
///     parser output is canonical: operator chains like <c>a &amp; b &amp; c</c> are
///     collected into a single n-ary node, constants are folded and operands are in
///     canonical order.
/// </summary>
internal class Parser
{
    private const int MaxNestingDepth = 1000;

    private readonly FormulaFactory _factory;
    private readonly string _source;
    private readonly List<Token> _tokens;
    private int _depth;
    private int _position;

    /// <summary>Creates a parser with a private <see cref="FormulaFactory" /> instance.</summary>
    public Parser(List<Token> tokens) : this(tokens, new FormulaFactory())
    {
    }

    /// <summary>Creates a parser that builds nodes through the given factory.</summary>
    public Parser(List<Token> tokens, FormulaFactory factory) : this(tokens, factory, null)
    {
    }

    /// <summary>
    ///     Creates a parser that builds through the given factory and carries the original
    ///     <paramref name="source" /> text so parse diagnostics can render a caret snippet.
    /// </summary>
    public Parser(List<Token> tokens, FormulaFactory factory, string? source)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _source = source ?? "";
        _position = 0;
    }

    private Token CurrentToken => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];

    /// <summary>Parses the token stream into a canonical expression tree.</summary>
    public AstNode Parse()
    {
        if (_tokens.Count == 0 || (_tokens.Count == 1 && _tokens[0].Type == TokenType.End))
            throw Error(ParseErrorCode.EmptyExpression, "Empty expression", 0, 0);

        var result = ParseOrExpression();
        if (CurrentToken.Type != TokenType.End)
            throw UnexpectedToken();
        return result;
    }

    private void Consume(TokenType expectedType)
    {
        if (CurrentToken.Type != expectedType)
        {
            var code = CurrentToken.Type == TokenType.End
                ? ParseErrorCode.UnexpectedEndOfInput
                : ParseErrorCode.ExpectedToken;
            throw Error(code,
                $"Expected {expectedType}, got {CurrentToken.Type} at position {CurrentToken.Position}",
                CurrentToken, new[] { Describe(expectedType) });
        }

        _position++;
    }

    private AstNode ParseOrExpression()
    {
        var operands = new List<AstNode> { ParseAndExpression() };

        while (CurrentToken.Type == TokenType.Or)
        {
            Consume(TokenType.Or);
            operands.Add(ParseAndExpression());
        }

        return operands.Count == 1 ? operands[0] : _factory.Or(operands);
    }

    private AstNode ParseAndExpression()
    {
        var operands = new List<AstNode> { ParseNotExpression() };

        while (CurrentToken.Type == TokenType.And)
        {
            Consume(TokenType.And);
            operands.Add(ParseNotExpression());
        }

        return operands.Count == 1 ? operands[0] : _factory.And(operands);
    }

    private AstNode ParseNotExpression()
    {
        if (CurrentToken.Type == TokenType.Not)
        {
            Consume(TokenType.Not);
            EnterNesting();
            var operand = ParseNotExpression();
            _depth--;
            return _factory.Not(operand);
        }

        return ParsePrimaryExpression();
    }

    private AstNode ParsePrimaryExpression()
    {
        if (CurrentToken.Type == TokenType.Variable)
        {
            var node = CurrentToken.Value switch
            {
                "1" => _factory.True,
                "0" => _factory.False,
                _ => _factory.Variable(CurrentToken.Value)
            };
            Consume(TokenType.Variable);
            return node;
        }

        if (CurrentToken.Type == TokenType.LeftParen)
        {
            Consume(TokenType.LeftParen);
            EnterNesting();
            var expression = ParseOrExpression();
            _depth--;
            Consume(TokenType.RightParen);
            return expression;
        }

        // A primary expression must start with a variable, a constant, or "(".
        throw UnexpectedToken(new[] { "variable", "(" });
    }

    private void EnterNesting()
    {
        if (++_depth > MaxNestingDepth)
            throw Error(ParseErrorCode.NestingTooDeep,
                $"Expression is nested too deeply (more than {MaxNestingDepth} levels)",
                CurrentToken.Position, 0);
    }

    /// <summary>An "unexpected token" (or end-of-input) error at the current token.</summary>
    private FormulaParseException UnexpectedToken(IReadOnlyList<string>? expected = null)
    {
        var code = CurrentToken.Type == TokenType.End
            ? ParseErrorCode.UnexpectedEndOfInput
            : ParseErrorCode.UnexpectedToken;
        var what = CurrentToken.Type == TokenType.End ? "end of input" : CurrentToken.Value;
        return Error(code, $"Unexpected token {what} at position {CurrentToken.Position}", CurrentToken, expected);
    }

    private FormulaParseException Error(ParseErrorCode code, string message, Token token,
        IReadOnlyList<string>? expected = null)
    {
        var length = token.Type == TokenType.End ? 0 : token.Value.Length;
        return Error(code, message, token.Position, length, expected);
    }

    private FormulaParseException Error(ParseErrorCode code, string message, int position, int length,
        IReadOnlyList<string>? expected = null)
    {
        return new FormulaParseException(new ParseDiagnostic(code, message, position, length, _source, expected));
    }

    private static string Describe(TokenType type)
    {
        return type switch
        {
            TokenType.And => "&",
            TokenType.Or => "|",
            TokenType.Not => "!",
            TokenType.LeftParen => "(",
            TokenType.RightParen => ")",
            TokenType.Variable => "variable",
            TokenType.End => "end of input",
            _ => type.ToString()
        };
    }
}
