# Operation Contracts & Statuses

LogicalOptimizer never claims more than it can prove. Two enums make the guarantees
explicit on every result object, and **every** returned optimization is verified
equivalent to its input.

## `MinimizationStatus`

`OptimizationResult.MinimizationStatus` records the provenance of the minimality claim.
It refers to the two-level cover cost model: **total literals first, then term count**.
The returned optimized (multi-level) expression never has more literals than that cover.

Its scope is the **SOP** side — the `Optimized` expression and the `DNF` artifact. The
equivalent-CNF (POS) artifact has its own, independent
[`CnfMinimizationStatus`](#cnfminimizationstatus) because its cover search can hit a budget
separately.

| Value | Meaning |
|---|---|
| `MinimalProven` | The minimum-cover search completed: the result is **provably minimal**. The normal case for ≤ 10 variables. |
| `BudgetExceeded` | The exact search ran but hit a work budget: the result is sound and usually minimal, but optimality was **not proven**. |
| `Heuristic` | Outside the exact range (or the exact attempt fell back): rule-based simplification only. |

There are **no silent fallbacks** — a downgrade always shows up as a status change, never
as a quietly worse answer dressed up as minimal.

## What "provably minimal" means, and its zone

"Provably minimal" is a statement about the two-level cover: the exact Quine–McCluskey
backend (covering-table reductions + lower-bound-pruned branch-and-bound) searched the
whole space and found a cover of minimum literal count (then minimum term count). It is
**not** a claim about minimal gate count, circuit depth, or delay.

The proof is attempted by variable count:

| Variables | Behavior | Typical status |
|---|---|---|
| **≤ 10** | Exact QM, unbounded cover search — minimality **guaranteed** (verified for every 3- and 4-variable function). | `MinimalProven` |
| **11–12** | Exact QM under work budgets. | `MinimalProven` or `BudgetExceeded` |
| **13–24** | SAT-based prime-cover SOP (no 2ⁿ truth table); adopted only after a SAT-miter equivalence proof. | `Heuristic` |
| **> 24** | Local subcircuit rewriting + Espresso-lite cube-list heuristics (EXPAND / IRREDUNDANT / REDUCE). | `Heuristic` |

See [Resource budgets & the zone model](budgets-and-zones.md) for the routing details.

## Mandatory equivalence verification

Independently of the minimality status, **every** optimization is verified equivalent to
the input before it is returned:

- **≤ 12 variables** — by exhaustive truth table.
- **> 12 variables** — by the built-in CDCL SAT solver (an XOR-miter proof).

A rewrite is accepted only when equivalence is **positively proven**. If verification cannot
prove it — either it refutes the result with a counterexample (which would be an optimizer
bug) or, beyond the truth-table range, the SAT proof exhausts its budget (an `Unknown`
verdict) — the facade **rolls back to the input** and records a `SoundnessRollback` metric
rather than ship an unverified answer. You can also check it yourself, as a bool or
three-valued:

```csharp
var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");
Console.WriteLine(result.IsEquivalent());                    // True
// CheckEquivalence keeps the full verdict that IsEquivalent collapses into false:
Console.WriteLine(result.CheckEquivalence().AreEquivalent);  // true / false / null (Unknown)
```

For UNSAT verdicts (including equivalence proofs via `EquivalenceChecker.CheckWithProof`)
the SAT engine can emit an externally checkable **DRAT** certificate.

## `ComputationStatus`

Some result fields are potentially expensive to compute (for example a full truth table
for many variables). `ComputationStatus` reports what happened for such a field:

| Value | Meaning |
|---|---|
| `Computed` | The value was computed and is present. |
| `TooLarge` | The computation was skipped because the input exceeded the size limit for it. |
| `NotRequested` | The caller did not ask for this value. |

This lets a consumer distinguish "we tried and it did not fit" from "we never asked" —
again, no silent blanks.

## `CnfMinimizationStatus`

`OptimizationResult.CnfMinimizationStatus` reports the minimality provenance of the
**equivalent-CNF (POS)** artifact specifically — kept separate from the SOP-scoped
[`MinimizationStatus`](#minimizationstatus) because the POS minimum-cover search can hit its
budget independently of the SOP one. It reuses the `MinimizationStatus` enum:

| Value | Meaning |
|---|---|
| `MinimalProven` | The POS minimum-cover search completed: the equivalent CNF is **provably minimal** (the normal case in the ≤ 10 guarantee zone). |
| `BudgetExceeded` | The exact POS search ran but hit the cover-step budget (possible at 11–12 variables): sound, but not proven minimal. |
| `Heuristic` | No exact equivalent CNF was produced — the heuristic zone, `CnfMode.Tseitin` (an *equisatisfiable* CNF, where two-level POS minimality does not apply), CNF not requested, or a `TooLarge` result. |

So a caller can trust "provably minimal POS" only when `CnfMinimizationStatus` is
`MinimalProven`; for `CnfMode.Tseitin` the value is always `Heuristic` by design.

## Budgets never produce wrong answers

Exhausting any [`ResourceBudget`](budgets-and-zones.md) limit never yields an incorrect
result. Each engine either falls back (heuristic simplification, a rollback to the input, or
an `Unknown` verdict from a standalone equivalence check) or throws a **dedicated** budget/size
exception — `ComputationBudgetExceededException`, `NodeBudgetExceededException` or
`NormalFormTooLargeException` (all derive from `InvalidOperationException`) — and the
minimality status reflects the outcome.
