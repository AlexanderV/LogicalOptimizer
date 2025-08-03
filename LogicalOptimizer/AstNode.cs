namespace LogicalOptimizer;

public abstract class AstNode
{
    public abstract AstNode Clone();
    public abstract override string ToString();
    public abstract HashSet<string> GetVariables();
    public abstract override bool Equals(object? obj);
    public abstract override int GetHashCode();
}