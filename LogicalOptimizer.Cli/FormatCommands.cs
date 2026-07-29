using System;
using System.IO;
using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Handles the standard-format verbs (<c>solve</c>, <c>maxsat</c>, <c>solve-pb</c>,
///     <c>count</c>) that read DIMACS/WCNF/OPB files through <c>LogicalOptimizer.Formats</c>
///     and dispatch to the SAT, MaxSAT, pseudo-Boolean or d-DNNF engines. Kept separate from
///     the expression-oriented flow of <see cref="CommandLineProcessor" />.
/// </summary>
internal static class FormatCommands
{
    /// <summary>True when the first CLI argument selects one of the standard-format verbs.</summary>
    public static bool IsFormatVerb(string arg)
    {
        return arg is "solve" or "maxsat" or "solve-pb" or "count";
    }

    public static int Run(string[] args)
    {
        var verb = args[0];
        string? file = null;
        var engine = "dnnf";

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--engine")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: --engine requires a value (dnnf).");
                    return 1;
                }

                engine = args[++i];
            }
            else if (arg.StartsWith("--engine=", StringComparison.Ordinal))
            {
                engine = arg["--engine=".Length..];
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Error: unknown option '{arg}'.");
                return 1;
            }
            else if (file == null)
            {
                file = arg;
            }
            else
            {
                Console.Error.WriteLine("Error: expected exactly one input file.");
                return 1;
            }
        }

        if (file == null)
        {
            Console.Error.WriteLine($"Error: '{verb}' requires an input file.");
            return 1;
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: file not found: {file}");
            return 1;
        }

        try
        {
            return verb switch
            {
                "solve" => Solve(file),
                "maxsat" => MaxSat(file),
                "solve-pb" => SolvePb(file),
                "count" => Count(file, engine),
                _ => 1
            };
        }
        catch (FormatParseException ex)
        {
            Console.Error.WriteLine($"Parse error: {ex.Message}");
            return 1;
        }
        catch (ComputationBudgetExceededException ex)
        {
            Console.Error.WriteLine($"Budget exceeded: {ex.Message}");
            return 1;
        }
    }

    private static int Solve(string file)
    {
        using var reader = new StreamReader(file);
        var problem = DimacsParser.Parse(reader);
        var solver = problem.ToSolver();
        var result = solver.Solve();

        switch (result)
        {
            case SatResult.Satisfiable:
                Console.WriteLine("s SATISFIABLE");
                PrintModel(v => solver.GetValue(v), problem.VariableCount);
                return 0;
            case SatResult.Unsatisfiable:
                Console.WriteLine("s UNSATISFIABLE");
                return 0;
            default:
                Console.WriteLine("s UNKNOWN");
                return 0;
        }
    }

    private static int MaxSat(string file)
    {
        using var reader = new StreamReader(file);
        var problem = WcnfParser.Parse(reader);
        var result = problem.Solve();

        switch (result.Status)
        {
            case MaxSatStatus.Optimal:
                Console.WriteLine("s OPTIMUM FOUND");
                Console.WriteLine($"o {result.Cost}");
                PrintModel(v => result.GetValue(v), problem.VariableCount);
                return 0;
            case MaxSatStatus.HardClausesUnsatisfiable:
                Console.WriteLine("s UNSATISFIABLE");
                return 0;
            default:
                Console.WriteLine("s UNKNOWN");
                if (result.Values != null) Console.WriteLine($"o {result.Cost}");
                return 0;
        }
    }

    private static int SolvePb(string file)
    {
        using var reader = new StreamReader(file);
        var problem = OpbParser.Parse(reader);
        var solver = problem.ToSolver();
        var result = solver.Solve();

        switch (result)
        {
            case SatResult.Satisfiable:
                Console.WriteLine("s SATISFIABLE");
                // Only the original problem variables are meaningful; the encoding adds auxiliaries.
                PrintModel(v => solver.GetValue(v), problem.VariableCount);
                return 0;
            case SatResult.Unsatisfiable:
                Console.WriteLine("s UNSATISFIABLE");
                return 0;
            default:
                Console.WriteLine("s UNKNOWN");
                return 0;
        }
    }

    private static int Count(string file, string engine)
    {
        if (!engine.Equals("dnnf", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Error: unsupported count engine '{engine}'. Supported: dnnf.");
            return 1;
        }

        using var reader = new StreamReader(file);
        var problem = DimacsParser.Parse(reader);
        var formula = problem.ToFormula();
        var circuit = KnowledgeCompilation.CompileToDnnf(formula);

        var count = circuit.CountModels();
        // Variables declared in the header but absent from every clause are free: each
        // doubles the model count over the full declared variable set.
        var freeVariables = problem.VariableCount - formula.GetVariables().Count;
        if (freeVariables > 0) count *= BigInteger.Pow(2, freeVariables);

        Console.WriteLine(count);
        return 0;
    }

    private static void PrintModel(Func<int, bool> value, int variableCount)
    {
        if (variableCount == 0)
        {
            Console.WriteLine("v 0");
            return;
        }

        var literals = new string[variableCount];
        for (var v = 1; v <= variableCount; v++)
            literals[v - 1] = (value(v) ? v : -v).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Console.WriteLine("v " + string.Join(' ', literals) + " 0");
    }
}
