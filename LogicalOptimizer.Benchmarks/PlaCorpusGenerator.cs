namespace LogicalOptimizer.Benchmarks;

/// <summary>One generated multi-output PLA corpus member: file name, kind tag and full text.</summary>
public sealed record PlaInstance(string FileName, string Kind, string Content);

/// <summary>
///     Deterministic generator for the vendored multi-output PLA corpus
///     (LogicalOptimizer.Benchmarks/PlaCorpus). Closes part of competitive-assessment gap #3
///     ("corpus too small and synthetic" — 30-day roadmap item 1: multi-output PLA family).
///     These are GENERATED, structured functions in the classic Espresso <c>.pla</c> cube
///     format (fd interpretation: an output column '1' puts the cube in that output's
///     ON-set), not downloaded industrial benchmarks:
///     <list type="bullet">
///         <item><b>bcd7seg</b> — BCD-to-7-segment decoder from the well-known truth table (4 in / 7 out).</item>
///         <item><b>add2</b> — 2-bit binary adder, full minterm table (4 in / 3 out).</item>
///         <item><b>dec3to8</b> — 3-to-8 line decoder, one-hot outputs (3 in / 8 out).</item>
///         <item><b>prio8</b> — 8-line priority encoder with valid flag, don't-care inputs (8 in / 4 out).</item>
///         <item><b>cmp3</b> — 3-bit magnitude comparator, full minterm table (6 in / 3 out).</item>
///         <item><b>rnd*</b> — seeded pseudo-random cube lists (fixed seeds recorded in each file).</item>
///     </list>
///     Everything is derived from fixed truth tables or <see cref="Random" /> with the fixed
///     seeds below, so regeneration is byte-identical. Regenerate with:
///     <c>dotnet run -c Release --project LogicalOptimizer.Benchmarks -- generate-corpora</c>.
/// </summary>
public static class PlaCorpusGenerator
{
    private static readonly int[] RandomSeeds = { 9001, 9002, 9003 };

    public static IReadOnlyList<PlaInstance> Generate()
    {
        var instances = new List<PlaInstance>
        {
            BcdTo7Segment(),
            TwoBitAdder(),
            Decoder3To8(),
            PriorityEncoder8(),
            Comparator3(),
            RandomPla("rnd6x4-01.pla", inputs: 6, outputs: 4, cubes: 24, seed: RandomSeeds[0]),
            RandomPla("rnd6x4-02.pla", inputs: 6, outputs: 4, cubes: 24, seed: RandomSeeds[1]),
            RandomPla("rnd8x3-01.pla", inputs: 8, outputs: 3, cubes: 28, seed: RandomSeeds[2])
        };
        return instances;
    }

    /// <summary>BCD-to-7-segment decoder: digits 0..9, segments a..g (common truth table).</summary>
    private static PlaInstance BcdTo7Segment()
    {
        // Segment patterns a..g for digits 0..9 (1 = segment lit).
        var segments = new[]
        {
            "1111110", "0110000", "1101101", "1111001", "0110011",
            "1011011", "1011111", "1110000", "1111111", "1111011"
        };
        var cubes = new List<(string Inputs, string Outputs)>();
        for (var digit = 0; digit <= 9; digit++)
            cubes.Add((Bits(digit, 4), segments[digit]));

        return new PlaInstance("bcd7seg.pla", "bcd-to-7-segment",
            Render("bcd-to-7-segment",
                "digits 0..9 from the well-known common-cathode segment table; inputs 10..15 unspecified (fd: off)",
                Array.Empty<string>(),
                new[] { "d3", "d2", "d1", "d0" },
                new[] { "sa", "sb", "sc", "sd", "se", "sf", "sg" },
                cubes));
    }

    /// <summary>2-bit adder: (a1 a0) + (b1 b0) = (cout s1 s0), full 16-row minterm table.</summary>
    private static PlaInstance TwoBitAdder()
    {
        var cubes = new List<(string Inputs, string Outputs)>();
        for (var a = 0; a < 4; a++)
            for (var b = 0; b < 4; b++)
            {
                var sum = a + b;
                cubes.Add((Bits(a, 2) + Bits(b, 2), Bits(sum, 3)));
            }

        return new PlaInstance("add2.pla", "2-bit-adder",
            Render("2-bit-adder", "(a1 a0) + (b1 b0) = (cout s1 s0); full minterm table",
                Array.Empty<string>(),
                new[] { "a1", "a0", "b1", "b0" },
                new[] { "cout", "s1", "s0" },
                cubes));
    }

    /// <summary>3-to-8 line decoder: one-hot output per input combination.</summary>
    private static PlaInstance Decoder3To8()
    {
        var cubes = new List<(string Inputs, string Outputs)>();
        for (var i = 0; i < 8; i++)
        {
            var outputs = new char[8];
            Array.Fill(outputs, '0');
            outputs[i] = '1';
            cubes.Add((Bits(i, 3), new string(outputs)));
        }

        return new PlaInstance("dec3to8.pla", "3-to-8-decoder",
            Render("3-to-8-decoder", "one-hot line decoder: y_i = minterm i of (s2 s1 s0)",
                Array.Empty<string>(),
                new[] { "s2", "s1", "s0" },
                new[] { "y0", "y1", "y2", "y3", "y4", "y5", "y6", "y7" },
                cubes));
    }

    /// <summary>8-line priority encoder (r7 highest): valid flag + 3-bit index, '-' inputs below the winner.</summary>
    private static PlaInstance PriorityEncoder8()
    {
        var cubes = new List<(string Inputs, string Outputs)>();
        for (var i = 7; i >= 0; i--)
        {
            // Input columns are r7..r0: '0' for every higher-priority line, '1' at the
            // winner, '-' for everything below it (the essence of a priority encoder).
            var inputs = new char[8];
            for (var bit = 7; bit >= 0; bit--)
            {
                var column = 7 - bit;
                inputs[column] = bit > i ? '0' : bit == i ? '1' : '-';
            }

            cubes.Add((new string(inputs), "1" + Bits(i, 3)));
        }

        return new PlaInstance("prio8.pla", "8-line-priority-encoder",
            Render("8-line-priority-encoder",
                "v=1 and (y2 y1 y0)=index of highest asserted request line, r7 highest; no request => all outputs 0 (fd)",
                Array.Empty<string>(),
                new[] { "r7", "r6", "r5", "r4", "r3", "r2", "r1", "r0" },
                new[] { "v", "y2", "y1", "y0" },
                cubes));
    }

    /// <summary>3-bit magnitude comparator: lt / eq / gt over (a2 a1 a0) vs (b2 b1 b0), full 64-row table.</summary>
    private static PlaInstance Comparator3()
    {
        var cubes = new List<(string Inputs, string Outputs)>();
        for (var a = 0; a < 8; a++)
            for (var b = 0; b < 8; b++)
            {
                var outputs = a < b ? "100" : a == b ? "010" : "001";
                cubes.Add((Bits(a, 3) + Bits(b, 3), outputs));
            }

        return new PlaInstance("cmp3.pla", "3-bit-comparator",
            Render("3-bit-comparator", "lt/eq/gt of (a2 a1 a0) vs (b2 b1 b0); full minterm table",
                Array.Empty<string>(),
                new[] { "a2", "a1", "a0", "b2", "b1", "b0" },
                new[] { "lt", "eq", "gt" },
                cubes));
    }

    /// <summary>
    ///     Seeded pseudo-random multi-output PLA: cube inputs drawn from {0,1,-} (2:2:1),
    ///     outputs uniform over {0,1} with at least one '1' forced per cube (fd semantics
    ///     make an all-zero output row meaningless) and at least one cube per output column.
    /// </summary>
    private static PlaInstance RandomPla(string fileName, int inputs, int outputs, int cubes, int seed)
    {
        var random = new Random(seed);
        var list = new List<(string Inputs, string Outputs)>();
        for (var c = 0; c < cubes; c++)
        {
            var input = new char[inputs];
            for (var i = 0; i < inputs; i++)
            {
                var pick = random.Next(5);
                input[i] = pick < 2 ? '0' : pick < 4 ? '1' : '-';
            }

            var output = new char[outputs];
            for (var o = 0; o < outputs; o++) output[o] = random.Next(2) == 1 ? '1' : '0';
            if (Array.IndexOf(output, '1') < 0) output[random.Next(outputs)] = '1';

            list.Add((new string(input), new string(output)));
        }

        // Every output column must have at least one ON-set cube, or that output is the
        // constant 0 and adds nothing to the regression; force deterministically if needed.
        for (var o = 0; o < outputs; o++)
            if (list.All(cube => cube.Outputs[o] != '1'))
            {
                var (input, old) = list[o % list.Count];
                var patched = old.ToCharArray();
                patched[o] = '1';
                list[o % list.Count] = (input, new string(patched));
            }

        var inputNames = Enumerable.Range(0, inputs).Select(i => $"x{i}").ToArray();
        var outputNames = Enumerable.Range(0, outputs).Select(o => $"f{o}").ToArray();
        return new PlaInstance(fileName, "seeded-random",
            Render("seeded-random",
                "pseudo-random cube list; fully reproducible from the recorded seed",
                new[] { $"# seed: {seed}" },
                inputNames, outputNames, list));
    }

    private static string Bits(int value, int width)
    {
        var chars = new char[width];
        for (var i = 0; i < width; i++)
            chars[i] = (value >> (width - 1 - i) & 1) == 1 ? '1' : '0';
        return new string(chars);
    }

    private static string Render(string kind, string note, string[] extraComments,
        string[] inputs, string[] outputs, List<(string Inputs, string Outputs)> cubes)
    {
        var lines = new List<string>
        {
            $"# kind: {kind}",
            "# generator: LogicalOptimizer.Benchmarks PlaCorpusGenerator (deterministic)",
            "# regenerate: dotnet run -c Release --project LogicalOptimizer.Benchmarks -- generate-corpora",
            $"# note: {note}"
        };
        lines.AddRange(extraComments);
        lines.Add($".i {inputs.Length}");
        lines.Add($".o {outputs.Length}");
        lines.Add($".ilb {string.Join(' ', inputs)}");
        lines.Add($".ob {string.Join(' ', outputs)}");
        lines.Add($".p {cubes.Count}");
        foreach (var (input, output) in cubes)
        {
            if (input.Length != inputs.Length || output.Length != outputs.Length)
                throw new InvalidOperationException($"Malformed cube {input} {output} in {kind}");
            lines.Add($"{input} {output}");
        }

        lines.Add(".e");
        return string.Join('\n', lines) + "\n";
    }
}
