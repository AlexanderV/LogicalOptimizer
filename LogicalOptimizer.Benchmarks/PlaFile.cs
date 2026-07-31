namespace LogicalOptimizer.Benchmarks;

/// <summary>
///     Minimal reader for the Espresso <c>.pla</c> SUBSET used by the vendored PlaCorpus:
///     <c>.i/.o/.ilb/.ob/.p</c> headers, cube lines with <c>0/1/-</c> inputs and <c>0/1</c>
///     outputs, terminated by <c>.e</c>; <c>#</c> comment lines. Semantics are the Espresso
///     fd default restricted to no output don't-cares: output <c>j</c> is the OR of the cubes
///     whose output column <c>j</c> is <c>'1'</c> (a <c>'0'</c> output entry says nothing).
///     <para>
///         DEV-TOOL INFRASTRUCTURE, not a shipped import path: the Benchmarks project is
///         never packaged, so this stays outside the pinned public API of the library
///         packages (which export BLIF/Verilog but deliberately have no PLA importer).
///         Unknown directives throw instead of being skipped, so the reader can never
///         silently misread a real-world PLA feature (e.g. <c>.type fr</c> or output
///         don't-cares) this subset does not model.
///     </para>
/// </summary>
public sealed class PlaFile
{
    private PlaFile(IReadOnlyList<string> inputNames, IReadOnlyList<string> outputNames,
        IReadOnlyList<(string Inputs, string Outputs)> cubes)
    {
        InputNames = inputNames;
        OutputNames = outputNames;
        Cubes = cubes;
    }

    public IReadOnlyList<string> InputNames { get; }
    public IReadOnlyList<string> OutputNames { get; }

    /// <summary>Cube rows: input part over {0,1,-}, output part over {0,1}.</summary>
    public IReadOnlyList<(string Inputs, string Outputs)> Cubes { get; }

    public static PlaFile Read(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static PlaFile Parse(string text)
    {
        var inputCount = -1;
        var outputCount = -1;
        var declaredCubes = -1;
        List<string>? inputNames = null;
        List<string>? outputNames = null;
        var cubes = new List<(string Inputs, string Outputs)>();
        var ended = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (ended)
                throw new FormatException($"Content after .e: '{line}'");

            if (line.StartsWith('.'))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case ".i":
                        inputCount = int.Parse(parts[1]);
                        break;
                    case ".o":
                        outputCount = int.Parse(parts[1]);
                        break;
                    case ".ilb":
                        inputNames = parts.Skip(1).ToList();
                        break;
                    case ".ob":
                        outputNames = parts.Skip(1).ToList();
                        break;
                    case ".p":
                        declaredCubes = int.Parse(parts[1]);
                        break;
                    case ".e":
                        ended = true;
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Directive '{parts[0]}' is outside the PLA subset this corpus reader models");
                }

                continue;
            }

            var cube = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cube.Length != 2)
                throw new FormatException($"Cube line must be '<inputs> <outputs>': '{line}'");
            if (inputCount < 0 || outputCount < 0)
                throw new FormatException(".i/.o must precede the first cube line");
            if (cube[0].Length != inputCount || cube[0].Any(c => c is not ('0' or '1' or '-')))
                throw new FormatException($"Bad input part '{cube[0]}' (want {inputCount} of 0/1/-)");
            if (cube[1].Length != outputCount || cube[1].Any(c => c is not ('0' or '1')))
                throw new FormatException($"Bad output part '{cube[1]}' (want {outputCount} of 0/1)");
            cubes.Add((cube[0], cube[1]));
        }

        if (!ended) throw new FormatException("Missing .e terminator");
        if (declaredCubes >= 0 && declaredCubes != cubes.Count)
            throw new FormatException($".p declares {declaredCubes} cubes but {cubes.Count} were read");
        if (inputNames == null || outputNames == null)
            throw new FormatException("This corpus subset requires .ilb and .ob name lists");
        if (inputNames.Count != inputCount || outputNames.Count != outputCount)
            throw new FormatException(".ilb/.ob name counts must match .i/.o");

        return new PlaFile(inputNames, outputNames, cubes);
    }

    /// <summary>
    ///     Output <paramref name="output" /> as a SOP expression string over the input names
    ///     (parser syntax <c>!</c>/<c>&amp;</c>/<c>|</c>): the OR of the product terms of
    ///     every cube whose output column is '1'. "0" when the ON-set is empty.
    /// </summary>
    public string OutputExpression(int output)
    {
        var terms = new List<string>();
        foreach (var (inputs, outputs) in Cubes)
        {
            if (outputs[output] != '1') continue;
            var literals = new List<string>();
            for (var i = 0; i < inputs.Length; i++)
                if (inputs[i] == '1') literals.Add(InputNames[i]);
                else if (inputs[i] == '0') literals.Add("!" + InputNames[i]);

            terms.Add(literals.Count == 0 ? "1" : string.Join(" & ", literals));
        }

        return terms.Count == 0 ? "0" : string.Join(" | ", terms);
    }

    /// <summary>
    ///     Literal count of the raw cube expansion of <paramref name="output" /> — the sum of
    ///     the specified (non-'-') input positions over its ON-set cubes. This is the size of
    ///     the expression <see cref="OutputExpression" /> produces, and the baseline a
    ///     minimizer's result must never exceed.
    /// </summary>
    public int OutputCubeLiteralCount(int output)
    {
        return Cubes.Where(cube => cube.Outputs[output] == '1')
            .Sum(cube => cube.Inputs.Count(c => c != '-'));
    }
}
