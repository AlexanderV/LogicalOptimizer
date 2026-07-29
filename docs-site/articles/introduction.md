# Introduction / Getting Started

## What LogicalOptimizer is

LogicalOptimizer is a lightweight, **dependency-free** .NET library and CLI for parsing,
optimizing, and transforming Boolean expressions. It is the most complete *managed*
.NET Boolean-optimization toolkit — everything is in-house (no native or third-party
runtime dependencies), and **every result it returns is verified equivalent to the
input** before you get it.

Two properties define the project and are never traded away:

- **Zero production dependencies** — the shipping packages have no runtime package references.
- **Explainability + mandatory verification** — minimality is reported with an explicit
  status (never a silent downgrade), and equivalence is checked on every optimization
  (truth table up to 12 variables, built-in CDCL SAT miter proof beyond).

What it is *not*: a replacement for Z3 (full SMT), ABC (logic synthesis), CUDD
(industrial BDD), or a complete Espresso. It is best-in-niche, not parity-across-the-board.

## Requirements

- **Library packages**: .NET 8.0 or higher (multi-targeted `net8.0;net10.0`).
- **CLI tool / building from source**: .NET 10 SDK.
- **OS**: Windows, Linux, or macOS.

## Installation

Add the library as a NuGet package. The `LogicalOptimizer` facade pulls in all four
engine packages; you can also depend on individual layers:

```bash
dotnet add package LogicalOptimizer               # facade: everything
# or pick individual layers:
dotnet add package LogicalOptimizer.Core          # n-ary AST, FormulaFactory, AstFormatter, truth tables
dotnet add package LogicalOptimizer.Sat           # CDCL solver, CNF encodings, MaxSAT
dotnet add package LogicalOptimizer.Bdd           # ROBDD
dotnet add package LogicalOptimizer.Minimization  # QM, Espresso-lite, multi-output
```

Install the CLI as a global .NET tool (the command is `logical-optimizer`):

```bash
dotnet tool install -g LogicalOptimizer.Cli
```

## Your first optimize

From the CLI:

```bash
logical-optimizer "a & b | a & c"
```

Which prints (running from source with
`dotnet run --project LogicalOptimizer.Cli -c Release -- "a & b | a & c"` gives the same):

```text
Original: a & b | a & c
Optimized: a & (b | c)
CNF: a & (b | c)
DNF: a & b | a & c
Variables: [a, b, c]

Truth Table:
| a | b | c | Result |
| - | - | - | ------ |
| 0 | 0 | 0 | 0      |
| 0 | 0 | 1 | 0      |
| 0 | 1 | 0 | 0      |
| 0 | 1 | 1 | 0      |
| 1 | 0 | 0 | 0      |
| 1 | 0 | 1 | 1      |
| 1 | 1 | 0 | 1      |
| 1 | 1 | 1 | 1      |
```

(A truth table is printed for expressions with ≤ 6 variables; an `Advanced:` line is
printed only when a pattern such as XOR / implication / equivalence is recognized.)

## Your first optimize (library)

```csharp
using LogicalOptimizer;

var optimizer = new BooleanExpressionOptimizer();
var result = optimizer.OptimizeExpression("a & b | a & c", includeMetrics: true);

Console.WriteLine(result.Original);   // a & b | a & c
Console.WriteLine(result.Optimized);  // a & (b | c)
Console.WriteLine(result.CNF);        // a & (b | c)
Console.WriteLine(result.DNF);        // a & b | a & c
Console.WriteLine(string.Join(", ", result.Variables));  // a, b, c

// The minimality provenance is explicit:
Console.WriteLine(result.MinimizationStatus);  // MinimalProven (≤10 vars)

// Every result is verified equivalent to the input before return:
Console.WriteLine(result.IsEquivalent());      // True
```

Building formulas programmatically always goes through `FormulaFactory`, the single
construction entry point (results are canonical and interned):

```csharp
var f = new FormulaFactory();
var parsed = f.Parse("c & a & b");
Console.WriteLine(parsed);                          // a & b & c  (canonical order)

var built = f.And(f.Variable("a"), f.Variable("b"), f.Variable("c"));
Console.WriteLine(ReferenceEquals(parsed, built));  // True (interning)
```

## Next steps

- [Formula construction & the AST](formula-construction.md) — `FormulaFactory`, the n-ary AST, `AstFormatter`.
- [Optimizer & options](optimizer-and-options.md) — `OptimizationOptions`, AIG rewriting (on by default in v3.0), quality analysis.
- [Operation contracts & statuses](contracts-and-statuses.md) — what "provably minimal" means.
- [Resource budgets & the zone model](budgets-and-zones.md) — how variable count routes the work.
- [Normal forms & transformations](normal-forms.md) — CNF / DNF / ANF / Tseitin.
- [Two-level minimization](minimization.md) — exact SOP/POS, don't-cares, CSV, multi-output.
- [SAT solving](sat-solving.md) · [Binary decision diagrams](bdd.md) · [Knowledge compilation](knowledge-compilation.md) · [Equivalence & backbones](equivalence-and-backbones.md) · [Export formats](exporters.md)
- [Packages & architecture](packages-and-architecture.md) — the nine-package split.
- [CLI usage](cli-usage.md) — every flag with verified output.

Every code example across these articles is mirrored by an executed, asserted test in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`, so the outputs shown are real.
