using LogicalOptimizer;

namespace Samples.Recipes;

/// <summary>
///     Recipe 4 — shrink a machine-generated condition and prove it still means the same.
///     The optimizer returns a smaller expression, verifies it is equivalent to the input,
///     and reports whether the result is provably minimal.
/// </summary>
internal static class OptimizeGeneratedConditions
{
    public static void Run()
    {
        // A code generator emitted a verbose guard.
        const string generated = "(a & b) | (a & c) | (a & d)";

        var result = new BooleanExpressionOptimizer().OptimizeExpression(generated);

        var before = AstMetrics.CountLiterals(new FormulaFactory().Parse(generated));
        var after = AstMetrics.CountLiterals(new FormulaFactory().Parse(result.Optimized));

        SampleAssert.That(result.IsEquivalent(), "the optimized form must be equivalent to the input");
        SampleAssert.That(after < before, "the optimized form should use fewer literals");
        SampleAssert.That(result.MinimizationStatus == MinimizationStatus.MinimalProven,
            "for a small function the result should be proven minimal");

        Console.WriteLine($"Generated: {generated}  ({before} literals)");
        Console.WriteLine($"Optimized: {result.Optimized}  ({after} literals)");
        Console.WriteLine($"Equivalent: proven, Minimality: {result.MinimizationStatus}");
    }
}
