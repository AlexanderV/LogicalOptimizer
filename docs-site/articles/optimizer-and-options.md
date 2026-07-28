# Optimizer & Options

`BooleanExpressionOptimizer` is the facade entry point: it parses, runs the rewrite
pipeline, routes minimization by variable count, verifies the result equivalent to the
input, and returns an `OptimizationResult`. Every example here is asserted in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

## Basic optimize

```csharp
using LogicalOptimizer;

var optimizer = new BooleanExpressionOptimizer();
var result = optimizer.OptimizeExpression("a & b | a & c", includeMetrics: true);

Console.WriteLine(result.Optimized);            // a & (b | c)
Console.WriteLine(result.CNF);                  // a & (b | c)
Console.WriteLine(result.DNF);                  // a & b | a & c
Console.WriteLine(string.Join(", ", result.Variables));  // a, b, c
Console.WriteLine(result.MinimizationStatus);   // MinimalProven
Console.WriteLine(result.IsEquivalent());       // True (verified against the input)
```

More rewrites, all verified equivalent to their input:

| Input | `Optimized` |
|---|---|
| `(a \| b) & (a \| c)` | `a \| b & c` |
| `!(a & b)` | `!a \| !b` |
| `a & b \| !a & c \| b & c` | `a & b \| c & !a` (consensus) |
| `a & 1 \| b & 0` | `a` (constants) |

## `OptimizationOptions`

`OptimizeExpression(expression, OptimizationOptions)` gives fine-grained control. Presets:
`OptimizationOptions.Default` (the standard path) and `OptimizationOptions.Everything`
(also computes the advanced form and both truth tables). Notable members: `ComputeCnf`,
`ComputeDnf`, `ComputeAdvancedForms`, `IncludeMetrics`, `IncludeDebugInfo`,
`IncludeTruthTables`, `CnfMode`, `Budget` ([ResourceBudget](budgets-and-zones.md)),
`CancellationToken`, and `EnableAigRewriting`.

```csharp
var everything = optimizer.OptimizeExpression("a & !b | !a & b", OptimizationOptions.Everything);
Console.WriteLine(everything.Advanced);              // a XOR b
Console.WriteLine(everything.OptimizedTruthTable);   // full truth table (not null)
```

## AIG rewriting is on by default since v3.0

`OptimizationOptions.EnableAigRewriting` defaults to **`true`** in v3.0 (it was opt-in in
2.5). On the default path the optimizer computes one extra candidate — a DAG-aware
And-Inverter-Graph cut rewrite — and **adopts it only when it is both verified equivalent
to the input and strictly cheaper** by the cost metric. So the default output may now be a
smaller multi-level form than before v3.0, and it can never be worse.

To restore the exact pre-3.0 output, turn the flag off:

```csharp
// Default (v3.0): AIG rewriting enabled.
Console.WriteLine(OptimizationOptions.Default.EnableAigRewriting);  // True

// Opt out — restores the pre-3.0 two-level/multi-level result.
var pinned = optimizer.OptimizeExpression("(a & b) | (a & c) | (a & d)",
    new OptimizationOptions { EnableAigRewriting = false });
Console.WriteLine(pinned.Optimized);   // a & (b | c | d)
```

Both paths stay equivalence-verified; the only difference is whether the extra AIG
candidate is allowed to win.

## `OptimizationResult` fields

Besides `Optimized` / `CNF` / `DNF` / `Advanced` / `Variables`, the result carries
`MinimizationStatus` (SOP/`Optimized`/`DNF` provenance) and `CnfMinimizationStatus`
(equivalent-CNF/POS provenance — reported separately, `Heuristic` for `CnfMode.Tseitin`
where two-level minimality does not apply), both explained in
[Operation contracts & statuses](contracts-and-statuses.md); `CnfStatus` / `DnfStatus`
(`ComputationStatus`), optional `Metrics` (`OptimizationMetrics`: node counts, iterations,
applied rules, elapsed time, convergence trace, allocated bytes), optional
`OriginalTruthTable` / `OptimizedTruthTable`, `DebugInfo`, and the `IsEquivalent()` /
`CheckEquivalence()` self-checks.

## Quality analysis

`OptimizationQualityAnalyzer` turns a result into a compression/complexity report:

```csharp
var r = optimizer.OptimizeExpression("a & b | a & c", new OptimizationOptions { IncludeMetrics = true });

var metrics = OptimizationQualityAnalyzer.AnalyzeOptimization(r);
Console.WriteLine(metrics.LiteralCount);   // 3
Console.WriteLine(metrics.OperatorCount);  // 2

Console.WriteLine(OptimizationQualityAnalyzer.GenerateQualityReport(r));
// === OPTIMIZATION QUALITY REPORT === ... compression ratio, optimality score, applied rules
```

## Next steps

- [Operation contracts & statuses](contracts-and-statuses.md) — what the statuses guarantee.
- [Resource budgets & the zone model](budgets-and-zones.md) — how variable count routes work.
- [Normal forms & transformations](normal-forms.md) — CNF / DNF / ANF / Tseitin.
