---
_layout: landing
title: LogicalOptimizer
---

# LogicalOptimizer

**A dependency-free .NET toolkit and CLI for parsing, optimizing, and transforming Boolean expressions** — with *provable* minimization and mandatory equivalence verification of every result.

LogicalOptimizer is the most complete managed .NET Boolean-optimization toolkit: exact Quine–McCluskey minimization with an explicit proof status, a built-in CDCL SAT solver, an ROBDD engine, Tseitin/Plaisted–Greenbaum CNF, cardinality/pseudo-Boolean/MaxSAT encodings, and DIMACS/BLIF/Verilog/LaTeX exporters — with **zero production dependencies**.

> [!NOTE]
> Two guarantees the library never trades away: **zero production dependencies**, and **explainability plus mandatory verification** — every returned result is checked equivalent to the input (by truth table up to 12 variables, by SAT miter beyond).

## Get started

```bash
# Library (facade pulls in all four engine packages)
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
| [Operation contracts & statuses](articles/contracts-and-statuses.md) | `MinimizationStatus`, `ComputationStatus`, what "provably minimal" means |
| [Resource budgets & the zone model](articles/budgets-and-zones.md) | `ResourceBudget`, `PerformanceValidator`, variable-count routing |
| [Packages & architecture](articles/packages-and-architecture.md) | The 5-package split, layering, `FormulaFactory`, canonical n-ary AST |
| [CLI usage](articles/cli-usage.md) | Flags and verified example outputs |
| [Migration to v2.0](articles/migration-v2.md) | Breaking changes and how to adapt |
| [Testing overview](articles/testing-overview.md) | The ten-technique testing strategy |
| [API Reference](api/index.md) | Generated from the XML doc comments of the five library packages |

## Where it fits

LogicalOptimizer is the *most complete managed .NET Boolean-optimization toolkit* — it is **not** a replacement for Z3 (full SMT), ABC (logic synthesis), CUDD (industrial BDD), or a complete Espresso. It is the best-in-niche managed option: everything in-house, everything verified.
