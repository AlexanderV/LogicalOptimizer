# LogicalOptimizer.Dnnf

**d-DNNF knowledge compilation for the LogicalOptimizer toolkit.** Compiles a formula once
to a deterministic, decomposable NNF circuit (top-down decision-DNNF with component
caching), then answers counting and enumeration queries in time linear in the compiled
circuit.

```bash
dotnet add package LogicalOptimizer.Dnnf
```

```csharp
using LogicalOptimizer;

var circuit = KnowledgeCompilation.CompileToDnnf(
    new FormulaFactory().Parse("(a | b) & (b | c)"));

Console.WriteLine(circuit.CountModels());            // 5   (exact #SAT, BigInteger)
Console.WriteLine(circuit.EnumerateModels().Count()); // 5
Console.WriteLine(circuit.WeightedModelCount(new Dictionary<string, (double, double)>
    { ["a"] = (0.5, 0.5), ["b"] = (0.5, 0.5), ["c"] = (0.5, 0.5) })); // 0.625
```

## When to choose this package

Pick `.Dnnf` when you compile a formula once and then run **many** counting queries against
it — exact `#SAT`, weighted model counting, conditioning and evidence — each linear in the
circuit. For a single count or for quantification, `LogicalOptimizer.Bdd` is usually enough.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
