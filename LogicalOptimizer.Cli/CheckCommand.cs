using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer;

/// <summary>
///     Handles the <c>check</c> verb: <c>logical-optimizer check "&lt;expr1&gt;" "&lt;expr2&gt;"</c>.
///     Reports whether the two expressions are equivalent, and on inequivalence a concrete
///     counterexample assignment where they differ. Backed by <see cref="EquivalenceChecker" />,
///     so every verdict is exact (truth table in the exhaustive range, SAT miter beyond); the
///     only non-answer is "unknown" when the conflict budget runs out on a very large instance.
///     <para>
///         Exit codes: <c>0</c> equivalent, <c>3</c> not equivalent, <c>4</c> unknown, plus the
///         shared <c>1</c> (usage error) and <c>2</c> (processing error). <c>3</c> and <c>4</c>
///         are additive codes specific to this verb — the meaning of <c>0</c>/<c>1</c>/<c>2</c>
///         is unchanged, per the SUPPORT.md CLI contract.
///     </para>
/// </summary>
internal static class CheckCommand
{
    public const string Verb = "check";

    /// <summary>Exit code when the expressions are proven NOT equivalent (counterexample found).</summary>
    public const int ExitNotEquivalent = 3;

    /// <summary>Exit code when the conflict budget ran out before a proof either way.</summary>
    public const int ExitUnknown = 4;

    private const string Usage =
        "Usage: logical-optimizer check \"<expression1>\" \"<expression2>\" [--format=json]";

    public static int Run(string[] args)
    {
        var format = CliOutputFormat.Text;
        var positional = new List<string>();

        // args[0] is the verb itself.
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--json")
            {
                format = CliOutputFormat.Json;
            }
            else if (arg == "--format" || arg == "-f")
            {
                if (i + 1 >= args.Length)
                    return UsageError("--format requires a value (text, json).");
                if (!TryParseFormat(args[++i], ref format, out var message))
                    return UsageError(message);
            }
            else if (arg.StartsWith("--format=", StringComparison.Ordinal))
            {
                if (!TryParseFormat(arg["--format=".Length..], ref format, out var message))
                    return UsageError(message);
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                return UsageError($"Unknown option '{arg}'.");
            }
            else
            {
                positional.Add(arg);
            }
        }

        if (positional.Count != 2)
            return UsageError(positional.Count < 2
                ? "'check' requires exactly two expressions."
                : "'check' takes exactly two expressions; more were provided.");

        var left = positional[0];
        var right = positional[1];

        foreach (var expression in positional)
            if (expression.Length > PerformanceValidator.MAX_EXPRESSION_LENGTH)
                return UsageError(
                    $"Expression is too long (maximum {PerformanceValidator.MAX_EXPRESSION_LENGTH:N0} characters)");

        // Parse each side separately so a diagnostic can name WHICH expression is malformed.
        if (!TryParse(left, "left", left, right, format, out var leftAst, out var exit)) return exit;
        if (!TryParse(right, "right", left, right, format, out var rightAst, out exit)) return exit;

        try
        {
            var result = EquivalenceChecker.Check(leftAst, rightAst);

            if (format == CliOutputFormat.Json)
                CheckReportWriter.Write(Console.Out, left, right, result);
            else
                WriteText(left, right, result);

            return result.AreEquivalent switch
            {
                true => 0,
                false => ExitNotEquivalent,
                null => ExitUnknown
            };
        }
        catch (Exception ex)
        {
            if (format == CliOutputFormat.Json)
                CheckReportWriter.WriteError(Console.Out, left, right, ex.Message);
            Console.Error.WriteLine($"Error checking equivalence: {ex.Message}");
            return 2;
        }
    }

    private static bool TryParseFormat(string value, ref CliOutputFormat format, out string message)
    {
        message = string.Empty;
        if (value.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            format = CliOutputFormat.Json;
            return true;
        }

        if (value.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            format = CliOutputFormat.Text;
            return true;
        }

        message = $"Unknown format '{value}'. Supported: text, json.";
        return false;
    }

    private static bool TryParse(string expression, string side, string left, string right,
        CliOutputFormat format, out AstNode ast, out int exitCode)
    {
        try
        {
            // FormulaFactory.Parse hands the source text to the parser, so a diagnostic
            // carries a complete caret snippet (the bare Parser constructor would not).
            ast = new FormulaFactory().Parse(expression);
            exitCode = 0;
            return true;
        }
        catch (Exception ex)
        {
            var diagnostic =
                (ex as FormulaParseException ?? ex.InnerException as FormulaParseException)?.Diagnostic;

            if (format == CliOutputFormat.Json)
                CheckReportWriter.WriteError(Console.Out, left, right, ex.Message, diagnostic, side);

            Console.Error.WriteLine(
                $"Error in the {side} expression: {diagnostic?.Message ?? ex.Message}");
            if (diagnostic is not null)
                Console.Error.WriteLine(diagnostic.Snippet);

            ast = null!;
            exitCode = 2;
            return false;
        }
    }

    private static void WriteText(string left, string right, EquivalenceCheckResult result)
    {
        Console.WriteLine($"Left: {left}");
        Console.WriteLine($"Right: {right}");

        switch (result.AreEquivalent)
        {
            case true:
                Console.WriteLine("Equivalent: proven");
                break;
            case false:
                Console.WriteLine("Equivalent: no");
                Console.WriteLine($"Counterexample: {FormatCounterexample(result.Counterexample!)}");
                break;
            default:
                Console.WriteLine("Equivalent: unknown (conflict budget exhausted before a proof either way)");
                break;
        }
    }

    private static string FormatCounterexample(IReadOnlyDictionary<string, bool> assignment)
    {
        // Both sides constant (e.g. `check 1 0`): the difference needs no variable assignment.
        if (assignment.Count == 0) return "(empty assignment)";

        return string.Join(", ", assignment
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine(Usage);
        return 1;
    }
}
