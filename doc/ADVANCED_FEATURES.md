# LogicalOptimizer Advanced Features Guide

This guide covers the **tooling** built around the optimizer: exporters, the quality analyzer,
AST visualization and the built-in benchmark runner. Everything here is available from the
`LogicalOptimizer` facade package.

The reasoning **engines** — CDCL SAT, ROBDD, d-DNNF knowledge compilation, cardinality /
pseudo-Boolean / MaxSAT encodings, AIG rewriting and the two-level minimizers — have their own
pages in the capability guide, each with a runnable, asserted example:
<https://AlexanderV.github.io/LogicalOptimizer/>.

---

## 1. Multi-format export

### Supported formats (6)

| Format | Purpose |
|---|---|
| **DIMACS CNF** | standard input for SAT solvers |
| **BLIF** | Berkeley Logic Interchange Format, for circuit synthesis |
| **Verilog** | logic circuit description |
| **CSV** | truth tables for spreadsheets and analysis |
| **Mathematical notation** | Unicode `∧ ∨ ¬` |
| **LaTeX** | `\land \lor \lnot` for papers |

```csharp
// All BooleanExpressionExporter methods are static and take the expression string.

string dimacs  = BooleanExpressionExporter.ToDimacs("(a | b) & (a | c)");
string blif    = BooleanExpressionExporter.ToBlif("a & b | c", "mymodule");
string verilog = BooleanExpressionExporter.ToVerilog("!(a & b)", "logic_gate");
string csv     = BooleanExpressionExporter.TruthTableToCsv("a & b | c");

string math  = BooleanExpressionExporter.ToMathematicalNotation("a & (b | c)"); // a ∧ (b ∨ c)
string latex = BooleanExpressionExporter.ToLatex("a & b | c");                  // c \lor a \land b
```

> **Canonical order.** The exporters parse through `FormulaFactory`, so the output is in
> **canonical operand order** — which is why `a & b | c` renders as `c \lor a \land b`: the single
> literal `c` sorts before the `a & b` term. The semantics are unchanged; only the printed order
> is normalized. See [Formula construction](https://AlexanderV.github.io/LogicalOptimizer/articles/formula-construction.html).

### Importing standard formats

The reverse direction lives in the separate `LogicalOptimizer.Formats` package: streaming,
budget-aware parsers and round-trip writers for **DIMACS CNF**, **WCNF** (weighted partial MaxSAT)
and **OPB** (pseudo-Boolean), each handing off directly to the in-house engines.

```csharp
using LogicalOptimizer;

var problem = DimacsParser.Parse(new StringReader("p cnf 2 2\n1 2 0\n-1 0\n"));
Console.WriteLine(problem.Solve());        // Satisfiable
var formula = problem.ToFormula();         // hand off to the BDD or d-DNNF engine
```

The CLI exposes the same path as four verbs — `solve`, `maxsat`, `solve-pb` and `count` — so an
existing competition or benchmark corpus can be run without writing code. See
[CLI usage](https://AlexanderV.github.io/LogicalOptimizer/articles/cli-usage.html).

---

## 2. Optimization quality analysis

### Metrics

- **Compression ratio** — how much the expression shrank
- **Complexity** — combined assessment (operators + literals + depth)
- **Optimality score** — a 0–100 **heuristic** quality rating
- **`IsOptimal`** — a *proven* property, unlike the score: true only when the exact minimizer
  proved the two-level minimum (`MinimizationStatus.MinimalProven`)
- **Applied rules** — which optimizations fired
- **Possible improvements** — recommendations for further optimization

`OptimalityScore` and `IsOptimal` are deliberately independent: a high score does **not** assert
optimality, and a proven-minimal result may score below 100.

### Usage

```csharp
// AnalyzeOptimization is a static method returning OptimizationQualityAnalyzer.QualityMetrics.
var result  = new BooleanExpressionOptimizer()
    .OptimizeExpression("(a | b) & (a | c)", new OptimizationOptions { IncludeMetrics = true });
var metrics = OptimizationQualityAnalyzer.AnalyzeOptimization(result);

Console.WriteLine($"Optimized:   {result.Optimized}");                  // a | b & c
Console.WriteLine($"Compression: {metrics.CompressionRatio:P1}");       // 71.4%
Console.WriteLine($"Complexity:  {metrics.Complexity:F1}");             // 5.1
Console.WriteLine($"Score:       {metrics.OptimalityScore}/100");       // 65/100
Console.WriteLine($"IsOptimal:   {metrics.IsOptimal}");                 // True (MinimalProven)
```

(Numeric formatting follows the current culture; the values above are shown with an invariant
decimal point.)

### Explaining a result

Where the quality analyzer scores the *outcome*, the **diagnostic trace** explains how it was
reached: the engine chosen and the threshold behind it, the budgets in force, every candidate's
cost, what was adopted or rejected, which proof path discharged equivalence, and any fallback.

```csharp
var traced = new BooleanExpressionOptimizer()
    .OptimizeExpression("a & b | a & c", new OptimizationOptions { IncludeTrace = true });

foreach (var entry in traced.Trace!.Entries)
    Console.WriteLine($"{entry.Category}: {entry.Message}");
```

The trace is **diagnostic, not a stability contract** — its wording and ordering may change in any
release; only its shape and the `category` domain are stable. On the CLI it is `--trace`.

---

## 3. Extended operators (XOR, IMP, EQV)

### Pattern recognition

```csharp
// XOR (exclusive OR) - detected via AST analysis
var xorNode = new XorNode(varA, varB);
// Pattern: (a & !b) | (!a & b) → a XOR b

// IMP (implication) - detected via AST analysis
var impNode = new ImpNode(varA, varB);
// Pattern: !a | b → a → b

// EQV (biconditional)
// Pattern: (a & b) | (!a & !b) → a ↔ b
```

### AST-based pattern detection

- **No regular expressions**: pure syntax-tree analysis
- **Structural pattern matching**: traverses AST nodes to identify patterns
- **Recursive detection**: works at any depth in the expression tree
- **Node replacement**: detected patterns are replaced with specialized AST nodes

Advanced forms are a **display** layer. All internal processing uses the core `&`, `|`, `!`
operators, and the text grammar cannot express `XOR` / `→` / `↔` as input.

### Functional completeness

The `NandNode` / `NorNode` types and their rewrite rules give NAND-only and NOR-only bases. These
rules are `internal` and currently have no production consumer; they are exercised by a
truth-table sweep over the full operand grid rather than by shape assertions alone.

---

## 4. Benchmarking and performance testing

### Built-in runner

```bash
# installed CLI tool
logical-optimizer --benchmark
# or from source
dotnet run --project LogicalOptimizer.Cli -- --benchmark
```

It walks a fixed ladder of expression classes — simple (`a & b`), medium
(`(a | b) & (a | c)`), complex multi-level, and auto-generated very complex — plus stress runs at
10 / 50 / 100 / 200 variables, reporting node-count change and elapsed time per row:

```text
Expression                               Nodes    Time (ms)  Result
------------------------------------------------------------------------
a & b                                    2→1      ...        ✓
(a | b) & (a | c)                        5→3      ...        ✓
```

Wall-clock numbers from this runner are **machine-dependent and indicative only**. Published,
citable figures come from the BenchmarkDotNet suite with the environment recorded alongside them:

```bash
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- --filter *
```

See [BENCHMARKS.md](BENCHMARKS.md) for results, methodology and the pinned corpus, and
[CLAIMS.md](CLAIMS.md#benchmark-result--comparison-numbers) for what a published number does and
does not assert.

---

## 5. AST visualization

### Text visualization

```csharp
// AstVisualizer is a static class. Both members take an AstNode
// (e.g. new FormulaFactory().Parse("a & (b | c)")).
string tree = AstVisualizer.VisualizeTree(ast);
Console.WriteLine(tree);
```

```text
└─ AND (&)
   ├─ Variable: 'a'
   └─ OR (|)
      ├─ Variable: 'b'
      └─ Variable: 'c'
```

### Compact visualization (expression + tree)

```csharp
string compact = AstVisualizer.GetCompactVisualization(ast);
```

```text
AST: a & (b | c)
Tree:
└─ AND (&)
   ├─ Variable: 'a'
   └─ OR (|)
      ├─ Variable: 'b'
      └─ Variable: 'c'
```

A fuller human-readable dump — original and optimized trees plus metrics — is available as
`OptimizationResult.DebugInfo` when `OptimizationOptions.IncludeDebugInfo` is set.

---

## 6. Usage recommendations

**For researchers**
- **DIMACS export** for handing formulas to external SAT solvers, and the `.Formats` **parsers**
  for reading an existing corpus back in
- **d-DNNF model counting** (`count --engine dnnf`) for exact `#SAT`, cross-verified against the
  ROBDD counter
- **Quality analysis** to evaluate the effectiveness of new algorithms

**For engineers**
- **Verilog / BLIF export** for logic-circuit synthesis flows
- **Benchmarks** for performance evaluation under working loads
- **`--format=json`** for CI: a versioned report with a published JSON Schema and stable exit codes

**For students**
- **AST visualization** to understand expression structure
- **Mathematical / LaTeX notation** for academic papers
- **Truth tables** and the **diagnostic trace** to see how a simplification was reached

---

## 7. Where this fits

LogicalOptimizer covers propositional Boolean reasoning in pure managed .NET with no third-party
runtime dependency, and verifies every optimization result against its input before returning it.
It is not a replacement for Z3 (full SMT), ABC (industrial logic synthesis), CUDD (industrial BDD)
or a complete Espresso — [Choosing a tool](https://AlexanderV.github.io/LogicalOptimizer/articles/choosing-a-tool.html)
works through the alternatives scenario by scenario, including where this project is weakest.

Every claim the project makes in public is defined — with the test or CI check that backs it and
the limits it deliberately does not assert — in [CLAIMS.md](CLAIMS.md). The test suite behind it
is described in [TESTING.md](TESTING.md).
