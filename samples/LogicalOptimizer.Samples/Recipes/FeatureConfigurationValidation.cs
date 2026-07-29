using LogicalOptimizer;

namespace Samples.Recipes;

/// <summary>
///     Recipe 1 — is a product configuration valid, and what does it force?
///     A feature model is just a Boolean formula; SAT answers "can this be satisfied",
///     enumeration gives one concrete configuration, and the backbone gives the choices
///     that are forced in EVERY valid configuration.
/// </summary>
internal static class FeatureConfigurationValidation
{
    public static void Run()
    {
        var f = new FormulaFactory();

        // A small edition/feature model:
        //   exactly one edition:            (free | pro) & !(free & pro)
        //   single sign-on needs Pro:       sso   -> pro   ==  !sso | pro
        //   audit log needs Pro:            audit -> pro   ==  !audit | pro
        var constraints = f.Parse("(free | pro) & !(free & pro) & (!sso | pro) & (!audit | pro)");

        // Is there at least one valid configuration?
        var oneConfig = FormulaAnalysis.EnumerateModels(constraints).FirstOrDefault();
        SampleAssert.That(oneConfig is not null, "the feature model must have at least one valid configuration");
        Console.WriteLine($"A valid configuration: {Format(oneConfig)}");

        // What is forced across ALL valid configurations? (nothing here — both editions are possible)
        var backbone = FormulaAnalysis.ComputeBackbone(constraints);
        SampleAssert.That(backbone.IsSatisfiable == true, "the constraints must be satisfiable");
        Console.WriteLine($"Forced in every configuration: {Format(backbone.ForcedVariables)}");

        // Now the user picks single sign-on. That must force the Pro edition (and rule out Free).
        var withSso = f.And(constraints, f.Variable("sso"));
        var ssoForced = FormulaAnalysis.ComputeBackbone(withSso).ForcedVariables;
        SampleAssert.That(ssoForced is not null && ssoForced.TryGetValue("pro", out var pro) && pro,
            "choosing single sign-on must force the Pro edition");
        Console.WriteLine("Selecting sso forces pro = true, as the dependency requires.");
    }

    private static string Format(IReadOnlyDictionary<string, bool>? assignment)
    {
        return assignment is null
            ? "(none)"
            : string.Join(", ", assignment.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
    }
}
