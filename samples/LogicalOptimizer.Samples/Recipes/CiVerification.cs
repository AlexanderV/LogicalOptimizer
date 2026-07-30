using System.Text.Json;
using LogicalOptimizer;

namespace Samples.Recipes;

/// <summary>
///     Recipe 5 — a CI gate over a set of business-rule refactors. It verifies that each
///     candidate rule is still equivalent to its baseline and emits a machine-readable JSON
///     artifact a pipeline can archive or assert on; a real gate would exit non-zero when any
///     rule regressed.
/// </summary>
internal static class CiVerification
{
    public static void Run()
    {
        var checks = new (string Name, string Baseline, string Candidate)[]
        {
            ("access", "admin | (owner & businessHours)", "admin | owner & businessHours"),
            ("discount", "gold & !expired", "!expired & gold")
        };

        var rules = checks.Select(c =>
        {
            var check = EquivalenceChecker.Check(c.Baseline, c.Candidate);
            return new RuleResult(
                c.Name,
                check.AreEquivalent == true,
                check.AreEquivalent == false && check.Counterexample is not null
                    ? string.Join(",", check.Counterexample.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"))
                    : null);
        }).ToArray();

        var passed = Array.TrueForAll(rules, r => r.Equivalent);
        var artifact = new GateResult(passed, rules.Length, rules);

        // A real CI step would write this to a file and upload it as a build artifact.
        Console.WriteLine(JsonSerializer.Serialize(artifact, SampleJson.Options));

        SampleAssert.That(passed, "every business-rule equivalence must hold in the CI gate");
    }

    private sealed record RuleResult(string Rule, bool Equivalent, string? Counterexample);

    private sealed record GateResult(bool Passed, int Checked, IReadOnlyList<RuleResult> Rules);
}
