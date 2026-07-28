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
