using System.Text;

namespace LogicalOptimizer;

public class Lexer
{
    private readonly string _input;
    private int _position;

    public Lexer(string input)
    {
        _input = input?.Replace(" ", "") ?? throw new ArgumentNullException(nameof(input));
        _position = 0;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_position < _input.Length)
        {
            var current = _input[_position];

            switch (current)
            {
                case '0':
                case '1':
                    // Check that this is a single digit
                    if (_position + 1 < _input.Length && char.IsDigit(_input[_position + 1]))
                        throw new ArgumentException($"Multi-digit numbers are not supported. Position {_position}");
                    tokens.Add(new Token {Type = TokenType.Variable, Value = current.ToString(), Position = _position});
                    _position++;
                    break;
                case '!':
                    tokens.Add(new Token {Type = TokenType.Not, Value = "!", Position = _position});
                    _position++;
                    break;
                case '&':
                    tokens.Add(new Token {Type = TokenType.And, Value = "&", Position = _position});
                    _position++;
                    break;
                case '|':
                    tokens.Add(new Token {Type = TokenType.Or, Value = "|", Position = _position});
                    _position++;
                    break;
                case '(':
                    tokens.Add(new Token {Type = TokenType.LeftParen, Value = "(", Position = _position});
                    _position++;
                    break;
                case ')':
                    tokens.Add(new Token {Type = TokenType.RightParen, Value = ")", Position = _position});
                    _position++;
                    break;
                default:
                    if (char.IsLetter(current) || char.IsDigit(current))
                    {
                        var variable = ReadVariable();
                        tokens.Add(new Token
                            {Type = TokenType.Variable, Value = variable, Position = _position - variable.Length});
                    }
                    else
                    {
                        throw new ArgumentException($"Unexpected character '{current}' at position {_position}");
                    }

                    break;
            }
        }

        tokens.Add(new Token {Type = TokenType.End, Value = "", Position = _position});
        return tokens;
    }

    private string ReadVariable()
    {
        var sb = new StringBuilder();

        while (_position < _input.Length && (char.IsLetterOrDigit(_input[_position]) || _input[_position] == '_'))
        {
            sb.Append(_input[_position]);
            _position++;
        }

        return sb.ToString();
    }
}