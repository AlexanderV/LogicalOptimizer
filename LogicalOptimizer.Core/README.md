# LogicalOptimizer.Core

**The canonical Boolean layer of the LogicalOptimizer toolkit.** The n-ary AST,
`FormulaFactory` (a canonicalizing parser and builder), `AstFormatter`, `AstMetrics`,
truth tables and the internal And-Inverter Graph — dependency-free and Native-AOT-safe.

```bash
dotnet add package LogicalOptimizer.Core
```

```csharp
using LogicalOptimizer;

var f = new FormulaFactory();
var ast = f.Parse("c & a & b");

Console.WriteLine(ast);                        // a & b & c   (canonical operand order)
Console.WriteLine(AstMetrics.CountLiterals(f.Parse("a & (b | c)"))); // 3
Console.WriteLine(f.Parse("a | !a"));          // 1           (folded at parse time)
```

`FormulaFactory` is the single construction path: it flattens nested And/Or chains, sorts
operands into a stable canonical order, removes duplicates, folds constants and
complements, and interns the result — so equal formulas are the same instance.

## When to choose this package

Reference `.Core` alone when you only need to parse, canonicalize, format, measure or
build Boolean formulas and generate truth tables — with no solver, BDD or minimizer
pulled in. Every other package in the toolkit builds on this one.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
