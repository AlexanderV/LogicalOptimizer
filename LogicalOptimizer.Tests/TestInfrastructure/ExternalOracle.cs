using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     The external-oracle suites (SymPy, Z3) cross-check us against a tool we do not ship, so they
///     have to tolerate an environment where that tool is missing. The way they did it —
///     <c>if (!Available()) return;</c> — reports <b>Passed</b> while asserting nothing, and the
///     result is indistinguishable from a run where the oracle really agreed. That is the failure
///     mode doc/TESTING.md audit rule 7 exists for, and it is worst here: the whole external half of
///     technique 7 can disappear (a broken <c>pip install</c> step, a native library that stops
///     loading) without a single red test.
///     <para>
///         So the skip is opt-in-strict. Locally an absent oracle stays a skip. Where the oracle is
///         known to work — CI installs sympy in <c>ci.yml</c> — the job sets
///         <c>LOGICALOPTIMIZER_REQUIRE_&lt;NAME&gt;=1</c> and its absence becomes a FAILURE, so the
///         cross-check cannot be lost silently.
///     </para>
/// </summary>
internal static class ExternalOracle
{
    /// <summary>
    ///     Call at the point a suite would skip. Returns false when the caller should skip; throws a
    ///     readable failure instead when the environment declared this oracle mandatory.
    /// </summary>
    /// <param name="name">Oracle id, uppercased into the env var name (e.g. <c>SYMPY</c>, <c>Z3</c>).</param>
    /// <param name="available">Whether the probe found a usable oracle.</param>
    /// <param name="howToInstall">Shown in the failure so the fix is obvious from the log alone.</param>
    public static bool ShouldRun(string name, bool available, string howToInstall)
    {
        if (available) return true;

        var variable = $"LOGICALOPTIMIZER_REQUIRE_{name.ToUpperInvariant()}";
        if (Environment.GetEnvironmentVariable(variable) == "1")
            Assert.Fail(
                $"{name} is required in this environment ({variable}=1) but the probe could not use " +
                $"it, so the {name} differential cross-check did not run. Either restore the oracle " +
                $"({howToInstall}) or unset {variable} deliberately — do not let the external half of " +
                "the differential technique vanish into a green run.");

        return false;
    }
}
