# Decision record: projected model counting public API (P1.4)

**Status:** Accepted — resolves the open API questions from
[`POST_V3_ROADMAP.md`](../../POST_V3_ROADMAP.md) §9. Targets **v3.3**. No API is shipped by
this record; it fixes the contract the v3.3 implementation must follow so that the public
surface is not left open (roadmap §16 decision 4, §17 decision 4).

**Basis:** the completed design spike
[`doc/spikes/projected-model-counting.md`](../spikes/projected-model-counting.md) — SAT
blocking enumeration and BDD existential abstraction, validated against an independent
brute-force projected-and-deduplicated oracle on all 1,048,576 exhaustive ≤4-variable
checks plus randomized and edge cases.

Projected model counting = the number of **distinct assignments over a chosen subset of
variables** `P`. The trap (spike §2): different full models can collapse to one projected
model, so dropping literals or summing OR-branch counts overcounts. Every decision below
keeps the result sound against that trap or withholds it with an honest status.

## Committed public API (to implement in v3.3)

```csharp
namespace LogicalOptimizer;

public enum ProjectedCountStatus
{
    Exact,           // the count is exact
    BudgetExhausted  // a budget stopped the computation before an exact count was reached
}

public sealed class ProjectedModelCountResult
{
    public System.Numerics.BigInteger? Count { get; }   // non-null iff Status == Exact
    public ProjectedCountStatus Status { get; }
}

public static class FormulaAnalysis   // facade analysis surface (with ComputeBackbone / EnumerateModels)
{
    public static ProjectedModelCountResult CountProjectedModels(
        AstNode formula,
        IReadOnlyCollection<string> projectedVariables,
        ResourceBudget? budget = null,
        CancellationToken cancellationToken = default);
}
```

**Placement (settled at implementation, v3.1 working tree).** The MVP engine is SAT blocking
enumeration over the formula's Tseitin CNF and does **not** touch the compiled d-DNNF, so the
entry point ships as a static method on the facade's **`FormulaAnalysis`** — beside the other
SAT-backed queries `ComputeBackbone` / `EnumerateModels` — rather than the `DnnfCircuit`
extension sketched above. This keeps the SAT engine out of the `LogicalOptimizer.Dnnf` package
and keeps the query discoverable next to its siblings. The public result types
`ProjectedModelCountResult` and `ProjectedCountStatus { Exact, BudgetExhausted }` live in the
facade (`namespace LogicalOptimizer`). The **shape and contract** below are as fixed here; only
the host type moved from the sketch. When the exact d-DNNF/BDD path lands, it can add a
`DnnfCircuit`-level entry or an engine parameter without disturbing this facade method.

## Resolutions

### 1. Scope semantics — may `P` contain variables outside the formula?

**Decision.** `projectedVariables` **may** include names not occurring in the formula. Each
such free projection variable multiplies the projected count by 2 (both of its values are
distinct projected assignments), exactly as the spike oracle defines it. An **empty**
projection returns `1` for a satisfiable formula and `0` for an unsatisfiable one. A name
that is neither in the formula nor a syntactically valid variable is still accepted (it is
simply free); there is **no** "unknown variable" argument error, because the projection
universe is the union of the formula's variables and `P`.

**Rationale.** This matches the validated spike semantics, keeps the function total (no
surprising exceptions on caller-supplied scopes), and gives the clean algebraic laws
`project-all == CountModels()` and `project-∅ == {0,1}` that the acceptance tests assert.
The behaviour is documented on the method; callers who want strict membership can intersect
`P` with the formula's variables themselves.

### 2. Result shape — nullable count + status vs. discriminated result

**Decision.** `ProjectedModelCountResult { BigInteger? Count; ProjectedCountStatus Status }`,
with the invariant **`Count` is non-null iff `Status == Exact`**. Do **not** reuse the
existing `ComputationStatus` enum: its values (`Computed` / `TooLarge` / `NotRequested`)
describe the facade's optional-computation lifecycle and do not name the exact-vs-budget
outcome. A dedicated `ProjectedCountStatus { Exact, BudgetExhausted }` is introduced instead.
Cancellation is signalled the library-standard way — `OperationCanceledException` — not a
status value.

**Rationale.** The nullable-count-plus-status shape mirrors the established
`MinimizationStatus` pattern (a sound result carrying an explicit proof/limit status) and
directly enforces "a partial count is never reported as exact" (roadmap §17 decision 4).
Overloading `ComputationStatus` would give it two unrelated meanings and weaken both. A
projected-model **enumeration** API is a separate question — see (5).

### 3. Budget currency

**Decision.** The public budget is the shared `ResourceBudget`; the two engines map it to
their native currency:

- **SAT blocking enumeration** — bounded by the number of enumerated models and by SAT
  conflicts (both already expressible via the incremental `SatSolver`); running out of
  either yields `BudgetExhausted` with `Count == null`.
- **BDD / d-DNNF exact path** — bounded by the existing node budget; a node-budget trip
  yields `BudgetExhausted`.

If `budget` is `null`, the computation is unbounded (subject only to the
`CancellationToken`), consistent with the other engines.

**Rationale.** A single public budget type keeps the surface uniform with every other
expensive engine (roadmap §14 "Budgets, cancellation, status"); the per-engine mapping is
an implementation detail. Crucially, whichever currency trips, the **outcome type is the
same** (`BudgetExhausted`), so "budget exhausted" is comparable across engines from the
caller's point of view even though the internal unit differs.

### 4. Engine selection — explicit vs. `Auto`

**Decision.** Ship the **SAT blocking enumeration MVP as the only engine in v3.3**; do
**not** expose an engine parameter yet. When the exact d-DNNF/BDD path lands, add an
optional `enum ProjectedCountEngine { Auto, BlockingEnumeration, Abstraction }` defaulting
to `Auto`. `Auto` may change its choice between minor releases (governed by the same rule as
the encoding `Auto` in roadmap §17 decision 6: a CHANGELOG note, and never a worse result),
but it is deterministic **within** a release for a given input. Result **values** are always
engine-independent; only performance differs.

**Rationale.** YAGNI — one validated engine covers the MVP and avoids committing an engine
enum before the exact path exists. Reusing the encoding-`Auto` stability policy keeps one
consistent rule for "auto-selection that may evolve".

### 5. Separate projected-model **enumeration** API?

**Decision.** **Deferred, not in the counting contract.** v3.3 ships counting only. If
enumeration is later wanted, it is a separate method
(`EnumerateProjectedModels(projectedVariables, budget, cancellationToken)` returning
`IEnumerable<IReadOnlyDictionary<string,bool>>`), designed alongside the existing
`DnnfCircuit.EnumerateModels` and P1.3 sampling — not folded into
`ProjectedModelCountResult`.

**Rationale.** Counting and enumeration have different cost and streaming characteristics;
bundling an enumeration into the count result would either force materialization or
complicate the value type. Keeping them separate preserves the small, honest counting
surface.

## Deferred / out of scope

- The **exact d-DNNF projection** engine (spike §3c) and its compilation-determinism
  guarantee — implemented after the MVP, behind `Auto`/explicit selection.
- **Weighted** projected model counting — only if a use case appears; it would inherit the
  floating-point contract of roadmap §14 and is not part of this record.

## Acceptance criteria (reaffirmed from roadmap §9)

- exhaustive verification for all functions up to 4 variables (already met by the spike);
- randomized comparison against explicit projection/dedup enumeration;
- many-to-one projection tests (overcount trap);
- empty projection ⇒ `0` (UNSAT) / `1` (SAT); project-all ⇒ equals `CountModels()`;
- budget exhaustion is reported as `BudgetExhausted` with `Count == null` — never an exact
  number.
