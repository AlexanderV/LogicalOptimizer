namespace LogicalOptimizer;

public class Parser
{
    private const int MaxNestingDepth = 1000;

    private readonly List<Token> _tokens;
    private int _depth;
    private int _position;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _position = 0;
    }

    private Token CurrentToken => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];

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
        var left = ParseAndExpression();

        while (CurrentToken.Type == TokenType.Or)
        {
            Consume(TokenType.Or);
            var right = ParseAndExpression();
            left = new OrNode(left, right);
        }

        return left;
    }

    private AstNode ParseAndExpression()
    {
        var left = ParseNotExpression();

        while (CurrentToken.Type == TokenType.And)
        {
            Consume(TokenType.And);
            var right = ParseNotExpression();
            left = new AndNode(left, right);
        }

        return left;
    }

    private AstNode ParseNotExpression()
    {
        if (CurrentToken.Type == TokenType.Not)
        {
            Consume(TokenType.Not);
            EnterNesting();
            var operand = ParseNotExpression();
            _depth--;
            return new NotNode(operand);
        }

        return ParsePrimaryExpression();
    }

    private AstNode ParsePrimaryExpression()
    {
        if (CurrentToken.Type == TokenType.Variable)
        {
            AstNode node = CurrentToken.Value switch
            {
                "1" => ConstantNode.True,
                "0" => ConstantNode.False,
                _ => new VariableNode(CurrentToken.Value)
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
