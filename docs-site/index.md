---
_layout: landing
title: LogicalOptimizer
---

# LogicalOptimizer

**A .NET toolkit and CLI for parsing, optimizing, and transforming Boolean expressions, with no third-party runtime dependency** — with *provable* minimization and mandatory equivalence verification of every result.

LogicalOptimizer covers exact Quine–McCluskey minimization with an explicit proof status, a built-in CDCL SAT solver, an ROBDD engine, d-DNNF knowledge compilation, Tseitin/Plaisted–Greenbaum CNF, cardinality/pseudo-Boolean/MaxSAT encodings, and DIMACS/BLIF/Verilog/LaTeX exporters — in one managed package set with **no third-party runtime dependency**. How that scope compares against Z3, LogicNG, SymPy and PyEDA, measured on a pinned corpus with pinned competitor versions: [comparison results](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/comparison/merged.md).

> [!NOTE]
> Two guarantees the library never trades away: **no third-party runtime dependency** in any shipped package, and **explainability plus mandatory verification** — every returned result is checked equivalent to the input (by truth table up to 12 variables, by SAT miter beyond). Both terms are defined, and linked to the test or CI check that backs them, in [CLAIMS.md](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/CLAIMS.md).

## Get started

```bash
# Library (facade pulls in the four core engine packages; add .Dnnf separately)
dotnet add package LogicalOptimizer

# CLI as a global dotnet tool -> command: logical-optimizer
dotnet tool install -g LogicalOptimizer.Cli
```

```bash
logical-optimizer "a & b | a & c"
# Optimized: a & (b | c)
```

## Documentation map

| Section | What it covers |
|---|---|
| [Introduction / Getting Started](articles/introduction.md) | What the library is, install, first example |
| [Formula construction & the AST](articles/formula-construction.md) | `FormulaFactory`, n-ary AST, `AstFormatter`, `AstMetrics` |
| [Optimizer & options](articles/optimizer-and-options.md) | `BooleanExpressionOptimizer`, `OptimizationOptions`, AIG rewriting (v3.0 default), quality analysis |
| [Operation contracts & statuses](articles/contracts-and-statuses.md) | `MinimizationStatus`, `ComputationStatus`, what "provably minimal" means |
| [Resource budgets & the zone model](articles/budgets-and-zones.md) | `ResourceBudget`, `PerformanceValidator`, variable-count routing |
| [Normal forms & transformations](articles/normal-forms.md) | CNF / DNF / ANF, `Transformations`, equisatisfiable Tseitin CNF, `TruthTable` |
| [Two-level minimization](articles/minimization.md) | `TruthTableMinimizer`, don't-cares, CSV parsing, multi-output tables |
| [SAT solving, cardinality, PB & MaxSAT](articles/sat-solving.md) | `SatSolver`, assumptions, unsat cores, DRAT, `CardinalityEncoder`, `PseudoBooleanEncoder`, `MaxSatSolver` |
| [Binary decision diagrams](articles/bdd.md) | `BinaryDecisionDiagram`: model counting, enumeration, ordering |
| [Knowledge compilation & model counting](articles/knowledge-compilation.md) | `KnowledgeCompilation` / `DnnfCircuit`: exact/weighted `#SAT` |
| [Equivalence & backbones](articles/equivalence-and-backbones.md) | `FormulaAnalysis`, `EquivalenceChecker`, `CheckWithProof`, pluggable checkers |
| [Export formats](articles/exporters.md) | DIMACS / BLIF / Verilog / LaTeX / CSV / C# code generation |
| [Packages & architecture](articles/packages-and-architecture.md) | The 7-package split, layering, `FormulaFactory`, canonical n-ary AST |
| [CLI usage](articles/cli-usage.md) | Every flag with verified example outputs |
| [Migration to v2.0](articles/migration-v2.md) | Breaking changes and how to adapt |
| [Testing overview](articles/testing-overview.md) | The structured functional suite and the ten techniques layered on it |
| [API Reference](api/index.md) | Generated from the XML doc comments of the seven library packages |

Every code example across these articles is mirrored by an executed, asserted test in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs` — the outputs shown are real.

## Where it fits

LogicalOptimizer is a *propositional* Boolean reasoning toolkit for .NET. It is **not** a replacement for Z3 (full SMT), ABC (logic synthesis), CUDD (industrial BDD), or a complete Espresso. What it offers instead is a specific combination: everything in-house and pure managed, and every optimization result verified equivalent to the input with an explicit minimality status. Whether that combination fits your case — and where the alternatives are the better choice — is worked through in [Choosing a tool](articles/choosing-a-tool.md).
