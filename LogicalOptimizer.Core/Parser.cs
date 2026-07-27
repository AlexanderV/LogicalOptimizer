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
    private readonly List<Token> _tokens;
    private int _depth;
    private int _position;

    /// <summary>Creates a parser with a private <see cref="FormulaFactory" /> instance.</summary>
    public Parser(List<Token> tokens) : this(tokens, new FormulaFactory())
    {
    }

    /// <summary>Creates a parser that builds nodes through the given factory.</summary>
    public Parser(List<Token> tokens, FormulaFactory factory)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _position = 0;
    }

    private Token CurrentToken => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];

    /// <summary>Parses the token stream into a canonical expression tree.</summary>
    public AstNode Parse()
    {
        if (_tokens.Count == 0 || (_tokens.Count == 1 && _tokens[0].Type == TokenType.End))
            throw new ArgumentException("Empty expression");

        var result = ParseOrExpression();
        if (CurrentToken.Type != TokenType.End)
            throw new ArgumentException($"Unexpected token {CurrentToken.Value} at position {CurrentToken.Position}");
        return result;
    }

    private void Consume(TokenType expectedType)
    {
        if (CurrentToken.Type != expectedType)
            throw new ArgumentException(
                $"Expected {expectedType}, got {CurrentToken.Type} at position {CurrentToken.Position}");
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

        throw new ArgumentException($"Unexpected token {CurrentToken.Value} at position {CurrentToken.Position}");
    }

    private void EnterNesting()
    {
        if (++_depth > MaxNestingDepth)
            throw new ArgumentException($"Expression is nested too deeply (more than {MaxNestingDepth} levels)");
    }
}
