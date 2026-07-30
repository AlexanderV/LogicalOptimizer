using System.Numerics;
using LogicalOptimizer;

namespace Samples.Recipes;

/// <summary>
///     Recipe 3 — how many valid configurations does a feature model allow?
///     Both the BDD and the d-DNNF engines count satisfying assignments exactly (BigInteger,
///     so the count scales past 64 bits). Running both is a free cross-check.
/// </summary>
internal static class CountValidConfigurations
{
    public static void Run()
    {
        var f = new FormulaFactory();

        // Three features {a, b, c}: b requires a (b -> a), and at least one must be chosen.
        var model = f.Parse("(!b | a) & (a | b | c)");

        var viaDnnf = KnowledgeCompilation.CompileToDnnf(model).CountModels();
        var viaBdd = BinaryDecisionDiagram.BuildWithBestOrder(model).CountSatisfyingAssignments();

        SampleAssert.That(viaDnnf == viaBdd, "the d-DNNF and BDD counts must agree");
        SampleAssert.That(viaDnnf == new BigInteger(5), "this model should allow exactly 5 configurations");

        Console.WriteLine($"Valid configurations: {viaDnnf} (d-DNNF and BDD agree).");
    }
}
