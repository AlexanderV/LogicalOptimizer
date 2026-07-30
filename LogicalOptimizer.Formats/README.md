# LogicalOptimizer.Formats

**Standard-format interoperability for the LogicalOptimizer toolkit.** Streaming,
budget-aware parsers and round-trip writers for DIMACS CNF, WCNF (weighted partial MaxSAT)
and OPB (pseudo-Boolean) problems — each hands off directly to the in-house SAT, MaxSAT and
pseudo-Boolean engines.

```bash
dotnet add package LogicalOptimizer.Formats
```

```csharp
using LogicalOptimizer;

var problem = DimacsParser.Parse(new StringReader("p cnf 2 2\n1 2 0\n-1 0\n"));

Console.WriteLine(problem.VariableCount); // 2
Console.WriteLine(problem.Solve());       // Satisfiable  (hands off to the built-in solver)
```

## When to choose this package

Add `.Formats` when you exchange problems with other tools through DIMACS / WCNF / OPB, or
run the toolkit over an existing competition/benchmark corpus. A parsed `CnfProblem` can
`Solve()` on the in-house solver, or convert `ToFormula()` for the BDD and d-DNNF engines.

To do the same from the command line without writing code, `LogicalOptimizer.Cli` exposes
this package as the `solve`, `maxsat`, `solve-pb` and `count` verbs.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
