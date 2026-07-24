using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     DRAT proof logging tests. Every proof is validated by an independent RUP checker:
///     an addition step C is accepted only if asserting ¬C and unit-propagating over the
///     original clauses plus all previously accepted (undeleted) steps yields a conflict;
///     deletion steps remove a clause from the database. An UNSAT proof must end with the
///     empty clause.
/// </summary>
public class DratProofTests
{
    /// <summary>Independent RUP (reverse unit propagation) validation of a DRAT proof.</summary>
    private static void AssertValidRupProof(IReadOnlyList<int[]> originalClauses,
        IReadOnlyList<SatProofStep> proof, int variableCount)
    {
        Assert.NotEmpty(proof);
        Assert.False(proof[^1].IsDeletion);
        Assert.Empty(proof[^1].Literals); // UNSAT proofs end with the empty clause

        var database = new List<int[]>(originalClauses);
        foreach (var step in proof)
            if (step.IsDeletion)
            {
                var target = new HashSet<int>(step.Literals);
                var position = database.FindIndex(c => c.Length == step.Literals.Length &&
                                                       target.SetEquals(c));
                Assert.True(position >= 0,
                    $"Deletion of unknown clause [{string.Join(",", step.Literals)}]");
                database.RemoveAt(position);
            }
            else
            {
                Assert.True(IsRupInferable(database, step.Literals, variableCount),
                    $"Proof step [{string.Join(",", step.Literals)}] is not RUP-inferable");
                database.Add(step.Literals);
            }
    }

    /// <summary>Assert the negation of the clause, unit-propagate naively, expect a conflict.</summary>
    private static bool IsRupInferable(List<int[]> database, int[] clause, int variableCount)
    {
        var assignment = new sbyte[variableCount + 1]; // 0 unknown, +1 true, -1 false
        foreach (var literal in clause)
        {
            var variable = Math.Abs(literal);
            var forced = literal > 0 ? (sbyte)-1 : (sbyte)1; // negate the clause literal
            if (assignment[variable] != 0 && assignment[variable] != forced) return true; // ¬C contradictory
            assignment[variable] = forced;
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var dbClause in database)
            {
                var unassigned = 0;
                var lastUnassigned = 0;
                var satisfied = false;
                foreach (var literal in dbClause)
                {
                    var value = assignment[Math.Abs(literal)];
                    var literalValue = literal > 0 ? value : (sbyte)-value;
                    if (literalValue > 0)
                    {
                        satisfied = true;
                        break;
                    }

                    if (literalValue == 0)
                    {
                        unassigned++;
                        lastUnassigned = literal;
                    }
                }

                if (satisfied) continue;
                if (unassigned == 0) return true; // conflict reached
                if (unassigned == 1)
                {
                    assignment[Math.Abs(lastUnassigned)] = lastUnassigned > 0 ? (sbyte)1 : (sbyte)-1;
                    changed = true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void Proof_ContradictoryUnits_EndsWithEmptyClause()
    {
        var solver = new SatSolver(1);
        solver.AddClause(1);
        solver.AddClause(-1);
        solver.EnableProofLogging();

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
        AssertValidRupProof(new List<int[]> { new[] { 1 }, new[] { -1 } }, solver.Proof!, 1);
    }

    [Fact]
    public void Proof_Pigeonhole_ValidRupChain()
    {
        var clauses = new List<int[]>();
        for (var pigeon = 0; pigeon < 3; pigeon++)
            clauses.Add(new[] { 2 * pigeon + 1, 2 * pigeon + 2 });
        for (var hole = 1; hole <= 2; hole++)
            for (var a = 0; a < 3; a++)
                for (var b = a + 1; b < 3; b++)
                    clauses.Add(new[] { -(2 * a + hole), -(2 * b + hole) });

        var solver = new SatSolver(6);
        foreach (var clause in clauses) solver.AddClause(clause);
        solver.EnableProofLogging();

        Assert.Equal(SatResult.Unsatisfiable, solver.Solve());
        AssertValidRupProof(clauses, solver.Proof!, 6);
    }

    [Fact]
    public void Proof_RandomUnsatInstances_AllRupValid()
    {
        var random = new Random(777);
        var proofsChecked = 0;

        while (proofsChecked < 25)
        {
            var variableCount = random.Next(4, 10);
            var clauses = new List<int[]>();
            var clauseCount = variableCount * 5; // dense: usually UNSAT
            for (var c = 0; c < clauseCount; c++)
            {
                var length = random.Next(1, 4);
                var clause = new int[length];
                for (var k = 0; k < length; k++)
                {
                    var variable = random.Next(1, variableCount + 1);
                    clause[k] = random.Next(2) == 0 ? variable : -variable;
                }

                clauses.Add(clause);
            }

            var solver = new SatSolver(variableCount);
            foreach (var clause in clauses) solver.AddClause(clause);
            solver.EnableProofLogging();

            if (solver.Solve() != SatResult.Unsatisfiable) continue;

            // The solver normalizes clauses (dedup, tautology removal); the checker's
            // database starts from the ORIGINAL list, which is logically no stronger,
            // but deletion steps reference normalized clauses — validate against the
            // solver's own working set instead
            AssertValidRupProof(NormalizedClauses(clauses), solver.Proof!, variableCount);
            proofsChecked++;
        }
    }

    private static List<int[]> NormalizedClauses(List<int[]> clauses)
    {
        var result = new List<int[]>();
        foreach (var clause in clauses)
        {
            var normalized = new List<int>();
            var tautology = false;
            foreach (var literal in clause)
            {
                if (normalized.Contains(-literal))
                {
                    tautology = true;
                    break;
                }

                if (!normalized.Contains(literal)) normalized.Add(literal);
            }

            if (!tautology) result.Add(normalized.ToArray());
        }

        return result;
    }

    [Fact]
    public void CheckWithProof_EquivalentPair_ProofValidatesAgainstMiter()
    {
        var names = Enumerable.Range(1, 14).Select(i => $"v{i:D2}").ToList();
        var left = new Parser(new Lexer("!(" + string.Join(" & ", names) + ")").Tokenize()).Parse();
        var right = new Parser(new Lexer(string.Join(" | ", names.Select(n => $"!{n}"))).Tokenize()).Parse();

        var (result, miter, proof) = EquivalenceChecker.CheckWithProof(left, right);

        Assert.True(result.AreEquivalent);
        Assert.NotNull(proof);

        var proofSteps = proof!.Split('\n')
            .Select(line =>
            {
                var isDeletion = line.StartsWith("d ");
                var body = isDeletion ? line[2..] : line;
                var literals = body.Split(' ').Select(int.Parse).TakeWhile(l => l != 0).ToArray();
                return new SatProofStep(isDeletion, literals);
            })
            .ToList();
        AssertValidRupProof(miter.Clauses.ToList(), proofSteps, miter.TotalVariableCount);
    }

    [Fact]
    public void CheckWithProof_NonEquivalentPair_NoProofButCounterexample()
    {
        var (result, _, proof) = EquivalenceChecker.CheckWithProof(
            new Parser(new Lexer("a | b").Tokenize()).Parse(),
            new Parser(new Lexer("a & b").Tokenize()).Parse());

        Assert.False(result.AreEquivalent);
        Assert.Null(proof);
        Assert.NotNull(result.Counterexample);
    }

    [Fact]
    public void ToDrat_WithoutEnabling_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new SatSolver(1).ToDrat());
    }
}
