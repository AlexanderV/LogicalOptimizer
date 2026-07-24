namespace LogicalOptimizer;

public sealed class Token
{
    public TokenType Type { get; init; }
    public string Value { get; init; } = "";
    public int Position { get; init; }
}
