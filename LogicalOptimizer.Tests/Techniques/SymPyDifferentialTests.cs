using System.Diagnostics;
using System.Text;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Differential corpus against SymPy (roadmap P0: external oracle). For random
///     functions within the exact zone, SymPy's simplify_logic (Quine–McCluskey for
///     ≤8 variables) must produce a DNF that is (a) equivalent to ours and (b) has the
///     SAME literal count when our result is MinimalProven — two independent exact
///     minimizers agreeing on the optimum. Skips silently when python+sympy are not
///     installed (CI installs them; see ci.yml).
/// </summary>
[Trait("Category", "External")]
public class SymPyDifferentialTests
{
    private static bool? _sympyAvailable;

    private static bool SympyAvailable()
    {
        _sympyAvailable ??= RunPython("import sympy") is not null;
        return _sympyAvailable.Value;
    }

    /// <summary>Run a python snippet; stdout on success, null when python/module missing.</summary>
    private static string? RunPython(string code, string stdin = "")
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "-c \"" + code.Replace("\"", "\\\"") + "\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            process.Start();
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(120_000);
            return process.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null; // python not on PATH
        }
    }

    private const string BatchMinimizeScript =
        "import sys\n" +
        "from sympy import symbols\n" +
        "from sympy.parsing.sympy_parser import parse_expr\n" +
        "from sympy.logic.boolalg import simplify_logic\n" +
        "for line in sys.stdin:\n" +
        "    line = line.strip()\n" +
        "    if not line: continue\n" +
        "    e = parse_expr(line)\n" +
        "    print(str(simplify_logic(e, form='dnf')))\n";

    private static string ToSymPySyntax(string expression)
    {
        return expression.Replace("!", "~");
    }

    private static string FromSymPySyntax(string expression)
    {
        // SymPy prints ~x, &, |, True, False
        return expression.Replace("~", "!").Replace("True", "1").Replace("False", "0");
    }

    [Fact]
    public void MinimalDnf_AgreesWithSymPyQuineMcCluskey()
    {
        // OUR side is built first, on purpose. Its floors need no python, so a corpus that stopped
        // producing comparable cases is reported even in an environment that cannot run the oracle —
        // whereas an availability check at the top made the whole test a green no-op.
        var rng = new Random(818);
        var cases = new List<(string Expression, OptimizationResult Result)>();
        var optimizer = new BooleanExpressionOptimizer();
        var options = new OptimizationOptions { ComputeCnf = false, ComputeAdvancedForms = false };

        for (var trial = 0; trial < 30; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(3, 7), 4);
            var result = optimizer.OptimizeExpression(expression, options);
            if (result.DnfStatus == ComputationStatus.Computed)
                cases.Add((expression, result));
        }

        Assert.True(cases.Count >= 25,
            $"only {cases.Count}/30 generated expressions produced a computed DNF — the corpus is " +
            "too thin to be a differential test");

        // The cost comparison below — the half that cross-checks the minimality PROOF rather than
        // mere equivalence — only fires on MinimalProven rows, so require that they exist.
        var proven = cases.Count(c => c.Result.MinimizationStatus == MinimizationStatus.MinimalProven);
        Assert.True(proven >= 20,
            $"only {proven} of {cases.Count} cases are MinimalProven — SymPy would be compared " +
            "against almost no proofs");

        if (!ExternalOracle.ShouldRun("SymPy", SympyAvailable(), "pip install sympy")) return;

        var stdin = string.Join("\n", cases.Select(c => ToSymPySyntax(c.Expression))) + "\n";
        var output = RunPython(BatchMinimizeScript, stdin);
        Assert.NotNull(output);

        var lines = output!.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(cases.Count, lines.Length);

        for (var i = 0; i < cases.Count; i++)
        {
            var (expression, result) = cases[i];
            var sympyDnf = RandomExpressions.Parse(FromSymPySyntax(lines[i]));

            Assert.True(TruthTable.AreEquivalent(RandomExpressions.Parse(expression), sympyDnf),
                $"Case {i}: SymPy DNF not equivalent for '{expression}': '{lines[i]}'");

            if (result.MinimizationStatus == MinimizationStatus.MinimalProven)
            {
                var ourCost = AstMetrics.CountLiterals(RandomExpressions.Parse(result.DNF));
                var sympyCost = AstMetrics.CountLiterals(sympyDnf);
                Assert.True(ourCost <= sympyCost,
                    $"Case {i}: our PROVEN minimal DNF has {ourCost} literals but SymPy found {sympyCost} " +
                    $"for '{expression}' — the minimality proof is wrong");
            }
        }
    }
}
