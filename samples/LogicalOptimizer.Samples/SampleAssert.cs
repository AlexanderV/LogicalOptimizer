namespace Samples;

/// <summary>
///     Tiny assertion helper so each recipe verifies its own output. A failed check throws,
///     which the harness turns into a non-zero exit code — that is how CI proves the recipes
///     still produce the results their READMEs claim.
/// </summary>
internal static class SampleAssert
{
    public static void That(bool condition, string because)
    {
        if (!condition)
            throw new InvalidOperationException("Assertion failed: " + because);
    }
}
