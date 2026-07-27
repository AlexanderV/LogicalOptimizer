using System.Globalization;

namespace LogicalOptimizer.Tests;

/// <summary>One generated corpus instance: its file name and DIMACS text.</summary>
internal sealed record CorpusInstance(string FileName, string Dimacs, bool ExpectedSat, string Kind);

/// <summary>
///     Deterministic generator for the vendored SATLIB-STYLE SAT corpus
///     (LogicalOptimizer.Benchmarks/SatCorpus). These are GENERATED instances, not the
///     literal SATLIB uf/uuf archive files, chosen so every expected verdict is known by
///     construction (or independently brute-force-verified at generation time):
///     <list type="bullet">
///       <item>
///         <b>uf* (SAT):</b> planted random 3-SAT at the phase-transition ratio ≈ 4.26.
///         A random assignment is planted first and every clause is forced to keep at
///         least one literal satisfied by it, so the planted assignment satisfies the
///         formula — SATISFIABLE by construction.
///       </item>
///       <item>
///         <b>uuf* (UNSAT, random):</b> uniform random 3-SAT near the transition ratio,
///         seed-searched until an exhaustive 2^n brute-force oracle confirms UNSAT.
///         Kept small (≤ 18 vars) so the brute-force verification is independent of the
///         solver under test.
///       </item>
///       <item>
///         <b>uuf-php* (UNSAT, structured):</b> pigeonhole PHP(n+1 → n) — n+1 pigeons in
///         n holes — which is UNSATISFIABLE by construction and exponentially hard for
///         resolution/CDCL, exercising the learnt-clause machinery.
///       </item>
///     </list>
///     All randomness comes from <see cref="Random" /> seeded with the constants below,
///     which use the framework's reproducible seeded algorithm; the produced files are
///     also vendored, so the corpus is fixed regardless of RNG stability.
/// </summary>
internal static class SatCorpusGenerator
{
    /// <summary>Clause/variable ratio at the random 3-SAT hardness peak.</summary>
    public const double PhaseTransitionRatio = 4.26;

    private const int SatBaseSeed = 12_000;
    private const int UnsatBaseSeed = 71_000;

    // Planted-SAT variable counts (uf-style), spread across the requested 75..125 band.
    private static readonly int[] SatVariableCounts = { 75, 80, 90, 95, 100, 105, 110, 115, 120, 125 };

    // Small forced-UNSAT random 3-SAT sizes (uuf-style); kept ≤ 18 for brute-force checking.
    private static readonly int[] UnsatVariableCounts = { 16, 16, 17, 17, 18, 18 };

    // Pigeonhole hole counts n → PHP(n+1 → n). n+1 pigeons, n holes, (n+1)*n variables.
    private static readonly int[] PigeonholeHoleCounts = { 4, 5, 6, 7 };

    public static IReadOnlyList<CorpusInstance> Generate()
    {
        var instances = new List<CorpusInstance>();

        // ---- SAT: planted random 3-SAT at the phase transition ----
        for (var i = 0; i < SatVariableCounts.Length; i++)
        {
            var variables = SatVariableCounts[i];
            var seed = SatBaseSeed + i;
            var clauses = PlantedThreeSat(variables, seed, PhaseTransitionRatio);
            var comments = new[]
            {
                Kv("source", "SATLIB-style GENERATED (not the literal SATLIB archive)"),
                Kv("generator", "LogicalOptimizer.Tests SatCorpusGenerator"),
                Kv("kind", "planted-3sat"),
                Kv("expected", "SAT"),
                Kv("variables", variables.ToString()),
                Kv("clauses", clauses.Count.ToString()),
                Kv("ratio", PhaseTransitionRatio.ToString("0.00", CultureInfo.InvariantCulture)),
                Kv("seed", seed.ToString()),
                Kv("note", "SAT by construction: planted assignment satisfies every clause")
            };
            var name = $"uf{variables:D3}-01.cnf";
            instances.Add(new CorpusInstance(name, DimacsCnf.Write(variables, clauses, comments), true,
                "planted-3sat"));
        }

        // ---- UNSAT: forced random 3-SAT, brute-force-verified at generation time ----
        for (var i = 0; i < UnsatVariableCounts.Length; i++)
        {
            var variables = UnsatVariableCounts[i];
            var startSeed = UnsatBaseSeed + i * 1000;
            var (clauses, seed) = ForcedUnsatThreeSat(variables, startSeed, PhaseTransitionRatio);
            var comments = new[]
            {
                Kv("source", "SATLIB-style GENERATED (not the literal SATLIB archive)"),
                Kv("generator", "LogicalOptimizer.Tests SatCorpusGenerator"),
                Kv("kind", "random-3sat-forced-unsat"),
                Kv("expected", "UNSAT"),
                Kv("variables", variables.ToString()),
                Kv("clauses", clauses.Count.ToString()),
                Kv("ratio", PhaseTransitionRatio.ToString("0.00", CultureInfo.InvariantCulture)),
                Kv("seed", seed.ToString()),
                Kv("note", "UNSAT verified by exhaustive 2^n brute force at generation time")
            };
            var name = $"uuf{variables:D3}-{i + 1:D2}.cnf";
            instances.Add(new CorpusInstance(name, DimacsCnf.Write(variables, clauses, comments), false,
                "random-3sat-forced-unsat"));
        }

        // ---- UNSAT: pigeonhole PHP(n+1 → n), provably UNSAT and hard for CDCL ----
        foreach (var holes in PigeonholeHoleCounts)
        {
            var pigeons = holes + 1;
            var (variables, clauses) = Pigeonhole(holes);
            var comments = new[]
            {
                Kv("source", "SATLIB-style GENERATED (not the literal SATLIB archive)"),
                Kv("generator", "LogicalOptimizer.Tests SatCorpusGenerator"),
                Kv("kind", "pigeonhole-php"),
                Kv("expected", "UNSAT"),
                Kv("variables", variables.ToString()),
                Kv("clauses", clauses.Count.ToString()),
                Kv("pigeons", pigeons.ToString()),
                Kv("holes", holes.ToString()),
                Kv("note", "PHP(n+1 -> n): UNSAT by construction (pigeons exceed holes)")
            };
            var name = $"uuf-php{pigeons}-{holes}.cnf";
            instances.Add(new CorpusInstance(name, DimacsCnf.Write(variables, clauses, comments), false,
                "pigeonhole-php"));
        }

        return instances;
    }

    private static KeyValuePair<string, string> Kv(string k, string v)
    {
        return new KeyValuePair<string, string>(k, v);
    }

    /// <summary>
    ///     Planted random 3-SAT: a random assignment is fixed, then each clause is drawn
    ///     uniformly and, if the planted assignment would falsify it, one literal is set to
    ///     its satisfying polarity. Result is satisfiable by the planted assignment.
    /// </summary>
    private static List<int[]> PlantedThreeSat(int variables, int seed, double ratio)
    {
        var random = new Random(seed);
        var planted = new bool[variables + 1];
        for (var v = 1; v <= variables; v++) planted[v] = random.Next(2) == 1;

        var clauseCount = (int)Math.Round(ratio * variables);
        var clauses = new List<int[]>(clauseCount);
        for (var c = 0; c < clauseCount; c++)
        {
            var vars = PickDistinctVariables(random, variables, 3);
            var clause = new int[3];
            for (var k = 0; k < 3; k++) clause[k] = random.Next(2) == 0 ? vars[k] : -vars[k];

            var satisfied = clause.Any(l => l > 0 == planted[Math.Abs(l)]);
            if (!satisfied)
            {
                var j = random.Next(3);
                var variable = Math.Abs(clause[j]);
                clause[j] = planted[variable] ? variable : -variable;
            }

            clauses.Add(clause);
        }

        return clauses;
    }

    /// <summary>
    ///     Search seeds from <paramref name="startSeed" /> upward for a uniform random
    ///     3-SAT instance the brute-force oracle confirms UNSAT; returns it with the seed.
    /// </summary>
    private static (List<int[]> Clauses, int Seed) ForcedUnsatThreeSat(int variables, int startSeed, double ratio)
    {
        for (var seed = startSeed; seed < startSeed + 1000; seed++)
        {
            var clauses = UniformThreeSat(variables, seed, ratio);
            if (!SatTestOracles.BruteForceSatisfiable(variables, clauses))
                return (clauses, seed);
        }

        throw new InvalidOperationException(
            $"No UNSAT instance found for {variables} variables from seed {startSeed}");
    }

    private static List<int[]> UniformThreeSat(int variables, int seed, double ratio)
    {
        var random = new Random(seed);
        var clauseCount = (int)Math.Round(ratio * variables);
        var clauses = new List<int[]>(clauseCount);
        for (var c = 0; c < clauseCount; c++)
        {
            var vars = PickDistinctVariables(random, variables, 3);
            var clause = new int[3];
            for (var k = 0; k < 3; k++) clause[k] = random.Next(2) == 0 ? vars[k] : -vars[k];
            clauses.Add(clause);
        }

        return clauses;
    }

    private static int[] PickDistinctVariables(Random random, int variables, int count)
    {
        var chosen = new List<int>(count);
        while (chosen.Count < count)
        {
            var v = random.Next(1, variables + 1);
            if (!chosen.Contains(v)) chosen.Add(v);
        }

        return chosen.ToArray();
    }

    /// <summary>
    ///     Pigeonhole PHP(n+1 → n): variable index for "pigeon p in hole h" is
    ///     (p-1)*n + h, p in 1..n+1, h in 1..n. Each pigeon occupies at least one hole,
    ///     and no hole holds two pigeons.
    /// </summary>
    private static (int Variables, List<int[]> Clauses) Pigeonhole(int holes)
    {
        var pigeons = holes + 1;
        var clauses = new List<int[]>();

        int Var(int pigeon, int hole)
        {
            return (pigeon - 1) * holes + hole;
        }

        for (var p = 1; p <= pigeons; p++)
            clauses.Add(Enumerable.Range(1, holes).Select(h => Var(p, h)).ToArray());

        for (var h = 1; h <= holes; h++)
            for (var p = 1; p <= pigeons; p++)
                for (var q = p + 1; q <= pigeons; q++)
                    clauses.Add(new[] { -Var(p, h), -Var(q, h) });

        return (pigeons * holes, clauses);
    }
}
