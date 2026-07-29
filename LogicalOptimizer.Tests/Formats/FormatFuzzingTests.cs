using System.Text;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     In-process deterministic fuzzing of the DIMACS/WCNF/OPB parsers (seed-fixed,
///     reproducible). Garbage, mutated and truncated inputs must never hang or allocate
///     without bound: the only acceptable outcomes are a clean parse, a typed
///     <see cref="FormatParseException" /> or a <see cref="ComputationBudgetExceededException" />.
///     Every trial reads a bounded-length string, so the whole suite runs in bounded time and
///     memory.
/// </summary>
public class FormatFuzzingTests
{
    // Hostile alphabet: format keywords' characters, operators, signs, digits and controls.
    private const string Alphabet = "0123456789 \t\r\n-+~xhpcnfwmin:;=<>*|&()é中\0";

    private static void AssertControlled(Action parse, string context, string input)
    {
        try
        {
            parse();
        }
        catch (FormatParseException)
        {
            // Documented syntactic-rejection path.
        }
        catch (ComputationBudgetExceededException)
        {
            // Documented resource-guard path.
        }
        catch (Exception ex)
        {
            Assert.Fail($"{context}: input {Printable(input)} escaped with {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Printable(string input)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in input)
            builder.Append(c < ' ' || c > '~' ? $"\\u{(int)c:X4}" : c.ToString());
        return builder.Append('"').ToString();
    }

    [Fact]
    public void GarbageFuzz_AllParsersFailControlled()
    {
        var rng = new Random(70001);
        var iterations = 0;
        for (var trial = 0; trial < 2500; trial++)
        {
            var length = rng.Next(0, 60);
            var builder = new StringBuilder(length);
            for (var i = 0; i < length; i++) builder.Append(Alphabet[rng.Next(Alphabet.Length)]);
            var input = builder.ToString();

            AssertControlled(() => DimacsParser.Parse(new StringReader(input)), "DIMACS garbage", input);
            AssertControlled(() => WcnfParser.Parse(new StringReader(input)), "WCNF garbage", input);
            AssertControlled(() => OpbParser.Parse(new StringReader(input)), "OPB garbage", input);
            iterations += 3;
        }

        Assert.Equal(7500, iterations);
    }

    [Fact]
    public void TruncationFuzz_ValidPrefixesFailControlled()
    {
        const string cnf = "p cnf 3 2\n1 -3 0\n2 3 -1 0\n";
        const string wcnf = "p wcnf 2 3 10\n10 1 2 0\n3 1 0\n1 2 0\n";
        const string opb = "* #variable= 3 #constraint= 2\nmin: +1 x1 ;\n+1 x1 +2 ~x2 >= 1 ;\n+1 x3 <= 1 ;\n";

        var rng = new Random(70002);
        for (var trial = 0; trial < 400; trial++)
        {
            var c = cnf[..rng.Next(0, cnf.Length + 1)];
            var w = wcnf[..rng.Next(0, wcnf.Length + 1)];
            var o = opb[..rng.Next(0, opb.Length + 1)];
            AssertControlled(() => DimacsParser.Parse(new StringReader(c)), "DIMACS truncation", c);
            AssertControlled(() => WcnfParser.Parse(new StringReader(w)), "WCNF truncation", w);
            AssertControlled(() => OpbParser.Parse(new StringReader(o)), "OPB truncation", o);
        }
    }

    [Fact]
    public void TightTokenBudget_LargeInput_ThrowsBudgetNotOom()
    {
        // A tight token budget must abort a large stream instead of buffering it: proof the
        // parser's memory use is bounded by the budget, not the input size.
        var budget = new ResourceBudget { ParseTokenLimit = 1000 };
        var big = new StringBuilder("p cnf 5 100000\n");
        for (var i = 0; i < 100_000; i++) big.Append("1 -2 3 0\n");
        var text = big.ToString();

        Assert.Throws<ComputationBudgetExceededException>(() =>
            DimacsParser.Parse(new StringReader(text), budget));
    }

    [Fact]
    public void OversizedVariableIndex_ThrowsBudget_AcrossFormats()
    {
        Assert.Throws<ComputationBudgetExceededException>(() =>
            DimacsParser.Parse(new StringReader("p cnf 1 1\n99999999999 0\n")));
        Assert.Throws<ComputationBudgetExceededException>(() =>
            WcnfParser.Parse(new StringReader("h 99999999999\n")));
        Assert.Throws<ComputationBudgetExceededException>(() =>
            OpbParser.Parse(new StringReader("+1 x99999999999 >= 1 ;\n")));
    }
}
