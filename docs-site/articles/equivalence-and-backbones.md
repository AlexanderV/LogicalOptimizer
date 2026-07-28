# Equivalence & Backbones

The facade exposes SAT-backed formula analysis (`FormulaAnalysis`) and equivalence
checking (`EquivalenceChecker` plus pluggable implementations). Every example is asserted
in `LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

## Backbone, model enumeration, backbone simplification

`FormulaAnalysis` works on any `AstNode` and scales past the truth-table range by using
the built-in SAT solver.

```csharp
using LogicalOptimizer;

var f = new FormulaFactory();

// Backbone: variables forced to a fixed value in every model.
var backbone = FormulaAnalysis.ComputeBackbone(f.Parse("a & (b | c)"));
Console.WriteLine(backbone.IsSatisfiable);                       // True
Console.WriteLine(backbone.ForcedVariables!["a"]);               // True  (a is forced)
// b and c are free, so they do not appear in ForcedVariables.

// Lazy, projected model enumeration (blocking-clause based).
int models = FormulaAnalysis.EnumerateModels(f.Parse("a | b")).Count();  // 3

// Substitute the backbone and re-attach the forced literals.
var simplified = FormulaAnalysis.SimplifyWithBackbone(f.Parse("(a | b) & !a & (c | d)"));
// equivalent to the input, with the forced part propagated
```

`ComputeBackbone` returns a `BackboneResult` (`IsSatisfiable`, `ForcedVariables`);
`EnumerateModels` yields projected assignments lazily up to `maxModels`; both honor a
conflict budget and cancellation token.

## Equivalence checking with counterexamples

`EquivalenceChecker.Check` proves or refutes equivalence via an XOR-miter and the SAT
solver, returning a distinguishing assignment when the formulas differ:

```csharp
EquivalenceChecker.Check("a & b | a & c", "a & (b | c)").AreEquivalent;  // True

var diff = EquivalenceChecker.Check("a & b", "a | b");
Console.WriteLine(diff.AreEquivalent);   // False
Console.WriteLine(diff.Counterexample);  // e.g. a=True, b=False  (a&b false, a|b true)
```

`Check` has string and `AstNode` overloads and a `maxConflicts` bound;
`EquivalenceCheckResult.AreEquivalent` is `null` if the bound was hit before a verdict.

## Equivalence with a DRAT proof

For an *equivalent* pair the miter is UNSAT, and `CheckWithProof` returns the miter CNF
plus an externally checkable **DRAT** certificate:

```csharp
var (result, miterCnf, drat) = EquivalenceChecker.CheckWithProof(f.Parse("a & b"), f.Parse("b & a"));
Console.WriteLine(result.AreEquivalent);  // True
Console.WriteLine(drat is not null);      // True  (proof present for the UNSAT miter)
```

## Pluggable equivalence checkers

`IEquivalenceChecker` has three implementations you can choose by engine:

```csharp
new BddEquivalenceChecker().Check(f.Parse("a & b"), f.Parse("b & a")).AreEquivalent;    // True (BDD)
new HybridEquivalenceChecker().Check(f.Parse("a | b"), f.Parse("b | a")).AreEquivalent; // True (SAT + BDD fallback)
```

- `EquivalenceChecker` (static) — SAT-miter with optional DRAT proof.
- `BddEquivalenceChecker` — canonical ROBDD comparison under a node budget.
- `HybridEquivalenceChecker` — SAT first, BDD as a fallback engine.

## Next steps

- [SAT solving](sat-solving.md) — the engine underneath.
- [Binary decision diagrams](bdd.md) — the BDD engine used by `BddEquivalenceChecker`.
- [Operation contracts & statuses](contracts-and-statuses.md) — mandatory verification of every optimize.
