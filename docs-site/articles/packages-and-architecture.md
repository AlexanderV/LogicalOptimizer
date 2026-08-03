# Packages & Architecture

## The packages

Since v4.0 LogicalOptimizer ships as **two** NuGet packages
([decision record](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/decisions/package-consolidation-v4.md)):

| Package | Contents |
|---|---|
| **LogicalOptimizer** | The whole library: all seven assemblies below in one package. |
| **LogicalOptimizer.Cli** | The `logical-optimizer` global dotnet tool. |

The pre-4.0 per-layer package IDs (`.Core` / `.Sat` / `.Bdd` / `.Dnnf` / `.Formats` /
`.Minimization` / `.Full`) remain installable during a transition period as **deprecated
forwarding shells** — no code, a single dependency on `LogicalOptimizer` — so existing
references keep compiling unchanged.

## The assembly layering

Inside the single package, the code stays split into seven assemblies whose dependencies
are **acyclic and downward-only** — an internal architecture contract enforced by an
architecture test, no longer a package boundary:

| Assembly | Responsibility |
|---|---|
| **LogicalOptimizer.Core** | n-ary AST, `FormulaFactory` (parse + canonicalize), `AstFormatter`, `TruthTable`, metrics, `ResourceBudget`, `PerformanceValidator`. Depends on nothing. |
| **LogicalOptimizer.Sat** | CDCL solver, Tseitin / Plaisted–Greenbaum CNF, cardinality / pseudo-Boolean, MaxSAT. Depends on Core. |
| **LogicalOptimizer.Bdd** | ROBDD with hash-consing, model counting, quantification, sifting. Depends on Core. |
| **LogicalOptimizer.Dnnf** | Top-down d-DNNF knowledge compiler: exact `#SAT` model counting, weighted model counting, model enumeration. Depends on Sat + Core. See [Knowledge Compilation & Model Counting](knowledge-compilation.md). |
| **LogicalOptimizer.Formats** | DIMACS / WCNF / OPB parsers and round-trip writers with engine hand-off. Depends on Sat + Core. See [CLI usage](cli-usage.md). |
| **LogicalOptimizer.Minimization** | Quine–McCluskey, SAT prime cover, Espresso-lite, multi-output CSV. Depends on Sat + Core. |
| **LogicalOptimizer** (facade) | `BooleanExpressionOptimizer`, the rewrite pipeline, `EquivalenceChecker`, `FormulaAnalysis`, exporters. Depends on Core/Sat/Bdd/Minimization. |

`LogicalOptimizer.Dnnf` and `LogicalOptimizer.Formats` are standalone: they are consumed
directly rather than referenced by the facade's own code.

```mermaid
graph TD
    CLI["LogicalOptimizer.Cli"]
    Facade["LogicalOptimizer (facade)"]
    Min["LogicalOptimizer.Minimization"]
    Sat["LogicalOptimizer.Sat"]
    Bdd["LogicalOptimizer.Bdd"]
    Dnnf["LogicalOptimizer.Dnnf"]
    Formats["LogicalOptimizer.Formats"]
    Core["LogicalOptimizer.Core"]

    CLI --> Facade
    CLI --> Dnnf
    CLI --> Formats
    Facade --> Min
    Facade --> Sat
    Facade --> Bdd
    Facade --> Core
    Min --> Sat
    Min --> Core
    Sat --> Core
    Bdd --> Core
    Dnnf --> Sat
    Dnnf --> Core
    Formats --> Sat
    Formats --> Core
```

(The `LogicalOptimizer.Cli` node is the tool package; every other node in the graph is
an assembly inside the single `LogicalOptimizer` package.)

## `FormulaFactory` — the construction entry point

Since v2.0 `FormulaFactory` is the single **canonical** way to build And/Or trees: it is the
entry point for constructing and parsing formulas (`Parse`, `And` / `Or` / `Not` /
`Variable`, `Import`), and it canonicalizes at construction time. The public low-level
`AndNode`/`OrNode` constructors still build raw, non-canonical ASTs for callers that need
them, but only `FormulaFactory` guarantees the invariants below:

- **flatten** — `a & (b & c)` becomes one `AndNode` with operands `[a, b, c]`;
- **sort** — operands take a stable canonical order (`c & a & b` → `a & b & c`);
- **dedup** — `a & a` → `a` (idempotence);
- **constant folding** — `a & 1` → `a`, `a | 1` → `1`, `!!a` → `a`;
- **complement folding** — `a & !a` → `0`, `a | !a` → `1`;
- **interning** — structurally equal factory-built trees are the *same instance*, so
  reference equality works for canonical trees.

Two consequences worth internalizing:

1. **Degenerate formulas fold to constants at parse time.** `f.Parse("a | !a")` returns
   the constant `1`, not an `OrNode`.
2. **Output strings are canonically ordered.** `f.Parse("c & a & b").ToString()` is
   `"a & b & c"`.

```csharp
var f = new FormulaFactory();
var parsed = f.Parse("c & a & b");
Console.WriteLine(parsed);                          // a & b & c
var and = (AndNode)parsed;
Console.WriteLine(and.Operands.Count);              // 3 (n-ary, flattened)
Console.WriteLine(ReferenceEquals(parsed, f.And(    // True (interning)
    f.Variable("a"), f.Variable("b"), f.Variable("c"))));
```

## The n-ary canonical AST

The canonical core is `And` / `Or` / `Not` / `Variable` / `Constant`. And/Or are **n-ary**:
one n-ary `AndNode` / `OrNode` counts as **1 node** regardless of how many operands it has
(this is the v2 cost model — see the [migration guide](migration-v2.md)). The derived
binary nodes `XorNode` / `ImpNode` / `EqvNode` / `NandNode` / `NorNode` live *outside* the
canonical core: they are used only for extended-syntax parsing and pattern-recognition
display; `FormulaFactory.Import` decomposes them into And/Or/Not.

## Optimization flow (facade)

Every result is verified equivalent to the input before it is returned; minimality claims
carry an explicit status.

```mermaid
flowchart TD
    In["expression text"] --> Parse["FormulaFactory.Parse<br/>flatten · sort · dedup · fold · intern"]
    Parse --> Val["PerformanceValidator<br/>length / nesting / variable limits"]
    Val --> Pipe["rewrite pipeline<br/>fixpoint loop, ≤20 iterations, 10s guard"]
    Pipe --> Zone{variables?}
    Zone -- "≤ 10" --> QMg["exact QM<br/>MinimalProven guaranteed"]
    Zone -- "11–12" --> QMb["exact QM under budgets"]
    Zone -- "13–24" --> SatPath["SAT prime cover<br/>after SAT-miter proof"]
    Zone -- "> 24" --> Esp["Espresso-lite cube lists"]
    QMg --> Sel["SelectCheapest<br/>literals, then nodes"]
    QMb --> Sel
    SatPath --> Sel
    Esp --> Sel
    Sel --> Guard{"soundness guard<br/>≤12: truth table · >12: SAT miter"}
    Guard -- "equivalent" --> Out["OptimizationResult"]
    Guard -- "refuted (bug)" --> Roll["rollback to input"] --> Out
```

## Internal engines

Beyond the facade pipeline, the packages expose the standalone engines directly: the CDCL
`SatSolver` (two-watched literals, 1UIP learning, heap-VSIDS, Luby restarts, LBD clause-DB
reduction, subsumption; incremental solving with unsat cores and DRAT proofs), the
`BinaryDecisionDiagram` ROBDD (model counting, `Exists` / `ForAll`, `Restrict` / `Compose`,
`BuildWithBestOrder` / sifting), the cardinality / pseudo-Boolean / MaxSAT encoders, and
the two-level minimizers. See the [API Reference](../api/index.md) for the full member list.
