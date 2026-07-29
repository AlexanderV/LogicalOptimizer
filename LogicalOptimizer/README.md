# LogicalOptimizer

**Verified Boolean expression optimizer for .NET** — the facade package. It ties the
Core, SAT, BDD and Minimization layers together behind one entry point,
`BooleanExpressionOptimizer`. It references those four LogicalOptimizer packages and
no third-party package.

```bash
dotnet add package LogicalOptimizer
```

```csharp
using LogicalOptimizer;

var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");

Console.WriteLine(result.Optimized);          // a & (b | c)
Console.WriteLine(result.IsEquivalent());     // True          (verified against the input)
Console.WriteLine(result.MinimizationStatus); // MinimalProven
```

Every result is checked equivalent to the input before it is returned (truth table up to
12 variables, built-in SAT miter beyond), and minimality is reported explicitly —
`MinimalProven` / `BudgetExceeded` / `Heuristic`, never silently downgraded.

## When to choose this package

Start here for most uses: one type covering parsing, optimization, equivalence checking
with counterexamples, CNF/DNF, exporters and analysis. Add `LogicalOptimizer.Dnnf` and
`LogicalOptimizer.Formats` for d-DNNF model counting or DIMACS/WCNF/OPB interchange, or
install `LogicalOptimizer.Full` to get everything in one line. For a minimal dependency
set, reference the individual layer packages (`.Core` / `.Sat` / `.Bdd` /
`.Minimization`) directly.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
