using System.Numerics;
using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Correctness gate for the EXPERIMENTAL binary serialization of the BDD and d-DNNF engines
///     (roadmap P2.3). Both engines expose <c>Save(Stream)</c> / <c>Load(Stream, ResourceBudget?,
///     CancellationToken)</c> over a compact, self-describing little-endian format. The suite
///     proves: a differential round-trip preserving variables, model count and evaluation;
///     deterministic byte output; committed golden blobs (drift detector, regenerable via
///     <c>LOGICALOPTIMIZER_REGENERATE_GOLDEN=1</c>); forward-version and wrong-engine rejection
///     with the typed exception; corrupted-input fuzzing that never hangs or over-allocates; and a
///     budgeted load that refuses a hostile size header instead of pre-allocating.
/// </summary>
public class CircuitSerializationTests
{
    private const string RegenerateVariable = "LOGICALOPTIMIZER_REGENERATE_GOLDEN";

    private static AstNode Parse(string expression)
    {
        return RandomExpressions.Parse(expression);
    }

    // ----- Round-trip (differential against the in-memory circuit) --------------------------

    [Fact]
    public void RoundTrip_Dnnf_PreservesVariablesCountAndQueries()
    {
        var rng = new Random(20260729);
        var cases = 0;
        for (var trial = 0; trial < 300; trial++)
        {
            var variableCount = rng.Next(1, 8);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);
            var original = KnowledgeCompilation.CompileToDnnf(ast);

            var reloaded = DnnfRoundTrip(original);

            Assert.Equal(original.Variables, reloaded.Variables);
            Assert.Equal(original.CountModels(), reloaded.CountModels());
            Assert.Equal(original.IsSatisfiable, reloaded.IsSatisfiable);

            var variables = original.Variables;
            // Evaluation differential: every full assignment must agree with the original circuit
            // (compared via single-assignment evidence counts, which are 1 exactly for a model).
            if (variables.Count is > 0 and <= 7)
                for (var m = 0; m < 1 << variables.Count; m++)
                {
                    var assignment = new Dictionary<string, bool>();
                    for (var j = 0; j < variables.Count; j++) assignment[variables[j]] = (m & (1 << j)) != 0;
                    Assert.Equal(original.CountModels(assignment), reloaded.CountModels(assignment));
                }

            // Weighted differential on a random weight vector.
            var weights = variables.ToDictionary(v => v, _ => ((double)rng.Next(1, 4), (double)rng.Next(1, 4)));
            Assert.Equal(original.WeightedModelCount(weights), reloaded.WeightedModelCount(weights), 9);
            cases++;
        }

        Assert.True(cases >= 300);
    }

    [Fact]
    public void RoundTrip_Bdd_PreservesVariablesCountAndEvaluation()
    {
        var rng = new Random(987654321);
        var cases = 0;
        for (var trial = 0; trial < 300; trial++)
        {
            var variableCount = rng.Next(1, 7);
            var expression = RandomExpressions.Generate(rng, variableCount, rng.Next(2, 5), allowConstants: true);
            var ast = Parse(expression);
            // Alternate the builder so both identity and sifted variable orders are exercised.
            var original = (trial % 2 == 0)
                ? BinaryDecisionDiagram.BuildWithBestOrder(ast)
                : BinaryDecisionDiagram.BuildWithSiftedOrder(ast);

            var reloaded = BddRoundTrip(original);

            Assert.Equal(original.Variables, reloaded.Variables);
            Assert.Equal(original.CountSatisfyingAssignments(), reloaded.CountSatisfyingAssignments());
            Assert.Equal(original.IsTautology(), reloaded.IsTautology());
            Assert.Equal(original.IsContradiction(), reloaded.IsContradiction());

            var variables = original.Variables.OrderBy(v => v).ToList();
            if (variables.Count <= 6)
                for (var m = 0; m < 1 << variables.Count; m++)
                {
                    var assignment = new Dictionary<string, bool>();
                    for (var j = 0; j < variables.Count; j++) assignment[variables[j]] = (m & (1 << j)) != 0;
                    Assert.Equal(original.Evaluate(assignment), reloaded.Evaluate(assignment));
                }

            // The reloaded manager stays queryable: enumerated models match the count.
            Assert.Equal(reloaded.CountSatisfyingAssignments(),
                new BigInteger(reloaded.EnumerateSatisfyingAssignments().Count()));
            cases++;
        }

        Assert.True(cases >= 300);
    }

    // ----- Deterministic bytes --------------------------------------------------------------

    [Fact]
    public void DeterministicBytes_Dnnf_SavingTwiceIsIdentical()
    {
        var rng = new Random(4242);
        for (var trial = 0; trial < 100; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(1, 7), rng.Next(2, 5), allowConstants: true);
            var circuit = KnowledgeCompilation.CompileToDnnf(Parse(expression));

            Assert.Equal(Convert.ToBase64String(SaveDnnf(circuit)), Convert.ToBase64String(SaveDnnf(circuit)));
            // A reload then re-save is byte-identical too (the format is a fixed point).
            var once = SaveDnnf(circuit);
            var twice = SaveDnnf(DnnfCircuit.Load(new MemoryStream(once)));
            Assert.Equal(Convert.ToBase64String(once), Convert.ToBase64String(twice));
        }
    }

    [Fact]
    public void DeterministicBytes_Bdd_SavingTwiceIsIdentical()
    {
        var rng = new Random(24680);
        for (var trial = 0; trial < 100; trial++)
        {
            var expression = RandomExpressions.Generate(rng, rng.Next(1, 6), rng.Next(2, 5), allowConstants: true);
            var bdd = BinaryDecisionDiagram.BuildWithBestOrder(Parse(expression));

            Assert.Equal(Convert.ToBase64String(SaveBdd(bdd)), Convert.ToBase64String(SaveBdd(bdd)));
            var once = SaveBdd(bdd);
            var twice = SaveBdd(BinaryDecisionDiagram.Load(new MemoryStream(once)));
            Assert.Equal(Convert.ToBase64String(once), Convert.ToBase64String(twice));
        }
    }

    // ----- Golden format samples (drift detector) -------------------------------------------

    [Fact]
    public void Golden_Dnnf_WriterReproducesAndReaderLoads()
    {
        var circuit = KnowledgeCompilation.CompileToDnnf(Parse("(a | b) & c"));
        var actual = SaveDnnf(circuit);
        var golden = GoldenBytes("dnnf_sample.bin", actual);
        if (golden is null) return; // regenerated

        Assert.Equal(Convert.ToBase64String(golden), Convert.ToBase64String(actual));

        var loaded = DnnfCircuit.Load(new MemoryStream(golden));
        Assert.Equal(new[] { "a", "b", "c" }, loaded.Variables);
        Assert.Equal(new BigInteger(3), loaded.CountModels()); // (a|b)&c: {a c},{b c},{ab c} -> 3
    }

    [Fact]
    public void Golden_Bdd_WriterReproducesAndReaderLoads()
    {
        var bdd = BinaryDecisionDiagram.BuildWithBestOrder(Parse("(a | b) & c"));
        var actual = SaveBdd(bdd);
        var golden = GoldenBytes("bdd_sample.bin", actual);
        if (golden is null) return; // regenerated

        Assert.Equal(Convert.ToBase64String(golden), Convert.ToBase64String(actual));

        var loaded = BinaryDecisionDiagram.Load(new MemoryStream(golden));
        Assert.Equal(new BigInteger(3), loaded.CountSatisfyingAssignments());
        Assert.True(loaded.Evaluate(new Dictionary<string, bool> { ["a"] = true, ["c"] = true }));
        Assert.False(loaded.Evaluate(new Dictionary<string, bool> { ["a"] = true, ["c"] = false }));
    }

    // ----- Forward-version rejection --------------------------------------------------------

    [Fact]
    public void ForwardVersion_IsRefused_ForBothEngines()
    {
        var dnnf = SaveDnnf(KnowledgeCompilation.CompileToDnnf(Parse("a & b")));
        var bdd = SaveBdd(BinaryDecisionDiagram.BuildWithBestOrder(Parse("a & b")));

        // Byte 4 is the format version (after the 4-byte magic). Bump it beyond what we understand.
        dnnf[4] = 99;
        bdd[4] = 99;

        Assert.Throws<CircuitSerializationException>(() => DnnfCircuit.Load(new MemoryStream(dnnf)));
        Assert.Throws<CircuitSerializationException>(() => BinaryDecisionDiagram.Load(new MemoryStream(bdd)));
    }

    // ----- Wrong-engine rejection -----------------------------------------------------------

    [Fact]
    public void WrongEngine_IsRefused_BothDirections()
    {
        var dnnf = SaveDnnf(KnowledgeCompilation.CompileToDnnf(Parse("a | b")));
        var bdd = SaveBdd(BinaryDecisionDiagram.BuildWithBestOrder(Parse("a | b")));

        // A d-DNNF blob loaded as a BDD, and a BDD blob loaded as a d-DNNF, are typed errors.
        Assert.Throws<CircuitSerializationException>(() => BinaryDecisionDiagram.Load(new MemoryStream(dnnf)));
        Assert.Throws<CircuitSerializationException>(() => DnnfCircuit.Load(new MemoryStream(bdd)));
    }

    [Fact]
    public void ChecksumMismatch_IsRefused()
    {
        var blob = SaveDnnf(KnowledgeCompilation.CompileToDnnf(Parse("a & (b | c)")));
        // Flip a payload byte (past the header) without fixing the trailing CRC.
        blob[blob.Length / 2] ^= 0xFF;
        Assert.ThrowsAny<Exception>(() => DnnfCircuit.Load(new MemoryStream(blob)));
    }

    // ----- Corrupted-input fuzzing ----------------------------------------------------------

    [Fact]
    public void Fuzz_TruncationsAndBitFlips_NeverHangOrOverAllocate()
    {
        var seeds = new[]
        {
            SaveDnnf(KnowledgeCompilation.CompileToDnnf(Parse("(a | b) & (c | !d) & (a | !c)"))),
            SaveBdd(BinaryDecisionDiagram.BuildWithBestOrder(Parse("(a | b) & (c | !d) & (a | !c)")))
        };
        var loaders = new Action<byte[]>[]
        {
            b => DnnfCircuit.Load(new MemoryStream(b)),
            b => BinaryDecisionDiagram.Load(new MemoryStream(b))
        };
        // A tight budget so even a "valid nodeCount" mutation cannot drive a large allocation.
        var budget = new ResourceBudget { BddNodeLimit = 4096 };
        var budgetedLoaders = new Action<byte[]>[]
        {
            b => DnnfCircuit.Load(new MemoryStream(b), budget),
            b => BinaryDecisionDiagram.Load(new MemoryStream(b), budget)
        };

        var rng = new Random(70003);
        var iterations = 0;

        for (var s = 0; s < seeds.Length; s++)
        {
            var valid = seeds[s];

            // Truncations: every prefix length.
            for (var len = 0; len <= valid.Length; len++)
            {
                var prefix = valid[..len];
                AssertControlled(() => budgetedLoaders[s](prefix));
                iterations++;
            }

            // Random single/multi bit-flips over a copy of the valid blob.
            for (var trial = 0; trial < 6000; trial++)
            {
                var mutant = (byte[])valid.Clone();
                var flips = rng.Next(1, 5);
                for (var f = 0; f < flips; f++)
                    mutant[rng.Next(mutant.Length)] ^= (byte)(1 << rng.Next(8));
                AssertControlled(() => budgetedLoaders[s](mutant));
                AssertControlled(() => loaders[s](mutant));
                iterations += 2;
            }
        }

        // Report the executed iteration count (proves the loop ran to completion, no hang).
        Assert.True(iterations >= 24000, $"fuzz executed {iterations} controlled iterations");
        // With 2 seeds x 6000 trials x 2 loaders (+ per-prefix truncations) this is > 24000.
    }

    // ----- Budgeted load --------------------------------------------------------------------

    [Fact]
    public void BudgetedLoad_HugeDeclaredNodeCount_DoesNotPreAllocate()
    {
        // Hand-built minimal headers that DECLARE int.MaxValue nodes but carry no node bytes.
        // The loader must reject via the budget exception before allocating anything.
        var dnnfHeader = BuildHugeCountHeader(CircuitEngineByte.Dnnf);
        var bddHeader = BuildHugeCountHeader(CircuitEngineByte.Bdd);

        Assert.Throws<NodeBudgetExceededException>(() => DnnfCircuit.Load(new MemoryStream(dnnfHeader)));
        Assert.Throws<NodeBudgetExceededException>(() => BinaryDecisionDiagram.Load(new MemoryStream(bddHeader)));
    }

    [Fact]
    public void BudgetedLoad_TinyBudget_AbortsRealBlob()
    {
        var dnnf = SaveDnnf(KnowledgeCompilation.CompileToDnnf(Parse("(a | b) & (c | d)")));
        var bdd = SaveBdd(BinaryDecisionDiagram.BuildWithBestOrder(Parse("(a | b) & (c | d)")));
        var tiny = new ResourceBudget { BddNodeLimit = 1 };

        Assert.Throws<NodeBudgetExceededException>(() => DnnfCircuit.Load(new MemoryStream(dnnf), tiny));
        Assert.Throws<NodeBudgetExceededException>(() => BinaryDecisionDiagram.Load(new MemoryStream(bdd), tiny));
    }

    [Fact]
    public void Load_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DnnfCircuit.Load(null!));
        Assert.Throws<ArgumentNullException>(() => BinaryDecisionDiagram.Load(null!));
    }

    [Fact]
    public void Load_HonoursCancellation()
    {
        var blob = SaveBdd(BinaryDecisionDiagram.BuildWithBestOrder(Parse("a & b & c")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            BinaryDecisionDiagram.Load(new MemoryStream(blob), null, cts.Token));
    }

    // ----- Helpers --------------------------------------------------------------------------

    private static class CircuitEngineByte
    {
        public const byte Bdd = 1;
        public const byte Dnnf = 2;
    }

    // magic "LOCX" + version 1 + engine + varCount 0 + nodeCount int.MaxValue (for BDD there are
    // zero variable-order entries in between, so the same trailing layout reaches nodeCount).
    private static byte[] BuildHugeCountHeader(byte engine)
    {
        var bytes = new List<byte> { 0x4C, 0x4F, 0x43, 0x58, 0x01, engine };
        bytes.AddRange(BitConverter.GetBytes(0)); // varCount = 0 (little-endian on this platform)
        bytes.AddRange(BitConverter.GetBytes(int.MaxValue)); // nodeCount = huge
        return bytes.ToArray();
    }

    private static void AssertControlled(Action load)
    {
        try
        {
            load();
        }
        catch (CircuitSerializationException)
        {
            // Documented malformed-input path.
        }
        catch (NodeBudgetExceededException)
        {
            // Documented budget-guard path.
        }
        catch (Exception ex)
        {
            Assert.Fail($"Load escaped with an unexpected {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static byte[] SaveDnnf(DnnfCircuit circuit)
    {
        using var stream = new MemoryStream();
        circuit.Save(stream);
        return stream.ToArray();
    }

    private static byte[] SaveBdd(BinaryDecisionDiagram bdd)
    {
        using var stream = new MemoryStream();
        bdd.Save(stream);
        return stream.ToArray();
    }

    private static DnnfCircuit DnnfRoundTrip(DnnfCircuit circuit)
    {
        return DnnfCircuit.Load(new MemoryStream(SaveDnnf(circuit)));
    }

    private static BinaryDecisionDiagram BddRoundTrip(BinaryDecisionDiagram bdd)
    {
        return BinaryDecisionDiagram.Load(new MemoryStream(SaveBdd(bdd)));
    }

    // Returns the committed golden bytes to compare against, or null when regenerating (in which
    // case the fresh bytes were written to the source tree and the caller should skip asserting).
    private static byte[]? GoldenBytes(string fileName, byte[] fresh)
    {
        var sourcePath = Path.Combine(FindProjectRoot(), "TestData", "Serialization", fileName);

        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, fresh);
            return null;
        }

        var copiedPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Serialization", fileName);
        var path = File.Exists(copiedPath) ? copiedPath : sourcePath;
        Assert.True(File.Exists(path),
            $"Golden sample {fileName} is missing; regenerate it with {RegenerateVariable}=1 and commit it.");
        return File.ReadAllBytes(path);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LogicalOptimizer.Tests.csproj")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new InvalidOperationException("Cannot locate the test project root");
    }
}
