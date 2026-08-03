# LogicalOptimizer

**Verified Boolean expression toolkit for .NET — the whole library in one package.**
Since v4.0 this single package ships all seven assemblies: the optimizer facade, Core
(parser/AST/truth tables), SAT (CDCL solver, Tseitin, MaxSAT), BDD, d-DNNF knowledge
compilation, Minimization (Quine–McCluskey, Espresso-lite) and Formats
(DIMACS/WCNF/OPB/BLIF/Verilog). No third-party runtime dependency.

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

## Upgrading from the pre-4.0 packages

The former layer packages (`LogicalOptimizer.Core` / `.Sat` / `.Bdd` / `.Dnnf` /
`.Formats` / `.Minimization` / `.Full`) are deprecated forwarding shells that depend on
this package, so existing references keep compiling — replace them with a single
`dotnet add package LogicalOptimizer` at your convenience.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
