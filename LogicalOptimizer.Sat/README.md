# LogicalOptimizer.Sat

**Pure managed SAT layer of the LogicalOptimizer toolkit.** A dependency-free CDCL solver
(two-watched literals, 1UIP learning, VSIDS, Luby restarts, LBD clause-database reduction)
with incremental solving under assumptions, unsat cores and DRAT proofs — plus Tseitin /
Plaisted–Greenbaum CNF encodings, cardinality / pseudo-Boolean constraints and weighted
MaxSAT.

```bash
dotnet add package LogicalOptimizer.Sat
```

```csharp
using LogicalOptimizer;

var solver = new SatSolver(3);
solver.AddClause(1, 2);   // a | b
solver.AddClause(-1, 3);  // !a | c

Console.WriteLine(solver.Solve());            // Satisfiable
Console.WriteLine(solver.GetValue(1) || solver.GetValue(2)); // True (model satisfies a | b)
```

## When to choose this package

Reference `.Sat` when you want the raw solver and CNF/cardinality/MaxSAT encoders without
the optimizer, BDD or minimizer. For reading standard problem files (DIMACS / WCNF / OPB)
into this solver, add `LogicalOptimizer.Formats`.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
