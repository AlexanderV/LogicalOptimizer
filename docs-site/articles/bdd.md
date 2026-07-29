# Binary Decision Diagrams (ROBDD)

`LogicalOptimizer.Bdd` is a reduced, ordered BDD engine with hash-consing and canonical
complement edges: model counting, lazy satisfying-assignment enumeration, tautology /
contradiction tests, equivalence checking and variable-order optimization
(`BuildWithBestOrder` heuristics and `BuildWithSiftedOrder` Rudell-style sifting), all
under a node budget. Every example is asserted in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

## Build, count, evaluate, enumerate

```csharp
using LogicalOptimizer;

var f = new FormulaFactory();
var bdd = BinaryDecisionDiagram.BuildWithBestOrder(f.Parse("a & b | c"));

System.Numerics.BigInteger count = bdd.CountSatisfyingAssignments();  // 5
int enumerated = bdd.EnumerateSatisfyingAssignments().Count();        // 5 (lazy)

bool value = bdd.Evaluate(new Dictionary<string, bool>
{
    ["a"] = true, ["b"] = true, ["c"] = false
});                                                                    // True

IReadOnlyDictionary<string, bool> witness = bdd.FindSatisfyingAssignment();

Console.WriteLine(bdd.NodeCount);                       // reachable node count
Console.WriteLine(string.Join(",", bdd.Variables));     // variable order used
```

`CountSatisfyingAssignments` returns a `BigInteger`, so it stays exact past 64 variables.
`EnumerateSatisfyingAssignments` is lazy — you can take the first few models of a formula
with astronomically many.

## Tautology and contradiction

```csharp
var tautology = BinaryDecisionDiagram.BuildWithBestOrder(
    new OrNode(new VariableNode("a"), new NotNode(new VariableNode("a"))));
tautology.IsTautology();       // True

var contradiction = BinaryDecisionDiagram.BuildWithBestOrder(
    new AndNode(new VariableNode("a"), new NotNode(new VariableNode("a"))));
contradiction.IsContradiction();  // True
```

(These are built through the AST node types directly because the parser constant-folds
`a | !a` to `1` and `a & !a` to `0`.)

## Equivalence via canonical form

Because a ROBDD is canonical, two equivalent formulas build the same diagram:

```csharp
BinaryDecisionDiagram.AreEquivalent(f.Parse("a & b | a & c"), f.Parse("a & (b | c)")); // True
```

## Variable ordering

BDD size is order-sensitive. `BuildWithBestOrder` picks a good static order by heuristic;
`BuildWithSiftedOrder` runs Rudell-style dynamic sifting (bounded by `maxRebuilds`). Both
produce the same canonical function — only the node count differs:

```csharp
var sifted = BinaryDecisionDiagram.BuildWithSiftedOrder(f.Parse("a & b | c & d"));
sifted.CountSatisfyingAssignments();  // 7
```

Every builder takes an optional `nodeBudget` (default `DefaultNodeBudget = 1_000_000`) and
`CancellationToken`; exceeding the budget throws rather than exhausting memory.

## Serialization (experimental)

A built diagram can be persisted to a compact binary blob and read back into a valid
hash-consed manager, so a service can compile once and reuse across restarts:

```csharp
using var file = File.Create("diagram.locx");
bdd.Save(file);
// ...later / another process:
var reloaded = BinaryDecisionDiagram.Load(File.OpenRead("diagram.locx"));
reloaded.CountSatisfyingAssignments();  // identical to the original
```

The format is **experimental until v4** (no cross-version guarantee beyond the version gate,
which refuses — never misreads — a blob from a newer build). It is deterministic and
little-endian, stores both variable identities and the current variable order, and `Load`
is CRC-32 checked, structurally validated (a valid ordered/reduced diagram) and budgeted:
a hostile size header aborts with `NodeBudgetExceededException` instead of pre-allocating,
and any malformed input is a `CircuitSerializationException`. There is no reflection or
object deserialization. The engine byte means a d-DNNF blob loaded here is a typed error.

## Related

For exact **#SAT** and weighted model counting on a compiled circuit (rather than a BDD),
see [Knowledge compilation & model counting](knowledge-compilation.md). For a
BDD-backed equivalence checker, see
[`BddEquivalenceChecker`](equivalence-and-backbones.md#pluggable-equivalence-checkers).
