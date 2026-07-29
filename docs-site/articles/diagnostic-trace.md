# Diagnostic Trace

The optimizer reports *what* it proved through
[statuses](contracts-and-statuses.md). The **trace** answers the next question — *why this
result?* It is opt-in, structured, and safe to enable in production: it only records
bookkeeping and never changes the outcome.

```csharp
var result = new BooleanExpressionOptimizer()
    .OptimizeExpression("a & b | a & c", new OptimizationOptions { IncludeTrace = true });

Console.WriteLine(result.Trace);   // one line per decision
```

## What it answers

| Question | Where it shows up |
|---|---|
| Which engine ran? | `ZoneSelection` — `engine=exact-qm` / `sat-prime-cover` / `espresso-lite` |
| Why that engine? | the same entry carries `variables` and the `exactGate` / `guaranteeZone` / `satZone` thresholds that decided it |
| Which budget applied? | `Budget` entries — e.g. `qmPairComparisonLimit=unbounded`, `coverStepLimit` |
| Which proof path fired? | `SoundnessGuard` — `method=truth-table` or `sat-miter`; plus `ExactMinimization` / `SatPrimeCover` proof entries |
| Why a fallback / non-proven status? | `Fallback` entries (budget exhausted, `TooLarge` normal form) and the final `Status` entry |
| Which candidate lost, and why? | `Candidate` entries with each cost, then `Adopted` or `Rejected` with the winner and the comparison |

## Example

For `a & b | a & c` (3 variables, so the exact backend applies):

```text
[EngineSelection] RewritePipeline: fixpoint loop ran 2 iteration(s) (iterations=2, maxIterations=20, nodesAfter=5, nodesBefore=7)
[Proof] SoundnessGuard: rewrite proven equivalent to the input by truth table (method=truth-table, proven=true, ...)
[EngineSelection] ZoneSelection: 3 variable(s): exact Quine-McCluskey backend in the unbounded guarantee zone (minimality provable) (engine=exact-qm, exactGate=12, guaranteeZone=10, satZone=24, variables=3)
[Budget] ExactMinimization: QM pair comparisons unbounded (guarantee zone); cover search bounded (coverStepLimit=2147483647, qmPairComparisonLimit=unbounded)
[Proof] ExactMinimization: minimum-cover search completed: the two-level cover is provably minimal (minimizationStatus=MinimalProven, onSetSize=3)
[Candidate] ExactMinimization: candidate 'rewritten' costed (candidate=rewritten, literals=3, nodes=5)
[Candidate] ExactMinimization: candidate 'factored-min-sop' costed (candidate=factored-min-sop, literals=3, nodes=5)
[Candidate] ExactMinimization: candidate 'min-sop' costed (candidate=min-sop, literals=4, nodes=7)
[Rejected] ExactMinimization: no candidate beat 'rewritten' on literals, then nodes — keeping it (kept=rewritten)
[Proof] EquivalentCnf: minimum POS cover search completed: the equivalent CNF is provably minimal (cnfMinimizationStatus=MinimalProven)
[Rejected] AigRewrite: no structural gain (2 -> 2 AND nodes)
[Status] Result: final minimality provenance: MinimalProven (literalsIn=4, literalsOut=3, ...)
```

Read bottom-up it is a complete audit: the minimal SOP (`a & b | a & c`, 4 literals) *lost*
to the factored rewrite (`a & (b | c)`, 3 literals), the AIG rewriter found nothing to
improve, equivalence was discharged by truth table, and minimality was proven.

Note the ordering: the rewrite pipeline and its soundness guard run **before** zone dispatch,
so their entries come first. The trace records the real execution order, not a tidied one.

## Structure

`OptimizationTrace.Entries` is a list of `OptimizationTraceEntry`:

| Member | Meaning |
|---|---|
| `Category` | `EngineSelection`, `Budget`, `Candidate`, `Adopted`, `Rejected`, `Proof`, `Fallback`, `Status` |
| `Step` | the phase, e.g. `ZoneSelection`, `ExactMinimization`, `SoundnessGuard`, `AigRewrite` |
| `Message` | the decision and its reason, in words |
| `Data` | the same facts as string key/value pairs, for filtering and structured logs |

Filter by category when you only care about one aspect:

```csharp
foreach (var proof in result.Trace!.OfCategory(OptimizationTraceCategory.Proof))
    Console.WriteLine($"{proof.Step}: {proof.Data["method"]}");

// Did anything fall back?
var degraded = result.Trace.OfCategory(OptimizationTraceCategory.Fallback).Any();
```

## Beyond the exact gate

At 14 variables the same expression class routes differently, and the trace says so —
equivalence moves to the SAT miter and no minimality proof is claimed:

```text
[Proof] SoundnessGuard: rewrite proven equivalent to the input by SAT miter (method=sat-miter, satConflictLimit=20000, variables=14)
[EngineSelection] ZoneSelection: 14 variable(s): beyond the exact gate — no 2^n table; SAT prime-cover path, adopted only after a SAT-miter proof (engine=sat-prime-cover, ...)
[Proof] SatPrimeCover: prime cover proven equivalent by SAT miter — eligible as a candidate (coverFound=true, proven=true, ...)
[Status] Result: final minimality provenance: Heuristic (...)
```

And past 24 variables, where the distributive CNF cannot be built, the abandoned artifact is
an explicit `Fallback` rather than a bare `-` in the result:

```text
[Fallback] EquivalentCnf: distributive CNF conversion exceeded its size cap — reported as TooLarge instead of an unbounded blow-up (use --cnf-mode=tseitin for a linear CNF) (cnfStatus=TooLarge)
```

## From the CLI

```bash
logical-optimizer --trace "a & b | a & c"              # appended under "Trace:"
logical-optimizer --format=json --trace "a & b | a & c" # as a "trace" array
```

## Stability

The trace is a **diagnostic aid, not a stability contract**. Entry wording, ordering and the
exact set of entries may change in any minor release as the pipeline evolves. Log it, display
it, filter it by `Category`/`Step`/`Data` keys — but do not assert on exact text in your own
tests. The stable contracts are the [statuses](contracts-and-statuses.md) and, for the CLI,
the versioned `--format=json` report.

## Next steps

- [Operation Contracts & Statuses](contracts-and-statuses.md) — what each status guarantees
- [Resource Budgets & the Zone Model](budgets-and-zones.md) — the thresholds the trace reports
- [CLI Usage](cli-usage.md) — `--trace` and the JSON report
