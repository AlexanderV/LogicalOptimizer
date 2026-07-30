# Design spike: projected model counting (P1.4-spike)

Status: **design spike** (v3.1). No public API is shipped by this spike. The deliverable is a
validated recommendation plus working prototypes and an exhaustive validation harness. It feeds
the eventual public feature P1.4, whose contract is fixed in
[`doc/decisions/projected-model-counting-api.md`](../decisions/projected-model-counting-api.md).

Spike artifacts:

- Prototypes + oracle: `LogicalOptimizer.Tests/Spikes/ProjectedModelCounting.cs`
- Validation suite: `LogicalOptimizer.Tests/Spikes/ProjectedModelCountingTests.cs`

Both live in the test project and use internal engine access already granted via
`InternalsVisibleTo`. Nothing here appears on the public surface, so the API baseline and the
architecture/API-surface tests are untouched.

---

## 1. The problem

Ordinary model counting (`DnnfCircuit.CountModels`, `BinaryDecisionDiagram.CountSatisfyingAssignments`)
counts full satisfying assignments over *all* variables. **Projected** model counting counts the
number of *distinct* assignments over a chosen subset of variables — the **projection scope**
`P` — that can be extended to some satisfying assignment of the formula `f`. The remaining
variables `V \ P` are existentially *forgotten* (projected out):

```
projected_count(f, P) = |{ a|_P : a is a model of f over V }|
```

Formally this is `|{ p ∈ 2^P : ∃(V\P). f(p, V\P) }|`. It is the counting analogue of projecting a
relation in a database, and the basis for many practical queries (feature-model configuration
counts, reliability over observable variables, information-flow quantification).

Throughout, the **universe** `V` is the variable space we project within, and `P ⊆ V`. Projecting
onto **all** of `V` degenerates to ordinary `CountModels`; projecting onto the **empty** set gives
`1` if `f` is satisfiable and `0` if not.

---

## 2. The overcount trap

The dangerous, tempting shortcuts (roadmap §9) are:

1. simply delete the literals of the forgotten variables;
2. sum the model counts of the OR (decision) branches;
3. assume the d-DNNF **determinism** property survives forgetting.

All three overcount, because **after projection, different full models can collapse onto one
projected model.** A d-DNNF's decomposable-AND / deterministic-OR structure guarantees the
branches of an OR are disjoint *over the full variable set*. Forgetting variables destroys that
disjointness: two branches that differ only in a forgotten variable become the *same* projected
model, but the linear counting pass still adds them twice.

### Worked example

```
f = (x1 ∧ y) ∨ (x2 ∧ ¬y)          project out y, keep P = {x1, x2}
```

`y` is a clean decision variable, so a top-down compiler builds a deterministic OR on `y`:

| branch | models (x1, x2, y)      | projected onto {x1, x2} |
|--------|-------------------------|-------------------------|
| y = 1  | (1,0,1), (1,1,1)        | (1,0), (1,1)            |
| y = 0  | (0,1,0), (1,1,0)        | (0,1), (1,1)            |

Naive branch summation reports `2 + 2 = 4`. But the projected sets **overlap** on `(1,1)`, so the
true projected count is `|{(1,0), (1,1), (0,1)}| = 3`. The determinism that made summation exact
for full counting is exactly what projection breaks. This example is asserted in
`OvercountTrap_WorkedExample_FromDesignDoc` and both prototypes return `3`.

A second, simpler collapse is the many-to-one case `f = x0` over universe `{x0, x1, x2}`: all four
full models with `x0 = true` collapse to the single projected model `{x0 = true}`. A "count full
models" answer of `4` is wrong; the projected count is `1`
(`ManyToOneProjection_DoesNotOvercount`).

---

## 3. Prototyped strategies

The spike implements two strategies end to end and sketches a third.

### (a) SAT blocking enumeration — sound, budgeted MVP candidate

**Algorithm.** Tseitin-encode `f` to CNF (`TseitinConverter`), whose input-variable DIMACS indices
are exactly `V` sorted, with functionally-determined auxiliaries after. Then loop:

1. `Solve()`. If UNSAT → return the accumulated count as **exact**.
2. Read the current assignment of the **projection variables only** and record one projected
   model.
3. Add a **blocking clause built only from the projection literals** (the OR of their negations),
   forbidding every full model that shares this projection.
4. Repeat.

Blocking on the projection scope alone is the crux: it forbids the *entire* fibre of full models
above the projected point, so each distinct projection is produced **exactly once** — the strategy
cannot fall into the overcount trap. Edge behaviour falls out for free: an empty projection blocks
with the empty clause, so the second solve is UNSAT and the count is `1` iff `f` is SAT.
Projection variables that never occur in `f` are unconstrained; each is folded in as a `×2` factor
at the end (`count << freeProjectionVars`).

**Soundness.** Every returned model has a projection distinct from all previous ones (they are
blocked), and no projected model is skipped (a projection is blocked only after it has been
counted). So the enumerated set is exactly the set of distinct projected models. The Tseitin
encoding is model-projecting, so blocking on input literals is valid regardless of the auxiliary
variables.

**Budget / status.** Two honest bounds: `maxModels` caps how many distinct projections may be
enumerated, and `maxConflicts` caps each solve. Hitting the model budget returns
`BudgetExhausted`; an Unknown solve verdict returns `Unknown`. In both cases the count is
**withheld** (`Count = null`) — a partial run never masquerades as exact.

**Complexity.** One incremental SAT call per distinct projected model, so time is
`O(projected_count × cost_per_solve)` — **output-sensitive**. Memory is the CNF plus one blocking
clause per model. Wins when the projected count is small (or a budget makes "small-or-give-up"
acceptable) even if the full model count is astronomical. Loses when the projected count itself is
exponential: enumeration cannot beat its own output size.

### (b) BDD existential abstraction — exact where the BDD is affordable

**Algorithm.** Build a ROBDD (`BuildWithBestOrder`), existentially quantify away every
non-projection variable (`Exists`, which is `∃x.f = f|x=0 ∨ f|x=1` and *merges* paths that agree
on the survivors), then count. Because the forgotten variables become free in the manager,
`CountSatisfyingAssignments` over the quantified node is divided by `2^(forgotten in manager)` to
recover the distinct projected assignments; projection variables absent from the formula are folded
in as `×2` factors, symmetrically to the SAT path.

**Soundness.** `Exists` is the exact projection operator on the Boolean function: the resulting
BDD represents `g(P) = ∃(V\P). f`, whose satisfying `P`-assignments are precisely the projected
models. Counting is exact ROBDD `SatCount`. There is no summation over overlapping branches — the
canonical merge removes the overlap before counting.

**Complexity.** Dominated by BDD size, which is order-sensitive and can be exponential.
Quantification can *blow up* the diagram (the classic hard case for existential abstraction), so a
node budget guards it; exceeding it returns `BudgetExhausted`. Wins when the BDD (and its
quantification) stays compact and gives the count in one pass regardless of how large the projected
count is. Loses on order-hostile functions or when quantifying many variables explodes the
diagram.

### (c) Projected d-DNNF compilation — sketch (exact path, not implemented in the spike)

Two exact, compilation-based routes, deferred to the feature because each is a substantial build:

- **Existential abstraction then recompile.** Forget `V\P` by smoothing/forgetting on the circuit,
  then **recompile the forgotten circuit to a fresh deterministic d-DNNF** and count linearly. The
  recompilation is essential: forgetting produces a decomposable-but-**non-deterministic** DNNF
  (exactly the overlap of §2), and only re-imposing determinism over the projected scope makes the
  linear count exact again. Cost is the recompilation, which can be exponential in the worst case
  but reuses the existing top-down compiler and component cache.

- **Projected/decision-DNNF with a projected scope.** Compile top-down but constrain the decision
  order so all projection variables are decided **above** the forgotten ones, caching components by
  projected scope. The forgotten sub-circuits below then contribute a boolean "satisfiable?" (0/1)
  rather than a count, which is exactly projection. This is the most promising exact route but
  needs a real compiler change (decision heuristics, cache keying, and a determinism argument for
  the projected prefix).

A third option — a **#∃SAT / projected-#SAT** style search with component caching and a projection
frontier — is the state-of-the-art for exact projected counting but is a large, separate engine.

---

## 4. Empirical validation

An independent brute-force oracle (`BruteForceProjected`) enumerates all `2^|V|` assignments,
keeps the models, projects each onto `P`, and returns the size of the resulting **set** of distinct
projections. Both prototypes are checked against it.

| Validation | Scope | Result |
|---|---|---|
| Exhaustive n = 1 | all 4 functions × all subsets | oracle == SAT == BDD |
| Exhaustive n = 2 | all 16 functions × all subsets | oracle == SAT == BDD (~75 ms) |
| Exhaustive n = 3 | all 256 functions × all subsets | oracle == SAT == BDD (~220 ms) |
| **Exhaustive n = 4** | **all 65 536 functions × all 16 subsets = 1 048 576 checks** | **oracle == SAT == BDD**; summed projected count fingerprint `4 159 487` identical for both prototypes (~67 s) |
| Randomized | 400 trials, ≤ 10 vars, random projections | oracle == SAT == BDD (~20 ms) |
| Project-all | 60 trials vs shipped `CountModels` | SAT == BDD == `CompileToDnnf().CountModels()` |
| Empty projection | SAT ⇒ 1, UNSAT ⇒ 0 | both prototypes agree |
| Many-to-one / overcount trap | worked example ⇒ 3; `f=x0` ⇒ 1, 2 | both prototypes agree; no overcount |
| Budget (models) | 63-model instance, budget 10 | `BudgetExhausted`, `Count = null` |
| Budget (conflicts) | zero conflict budget | `Unknown` (or trivially `Exact`), `Count` withheld — never a partial passed as exact |

**Both strategies agreed with the oracle on every one of the 1 048 576 exhaustive 4-variable
checks** and on all randomized and edge cases — meeting the roadmap §9 acceptance criteria
("exhaustive verification for all functions up to 4 variables", randomized comparison,
many-to-one, empty projection, project-all == `CountModels`, budget exhaustion never exact).

**Cost profile.** SAT blocking enumeration is output-sensitive (one solve per projected model):
predictable, cheap when the projected count or budget is small, linear in the answer size
otherwise. BDD existential abstraction is single-pass but its cost (and feasibility) is governed by
diagram size, which quantification can inflate. On the tiny spike instances both are sub-millisecond
per query; the two profiles diverge exactly where their respective worst cases bite.

---

## 5. Recommendation

- **MVP (v3.3): SAT blocking enumeration.** Ship `CountProjectedModels(projectedVariables, budget,
  cancellationToken)` returning `ProjectedModelCountResult { BigInteger? Count; ComputationStatus
  Status }`. It is sound by construction against the overcount trap, output-sensitive, reuses the
  existing incremental `SatSolver` (assumptions/blocking already supported), and gives an honest
  budgeted status. **A partial (budget/cancellation-limited) enumeration is never reported as
  exact** — `Count` is `null` unless the enumeration ran to UNSAT within budget. This is exactly
  §17 decision 4.
- **Exact path (later): d-DNNF projection / existential abstraction** (§3c), with the BDD
  existential-abstraction route as an affordable exact fallback for favourable projection scopes.
  Offer it as an opt-in `Exact` mode / engine selection, still under a node/size budget, that
  either returns an exact count or a typed `BudgetExhausted` — never a heuristic partial.
- **Status contract.** One `ComputationStatus`: `Exact`, `BudgetExhausted`, (and cancellation via
  `OperationCanceledException`). `Count` is non-null **iff** `Status == Exact`. Empty projection ⇒
  `0`/`1`; project-all ⇒ `CountModels()`; both are exact and cheap.

### Open questions before committing the public API

1. **Scope semantics.** Must `P ⊆ f`'s variables, or may `P` include variables outside the formula
   (the spike defines the universe as the formula's variables and folds free projection variables
   in as `×2`)? The public contract must state this and how unknown projection names are treated
   (argument error vs. free variable).
2. **Result shape.** `BigInteger? Count` + status vs. a discriminated result; whether to expose the
   enumerated projected models (a `ProjectedModels` enumeration) alongside the count.
3. **Budget currency.** Model budget, conflict budget, node budget, wall-clock, or the shared
   `ResourceBudget` — and how each maps onto the two engines so "budget exhausted" is comparable
   across them.
4. **Engine selection.** Auto-pick (small projected count ⇒ blocking enumeration; compact BDD ⇒
   abstraction) vs. explicit engine parameter; and the determinism/reproducibility guarantee of
   `Auto` across releases.
5. **Exact-path determinism & floating point.** The exact d-DNNF route's compilation determinism,
   and (if a weighted projected count is ever added) the floating-point contract per §14.

---

## 6. References

- [`doc/decisions/projected-model-counting-api.md`](../decisions/projected-model-counting-api.md)
  — the accepted public contract: MVP on SAT blocking enumeration with
  `ProjectedModelCountResult{Count?, Status}`; exact d-DNNF projection after this spike; a partial
  count is never reported as exact.
- Engines studied: `LogicalOptimizer.Dnnf/DnnfCircuit.cs`,
  `LogicalOptimizer.Dnnf/KnowledgeCompilation.cs`, `LogicalOptimizer.Sat/SatSolver.cs`,
  `LogicalOptimizer.Sat/TseitinConverter.cs`, `LogicalOptimizer.Bdd/BinaryDecisionDiagram.cs`.
