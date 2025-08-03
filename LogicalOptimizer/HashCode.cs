namespace LogicalOptimizer;

public static class HashCode
{
    public static int Combine(params object[] values)
    {
        var hash = 17;
        foreach (var value in values) hash = hash * 31 + (value?.GetHashCode() ?? 0);
        return hash;
    }
}