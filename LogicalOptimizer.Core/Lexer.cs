namespace LogicalOptimizer;

internal class Lexer
{
    private readonly string _input;
    private int _position;

    public Lexer(string input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _position = 0;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_position < _input.Length)
        {
            var current = _input[_position];

            if (char.IsWhiteSpace(current))
            {
                _position++;
                continue;
            }

            switch (current)
            {
                case '0':
                case '1':
                    // Constants are exactly one digit; "10", "0a" etc. are errors
                    if (_position + 1 < _input.Length && IsIdentifierChar(_input[_position + 1]))
                        throw new ArgumentException(
                            $"Invalid token starting with '{current}' at position {_position}: constants are single digits 0 or 1");
                    tokens.Add(new Token { Type = TokenType.Variable, Value = current.ToString(), Position = _position });
                    _position++;
                    break;
                case '!':
                    tokens.Add(new Token { Type = TokenType.Not, Value = "!", Position = _position });
                    _position++;
                    break;
                case '&':
                    tokens.Add(new Token { Type = TokenType.And, Value = "&", Position = _position });
                    _position++;
                    break;
                case '|':
                    tokens.Add(new Token { Type = TokenType.Or, Value = "|", Position = _position });
                    _position++;
                    break;
                case '(':
                    tokens.Add(new Token { Type = TokenType.LeftParen, Value = "(", Position = _position });
                    _position++;
                    break;
                case ')':
                    tokens.Add(new Token { Type = TokenType.RightParen, Value = ")", Position = _position });
                    _position++;
                    break;
                default:
                    if (char.IsLetter(current) || current == '_')
                    {
                        var start = _position;
                        var variable = ReadVariable();
                        tokens.Add(new Token { Type = TokenType.Variable, Value = variable, Position = start });
                    }
                    else if (char.IsDigit(current))
                    {
                        throw new ArgumentException(
                            $"Invalid token at position {_position}: variables cannot start with a digit");
                    }
                    else
                    {
                        throw new ArgumentException($"Unexpected character '{current}' at position {_position}");
                    }

                    break;
            }
        }

        tokens.Add(new Token { Type = TokenType.End, Value = "", Position = _position });
        return tokens;
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private string ReadVariable()
    {
        var start = _position;
        while (_position < _input.Length && IsIdentifierChar(_input[_position]))
            _position++;
        return _input.Substring(start, _position - start);
    }
}
