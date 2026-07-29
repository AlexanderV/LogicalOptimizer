# Case Studies

Small worked examples with **measured** numbers: how many expressions were processed, the
size before and after, the verification status, and the time and memory it took. Every figure
below was produced by the commands shown — nothing is estimated.

> [!NOTE]
> Measured on Windows 11 x64, .NET SDK 10.0.301, Release build, single-threaded, after a
> warm-up call. Literal counts are machine-independent; **time and memory are not** — treat
> them as an order of magnitude, not a specification. Memory is bytes allocated on the calling
> thread for the call (`OptimizationMetrics.AllocatedBytes`), not peak working set.

## 1. A generated entitlement condition

A code generator expands a plan/feature model into a guard. The condition is correct but
verbose, and it is evaluated on every request.

**Input** (7 variables, 14 literals):

```text
(!sso | pro) & (!audit | pro) & (pro | free) & !(pro & free) &
(pro & seats | pro & unlimited) & (!trial | free)
```

**Result:**

```text
pro & !free & !trial & (seats | unlimited)
```

| Metric | Value |
|---|---|
| Variables | 7 |
| Literals | **14 → 5** (−64%) |
| AST nodes | 27 → 9 |
| Verification | equivalent to the input, **proven** |
| Minimality | `MinimalProven` |
| Time | 2.1 ms |
| Allocated | 749 KiB |

The interesting part is not the shrink — it is that `MinimalProven` says no smaller two-level
cover exists, and the equivalence proof rules out the classic failure mode of hand-simplifying
a generated guard: silently dropping a case (`!trial`, here).

## 2. A duplicated templated guard

Templating engines emit the same sub-condition once per branch. Five variables, four
near-identical terms.

**Input** (12 literals) → **result** (5 literals):

```text
region_eu & tier_gold & !suspended | region_eu & tier_silver & !suspended |
region_us & tier_gold & !suspended | region_us & tier_silver & !suspended
-->
!suspended & (region_eu | region_us) & (tier_gold | tier_silver)
```

| Metric | Value |
|---|---|
| Literals | **12 → 5** (−58%) |
| AST nodes | 21 → 9 |
| Verification | **proven** equivalent |
| Minimality | `MinimalProven` |
| Time | 1.7 ms · 564 KiB |

## 3. A wide rule set, past the exact gate

Fourteen variables is beyond the exact-minimization gate, so the honest outcome is a smaller
expression **without** a minimality claim — which is exactly what is reported.

**Input** (18 literals) → **result** (16 literals):

```text
a1&a2 | a3&a4 | a5&a6 | a7&a8 | a9&a10 | a11&a12 | a13&a14 | a1&a4 | a5&a8
-->
a10 & a9 | a11 & a12 | a13 & a14 | a3 & a4 | a7 & a8 | a1 & (a2 | a4) | a5 & (a6 | a8)
```

| Metric | Value |
|---|---|
| Variables | 14 |
| Literals | 18 → 16 |
| Verification | **proven** equivalent (SAT miter — no 2ⁿ table) |
| Minimality | `Heuristic` — *not* claimed as optimal |
| Time | 12.9 ms · 5.8 MiB |

This is the case that distinguishes the library from a tool that just returns something
smaller: the modest 18 → 16 gain is reported as heuristic, and the
[trace](diagnostic-trace.md) shows which engine ran and why no proof was available.

## 4. A business-rule regression check

A refactor of an access rule drops the business-hours guard for owners. The check is the
regression test.

```csharp
var check = EquivalenceChecker.Check("admin | (owner & businessHours)", "admin | owner");
// AreEquivalent = false
// Counterexample: admin=0, businessHours=0, owner=1
```

| Metric | Value |
|---|---|
| Verdict | **not equivalent** — behaviour changed |
| Counterexample | `admin=0, businessHours=0, owner=1` |
| Time | 0.3 ms · 11 KiB |

The counterexample is the whole value: it names the exact input where the refactor grants
access it should not, and drops straight into a unit test.

## 5. Bulk run over the committed corpus

Seventeen functions from 2 to 24 variables, one command, showing how status and cost track the
[zone model](budgets-and-zones.md).

```bash
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare
```

| Zone | Function | Vars | Input literals | Result literals | Status | Time (ms) |
|------|----------|-----:|---------------:|----------------:|--------|----------:|
| small | maj3 | 3 | 6 | 5 | `MinimalProven` | 0.7 |
| small | consensus3 | 3 | 6 | 4 | `MinimalProven` | 1.0 |
| small | xor2 | 2 | 4 | 4 | `MinimalProven` | 0.2 |
| small | xor3 | 3 | 12 | 10 | `MinimalProven` | 2.3 |
| small | maj4 | 4 | 12 | 8 | `MinimalProven` | 4.9 |
| small | mux2 | 3 | 4 | 4 | `MinimalProven` | 0.2 |
| small | pos6 | 6 | 6 | 6 | `MinimalProven` | 2.3 |
| small | eq5 | 5 | 10 | 10 | `MinimalProven` | 1.9 |
| small | pairs8 | 8 | 8 | 8 | `MinimalProven` | 2.7 |
| small | pairs10 | 10 | 10 | 10 | `MinimalProven` | 9.4 |
| mid | pairs12 | 12 | 12 | 12 | `BudgetExceeded` | 20.4 |
| mid | collapse14 | 14 | 28 | 7 | `Heuristic` | 1.7 |
| mid | pairs16 | 16 | 16 | 16 | `Heuristic` | 5.7 |
| mid | collapse18 | 18 | 36 | 9 | `Heuristic` | 2.9 |
| mid | pairs20 | 20 | 20 | 20 | `Heuristic` | 8.9 |
| mid | chain22 | 22 | 60 | 40 | `Heuristic` | 66.1 |
| mid | pairs24 | 24 | 24 | 24 | `Heuristic` | 12.1 |

**What to read from it.** Every function in the guarantee zone comes back `MinimalProven`.
`pairs12` is the honest edge case: 12 variables is inside the exact range but outside the
unbounded guarantee, the cover search hit its budget, and the status says `BudgetExceeded`
rather than pretending to a proof. Past the gate the results stay verified but heuristic —
`collapse14` (28 → 7) and `collapse18` (36 → 9) show the heuristic path still finds large
structural wins, while `pairs16`/`pairs20`/`pairs24` are already minimal and correctly left
alone. Nothing in the run exceeds ~66 ms.

## Why LogicalOptimizer for these

All five cases share one requirement: the answer must be **trustworthy inside a .NET process**.

- Every result is equivalence-verified before return, and optimality is either proven or
  explicitly not claimed — so the output can be shipped into a generated guard without a human
  re-deriving the truth table.
- No native dependency, no JVM, no Python: the same code runs in a service, a build task, or a
  Native-AOT single-file binary.
- Counterexamples turn "these rules differ" into an actionable failing test.

For arithmetic constraints (Z3), competition-scale SAT (Kissat/CaDiCaL) or industrial synthesis
(ABC), pick those instead — see [Choosing a Tool](choosing-a-tool.md).

## Reproduce

```csharp
// Cases 1-3: size, status, time and memory
var options = new OptimizationOptions { IncludeMetrics = true };
var result = new BooleanExpressionOptimizer().OptimizeExpression(expression, options);
Console.WriteLine($"{result.MinimizationStatus} {result.IsEquivalent()} " +
                  $"{result.Metrics!.ElapsedTime.TotalMilliseconds:F2}ms " +
                  $"{result.Metrics.AllocatedBytes / 1024.0:F1}KiB");
```

```bash
# Case 5: the whole corpus
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare
```
