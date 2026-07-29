# SAT Solving, Cardinality, PB & MaxSAT

`LogicalOptimizer.Sat` is a dependency-free CDCL SAT stack: two-watched literals, 1UIP
learning, heap-VSIDS, Luby restarts, LBD clause-database reduction and subsumption
preprocessing, plus incremental solving, unsat cores, DRAT proofs, cardinality /
pseudo-Boolean constraints and weighted partial MaxSAT. Every example is asserted in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

Literals are 1-based signed integers: `k` is variable *k* positive, `-k` negative.

## Solving and reading a model

```csharp
using LogicalOptimizer;

var solver = new SatSolver(3);
solver.AddClause(1, 2);    // a | b
solver.AddClause(-1, 3);   // !a | c

if (solver.Solve() == SatResult.Satisfiable)
{
    bool a = solver.GetValue(1);
    bool b = solver.GetValue(2);
    bool c = solver.GetValue(3);
    // a|b and !a|c both hold for the recovered assignment
}
```

`Solve(maxConflicts, ct)` bounds the search; it returns `SatResult.Unknown` if the
conflict budget is exhausted before a verdict.

## Incremental solving under assumptions

`Solve(assumptions, ...)` solves the same clause database under temporary unit
assumptions, reusing learned clauses between calls:

```csharp
var solver = new SatSolver(2);
solver.AddClause(1, 2);                 // a | b
solver.Solve(new[] { -1 });             // assume !a  -> Satisfiable
Console.WriteLine(solver.GetValue(2));  // True  (b forced)
```

## UNSAT cores and DRAT proofs

```csharp
var solver = new SatSolver(1);
solver.EnableProofLogging();            // enable DRAT logging before solving
solver.AddClause(1);
solver.AddClause(-1);
solver.Solve();                         // Unsatisfiable
Console.WriteLine(solver.ToDrat());     // DRAT certificate deriving the empty clause
// solver.UnsatCore exposes the failing assumption subset for assumption-based UNSAT.
```

## Building CNF with `CnfBuilder`

`CnfBuilder` allocates variables (`NewVariable`) and clauses, then hands you a solver
(`ToSolver`). It is also the target for the constraint encoders below.

## Cardinality constraints

`CardinalityEncoder` adds sequential-counter (Sinz) AtMost / AtLeast / Exactly-`k`
constraints onto a `CnfBuilder`:

```csharp
var builder = new CnfBuilder(4);
CardinalityEncoder.AtMostK(builder, new[] { 1, 2, 3, 4 }, 1);  // at most one true
var solver = builder.ToSolver();
solver.AddClause(1);
solver.AddClause(2);            // force two true -> contradicts AtMost(1)
solver.Solve();                // Unsatisfiable

var exactly = new CnfBuilder(3);
CardinalityEncoder.ExactlyK(exactly, new[] { 1, 2, 3 }, 2);
exactly.ToSolver().Solve();    // Satisfiable
```

## Pseudo-Boolean constraints

`PseudoBooleanEncoder` handles weighted sums (`Σ wᵢ·xᵢ ≤ bound`, and `AtLeast`):

```csharp
var builder = new CnfBuilder(3);
PseudoBooleanEncoder.AtMost(builder, new[] { 1, 2, 3 }, new long[] { 2, 3, 4 }, bound: 5);
builder.ToSolver().Solve();    // Satisfiable
```

## Encoding portfolio

Every encoder method has an opt-in overload that selects the CNF encoding and returns an
`EncodingStats` (clauses and auxiliary variables introduced). All encodings are semantically
equivalent — they trade size against propagation strength. Cardinality offers `Pairwise`,
`SequentialCounter` (the default), `Product` (at-most-one) and `Totalizer`; pseudo-Boolean
offers `DynamicProgramming` (the default), `BinaryMerge` and `GeneralizedTotalizer`. The
**parameterless** methods above keep their exact default output; the portfolio is additive.

```csharp
var builder = new CnfBuilder(10);
var atMostOne = Enumerable.Range(1, 10).ToList();

// Pick an encoding explicitly and read back its size:
var stats = CardinalityEncoder.AtMostK(builder, atMostOne, 1, CardinalityEncoding.Product);
Console.WriteLine(stats);                 // e.g. "29 clauses, 7 aux vars"

// Or let Auto measure the applicable encodings and keep the smallest (never larger than the
// default, deterministic within a release):
var auto = CardinalityEncoder.AtMostK(new CnfBuilder(10), atMostOne, 1, CardinalityEncoding.Auto);
```

## Weighted partial MaxSAT

`MaxSatSolver` maximizes satisfied soft-clause weight subject to hard clauses:

```csharp
var maxSat = new MaxSatSolver(2);
maxSat.AddHard(1, 2);          // a | b must hold
maxSat.AddSoft(3, -1);         // prefer a = false (weight 3)
maxSat.AddSoft(4, -2);         // prefer b = false (weight 4)

var result = maxSat.Solve();
Console.WriteLine(result.Status);       // Optimal
Console.WriteLine(result.Cost);         // 3   (violate only the cheaper soft clause)
Console.WriteLine(result.GetValue(1));  // True
Console.WriteLine(result.GetValue(2));  // False
```

`MaxSatStatus` is `Optimal`, `HardClausesUnsatisfiable`, or `Unknown`.

Two algorithms are available. The parameterless `Solve(...)` above runs the linear
search and is unchanged; the overload `Solve(MaxSatAlgorithm, ...)` also offers a
core-guided (MSU3-style) search that extracts UNSAT cores and raises a proven lower
bound round by round:

```csharp
var result = maxSat.Solve(MaxSatAlgorithm.CoreGuided);
Console.WriteLine(result.Status);        // Optimal
Console.WriteLine(result.Cost);          // 3
Console.WriteLine(result.LowerBound);    // 3   (== UpperBound when proven optimal)
```

Both return the same proven optimum. `MaxSatResult.LowerBound` and `UpperBound`
bracket the optimum; when a conflict budget is spent the result is `Unknown` with a
sound incumbent (`LowerBound < UpperBound`) — an incumbent is never reported as
`Optimal`, and `HardClausesUnsatisfiable` is distinct from budget exhaustion.

## Solving a formula's CNF directly

`SatSolver.FromCnf` builds a solver straight from an equisatisfiable
[`TseitinCnf`](normal-forms.md#equisatisfiable-tseitin-cnf):

```csharp
var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf("a & b");
SatSolver.FromCnf(cnf).Solve();   // Satisfiable
```

## Next steps

- [Equivalence & backbones](equivalence-and-backbones.md) — backbones, model enumeration, SAT-miter equivalence.
- [Normal forms & transformations](normal-forms.md) — where the Tseitin CNF comes from.
