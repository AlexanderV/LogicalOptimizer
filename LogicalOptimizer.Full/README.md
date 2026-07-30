# LogicalOptimizer.Full

**The whole LogicalOptimizer toolkit in one install.** A code-less meta-package: it ships
no assembly of its own and just depends on every managed package, so a single reference
brings in the optimizer, SAT, BDD, minimization, d-DNNF knowledge compilation and
DIMACS/WCNF/OPB import/export.

```bash
dotnet add package LogicalOptimizer.Full
```

```csharp
using LogicalOptimizer;

// The facade (optimization, SAT, BDD, minimization)…
var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");
Console.WriteLine(result.Optimized);                                   // a & (b | c)

// …plus the packages the facade does not pull in on its own:
var models = KnowledgeCompilation.CompileToDnnf(                        // .Dnnf
    new FormulaFactory().Parse("(a | b) & (b | c)")).CountModels();     // 5
var cnf = DimacsParser.Parse(new StringReader("p cnf 2 2\n1 2 0\n-1 0\n")); // .Formats
Console.WriteLine(cnf.Solve());                                        // Satisfiable
```

## When to choose this package

Pick `LogicalOptimizer.Full` when you want the complete toolkit without deciding which
layers you need up front. If you care about a minimal dependency set, install the facade
(`LogicalOptimizer`) or the individual layer packages (`.Core` / `.Sat` / `.Bdd` /
`.Dnnf` / `.Minimization` / `.Formats`) instead.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
