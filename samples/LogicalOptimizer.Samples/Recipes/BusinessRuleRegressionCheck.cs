using LogicalOptimizer;

namespace Samples.Recipes;

/// <summary>
///     Recipe 2 — did refactoring a business rule change its behaviour?
///     Equivalence checking proves a rewrite is behaviour-preserving, and returns a concrete
///     counterexample (an input where old and new disagree) when it is not.
/// </summary>
internal static class BusinessRuleRegressionCheck
{
    public static void Run()
    {
        // Old access rule: admins, or owners during business hours.
        const string oldRule = "admin | (owner & businessHours)";

        // Refactor #1 — a cosmetic rewrite that should preserve behaviour.
        const string safeRewrite = "admin | owner & businessHours";
        var safe = EquivalenceChecker.Check(oldRule, safeRewrite);
        SampleAssert.That(safe.AreEquivalent == true, "the cosmetic rewrite must stay equivalent");
        Console.WriteLine("Refactor #1 is equivalent — no behaviour change.");

        // Refactor #2 — a subtle bug: it drops the business-hours guard for owners.
        const string buggyRewrite = "admin | owner";
        var buggy = EquivalenceChecker.Check(oldRule, buggyRewrite);
        SampleAssert.That(buggy.AreEquivalent == false, "the buggy rewrite must be flagged as different");
        SampleAssert.That(buggy.Counterexample is not null, "a counterexample must be produced");
        Console.WriteLine($"Refactor #2 is NOT equivalent. Counterexample: {Format(buggy.Counterexample!)}");
    }

    private static string Format(IReadOnlyDictionary<string, bool> assignment)
    {
        return string.Join(", ", assignment.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
    }
}
