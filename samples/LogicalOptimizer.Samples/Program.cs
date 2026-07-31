using Samples;
using Samples.Recipes;

// Runs every recipe, prints its output, and reports PASS/FAIL. Any failed assertion inside
// a recipe throws and is reported here; the process exits non-zero if anything failed, so a
// CI step can gate on it.
var recipes = new (string Name, Action Run)[]
{
    ("1. Feature configuration validation", FeatureConfigurationValidation.Run),
    ("2. Business-rule regression check", BusinessRuleRegressionCheck.Run),
    ("3. Count valid configurations", CountValidConfigurations.Run),
    ("4. Optimize generated conditions", OptimizeGeneratedConditions.Run),
    ("5. CI verification", CiVerification.Run),
    ("6. External solver hand-off", ExternalSolverHandOff.Run)
};

var failures = 0;
foreach (var (name, run) in recipes)
{
    Console.WriteLine($"=== {name} ===");
    try
    {
        run();
        Console.WriteLine("PASS\n");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL: {ex.Message}\n");
    }
}

Console.WriteLine(failures == 0
    ? "All samples passed."
    : $"{failures} sample(s) failed.");

return failures == 0 ? 0 : 1;
