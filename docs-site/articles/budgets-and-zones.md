# Resource Budgets & the Zone Model

LogicalOptimizer routes work by **variable count** and bounds every expensive engine with
a **`ResourceBudget`**. Two fixed guards (`PerformanceValidator`) reject inputs that are
too large before any engine runs. Nothing here can make a result *wrong* — a budget that
is exhausted causes a documented fallback, a status change, or a documented exception.

## Fixed input limits — `PerformanceValidator`

Hard limits enforced at validation time (before optimization):

| Limit | Constant | Value |
|---|---|---|
| Maximum expression length | `MAX_EXPRESSION_LENGTH` | 10 000 characters |
| Maximum number of variables | `MAX_VARIABLES` | 100 |
| Maximum parenthesis nesting | `MAX_PARENTHESES_DEPTH` | 50 |

Truth-table-based operations cap at **20 variables** (`TruthTable.MaxVariables`) — a full
2ⁿ table beyond that is refused rather than attempted.

## The zone model (variable-count routing)

The facade picks a minimization strategy from the variable count. This routing is the
core behavior contract and is unchanged since v2.0:

| Zone | Engine | Guarantee |
|---|---|---|
| **≤ 10** | Exact Quine–McCluskey, unbounded cover search | `MinimalProven` **guaranteed** |
| **11–12** | Exact QM under work budgets | `MinimalProven` or `BudgetExceeded` |
| **13–24** | SAT-based prime cover (no 2ⁿ table), adopted only after a SAT-miter proof | `Heuristic`, verified equivalent |
| **> 24** | Local subcircuit rewriting + Espresso-lite cube lists (EXPAND / IRREDUNDANT / REDUCE) | `Heuristic`, sound by construction |

The scale contract in one line: expressions accept up to **100** variables overall;
truth-table operations cap at **20**; exact minimization is guaranteed **≤ 10**, budgeted
**≤ 12**; the SAT-based prime cover extends to **24**; and the SAT / BDD / Tseitin engines
have no variable cap — only work budgets.

## `ResourceBudget`

`ResourceBudget` unifies the work budgets for the potentially expensive engines. Every
limit has a safe default; callers with harder latency requirements tighten them per call
via `OptimizationOptions.Budget`. All engines also honor a `CancellationToken`.

| Member | Purpose | Default |
|---|---|---|
| `QmPairComparisonLimit` | Cube-pair comparisons for QM prime generation (11–12 var attempts) | validator default |
| `CoverStepLimit` | Branch-and-bound steps for the minimum-cover search outside the guarantee zone | 200 000 |
| `SatConflictLimit` | SAT conflicts for one explicit equivalence query | 200 000 |
| `SoundnessGuardConflictLimit` | SAT conflicts for the per-call soundness guard beyond the truth-table range | validator default |
| `BddNodeLimit` | Node budget for BDD construction | 1 000 000 |

```csharp
using LogicalOptimizer;

var options = new OptimizationOptions
{
    Budget = new ResourceBudget
    {
        CoverStepLimit = 50_000,   // tighter minimum-cover search
        BddNodeLimit   = 250_000
    }
};

var result = new BooleanExpressionOptimizer().OptimizeExpression("…", options);
// If a budget is hit inside the exact range, the status becomes BudgetExceeded —
// the answer stays sound, it is just no longer proven minimal.
```

> [!IMPORTANT]
> Exhausting a budget **never** produces a wrong result. Each engine falls back
> (heuristic simplification, a rollback to the input, or an `Unknown` verdict from a
> standalone equivalence check) or throws a dedicated budget/size exception
> (`ComputationBudgetExceededException`, `NodeBudgetExceededException`,
> `NormalFormTooLargeException`), and the [`MinimizationStatus`](contracts-and-statuses.md)
> reflects what happened.

`ResourceBudget.Default` exposes the shared default instance.

## Engine operating envelope

One row per engine: what it is meant for, where it starts to degrade, where it is cut
off, what exactly happens at the cutoff, and which external tool to hand the instance to
when it has outgrown the embedded engine. Two kinds of numbers appear, and they are
deliberately kept apart:

- **Hard limits** are enforced in code. Every one cites the constant that enforces it,
  and `LogicalOptimizer.Tests/Documentation/EngineEnvelopeConsistencyTests.cs` fails the
  build if a number in this table drifts from the constant.
- **Soft thresholds** are engineering guidance about where quality or latency degrades.
  Where a committed, executable artifact measures the behavior it is cited; everywhere
  else the entry is marked *guidance* — the project has no published latency/quality
  curves for those regions and does not pretend otherwise.

| Engine | Intended use | Soft threshold (degradation) | Hard limit / budget (enforced) | On exceed | Hand-off |
|---|---|---|---|---|---|
| **Heuristic rewrite optimizer** (rules + subcircuit library + AIG rewriting + Espresso-lite cube lists) | General simplification at any accepted size; the only minimization engine above 24 variables | *Guidance:* above the exact/SAT zones the distance from a true minimum cover is unquantified — the result is sound and never larger than the input, but no quality curve is published | Input limits `MAX_EXPRESSION_LENGTH` = 10 000, `MAX_VARIABLES` = 100, `MAX_PARENTHESES_DEPTH` = 50; `MAX_OPTIMIZATION_ITERATIONS` = 20 rewrite passes; whole-call deadline `MAX_PROCESSING_TIME_SECONDS` = 10 s | `ArgumentException` at validation (over-limit input never reaches an engine); `TimeoutException` when the whole-call deadline fires; status stays `Heuristic` | [Berkeley ABC](choosing-a-tool.md) for industrial multi-level synthesis |
| **Exact QM minimizer** (≤ 12 variables) | Provably minimal two-level SOP/POS for small functions (PLA-style control logic) | ≤ 10 variables always completes with a proof (finite search, cover search unbounded there). *Guidance:* at 11–12, dense functions may exhaust the budgets; no density curve is published | `EXACT_GUARANTEE_VARIABLES` = 10 (guarantee zone), `MAX_EXACT_MINIMIZATION_VARIABLES` = 12 (gate); at 11–12: `QmPairComparisonLimit` = 5 000 000, `CoverStepLimit` = 200 000 | `MinimizationStatus.BudgetExceeded` (sound cover kept, proof waived) or fallback to the heuristic path — reported in the status and trace, never silently | [Espresso / PyEDA](choosing-a-tool.md) for reference-grade large or multi-output two-level covers |
| **SAT two-level minimizer** (13–24 variables) | Two-level improvement without a 2ⁿ table; candidate adopted only after a SAT-miter equivalence proof | *Guidance:* result is heuristic by design — a prime cover, not a proven minimum; no quality curve is published | `MAX_SAT_MINIMIZATION_VARIABLES` = 24; `SAT_MINIMIZATION_CUBE_LIMIT` = 256 cubes; `SAT_MINIMIZATION_QUERY_CONFLICTS` = 20 000 per query; adoption proof capped by `SatConflictLimit` = 200 000 | Candidate discarded and the rewrite result kept; status stays `Heuristic` — an unproven cover is never adopted | [Espresso / PyEDA](choosing-a-tool.md) |
| **Embedded CDCL SAT** (assumptions, unsat cores, DRAT) | Embedded decision queries, equivalence miters, backbones, incremental solving — no variable cap | *Guidance:* hard industrial instances outgrow it long before any budget fires; it is [not a competition-grade solver](choosing-a-tool.md) | Per-call conflict budget `maxConflicts`: `SatSolver.Solve` default = 1 000 000; equivalence queries `EquivalenceChecker.DefaultMaxConflicts` = 200 000 (mirrored by `SatConflictLimit`) | `SatResult.Unknown`; an equivalence check returns `AreEquivalent == null` — an inconclusive verdict, never a wrong one | [CaDiCaL / Kissat](external-solvers.md) via DIMACS export or the `IExternalSatSolver` seam |
| **MaxSAT / pseudo-Boolean** | Weighted partial MaxSAT and PB feasibility on small-to-medium embedded instances | *Guidance:* no benchmark against dedicated MaxSAT solvers is published; treat large or tightly constrained optimization instances as hand-off candidates | `MaxSatSolver.Solve` conflict budget per SAT call default = 1 000 000; `PseudoBooleanProblem.Solve` default = 1 000 000 conflicts | `MaxSatStatus.Unknown` with the best sound incumbent and a `LowerBound ≤ optimum ≤ UpperBound` bracket — never reported `Optimal`; infeasible hard clauses are `HardClausesUnsatisfiable`; PB returns `SatResult.Unknown` | A dedicated MaxSAT/PB solver via the WCNF/OPB round-trip writers (`WeightedCnfProblem.Write`, `PseudoBooleanProblem.Write`) |
| **BDD** (incl. reordering) | Canonical equivalence, quantification, exact model counting when a good variable order exists | Variable **order**, not variable count, is the limit: measured on the committed adversarial corpus, the same 24-variable function costs 24 570 allocated nodes in the bad order and 36 after sifting (`eq12-separated`, `LogicalOptimizer.Benchmarks/BddOrderCorpus`) | `BddNodeLimit` = 1 000 000 (`BinaryDecisionDiagram.DefaultNodeBudget`); sifting bounded by `maxRebuilds` = 400 | `NodeBudgetExceededException` — typed, thrown during construction, before memory is exhausted; `BinaryDecisionDiagram.AreEquivalent` returns `null` (fall back to SAT) | [CUDD or Python `dd`](choosing-a-tool.md) for very large BDDs with deep reordering |
| **d-DNNF compile / model counting** | Exact `#SAT`, weighted counting, enumeration and conditioning via knowledge compilation | *Guidance:* compiled size depends on formula structure rather than variable count; no size curve is published | `KnowledgeCompilation.DefaultNodeBudget` = 1 000 000 nodes; projected counting bounded by `CoverStepLimit` (enumerated models) and `SatConflictLimit` (conflicts per solve) | `NodeBudgetExceededException` from the compiler; `CountProjectedModels` returns `ProjectedCountStatus.BudgetExhausted` with `Count == null` — a partial count is never reported as exact | [d4 or another `#SAT` counter](external-solvers.md) on the Tseitin DIMACS export |

Two cross-cutting properties hold for every row:

- **No crash, no hang.** Every "on exceed" outcome is a documented status or a typed
  exception; every engine additionally honors a `CancellationToken`. Cancellation
  latency (overshoot) is measured, not assumed: the `-- cancellation-overshoot`
  benchmark harness cancels each budgeted engine mid-flight and reports median/max
  overshoot (see `doc/COMPARISON_METHODOLOGY.md`, "Resource observability" — the numbers
  are machine-dependent and reported for observability, never asserted as a bound).
- **Executable evidence for the adversarial cases.** The BDD row's behavior is pinned by
  `LogicalOptimizer.Tests/Engines/Bdd/BddOrderCorpusRegressionTests.cs`: the adversarial
  order provably throws `NodeBudgetExceededException` under an explicit 1 500-node
  budget while the good order builds the same function within it, and sifting recovers
  the adversarial input. The exact-QM row's guarantee zone is pinned by
  `LogicalOptimizer.Tests/Engines/Minimization/PlaCorpusRegressionTests.cs`: every
  multi-output corpus member finishes `MinimalProven` at a pinned literal count.
