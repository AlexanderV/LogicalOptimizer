# LogicalOptimizer.Bdd

**Reduced Ordered Binary Decision Diagrams for the LogicalOptimizer toolkit.** Hash-consed
ROBDDs with exact model counting, existential/universal quantification, restriction,
functional composition and variable-order optimization (static heuristics plus sifting).

```bash
dotnet add package LogicalOptimizer.Bdd
```

```csharp
using LogicalOptimizer;

var bdd = BinaryDecisionDiagram.BuildWithBestOrder(new FormulaFactory().Parse("a & b | c"));

Console.WriteLine(bdd.CountSatisfyingAssignments()); // 5
Console.WriteLine(bdd.Evaluate(new Dictionary<string, bool>
    { ["a"] = true, ["b"] = true, ["c"] = false })); // True
```

## When to choose this package

Reach for `.Bdd` when you need a canonical function representation for **exact model
counting**, quantifier elimination, equivalence by structural identity, or counting valid
configurations of a feature model. For linear-time repeated counting queries on one
compiled circuit, see `LogicalOptimizer.Dnnf` instead.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
