# LogicalOptimizer.Minimization

**Two-level Boolean minimization for the LogicalOptimizer toolkit.** Exact Quine–McCluskey
with an explicit provable-minimality status, SAT-based mid-range prime covers, Espresso-style
cube-list heuristics for large functions, multi-output PLA-style cube sharing and CSV
truth-table parsing.

```bash
dotnet add package LogicalOptimizer.Minimization
```

```csharp
using LogicalOptimizer;

var variables = new[] { "a", "b", "c" };
var onSet = new[] { 3, 5, 6, 7 }; // majority of 3 inputs

var (expression, provenMinimal) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);

Console.WriteLine(expression);     // a & b | a & c | b & c
Console.WriteLine(provenMinimal);  // True
```

## When to choose this package

Reference `.Minimization` when you work directly from truth tables, on-sets/don't-cares or
CSV tables and want a **minimal sum-of-products with a proof status** (or multi-output
covers with shared cubes). For optimizing an expression string end-to-end — with
equivalence verification and multi-level factoring — use the `LogicalOptimizer` facade.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
