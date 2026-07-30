# LogicalOptimizer.Cli

**Boolean optimization and analysis from the command line.** A .NET global tool
(`logical-optimizer`) over the LogicalOptimizer toolkit: optimize expressions with
verified equivalence and explicit minimality, print truth tables, minimize CSV /
multi-output tables and export to other formats.

```bash
dotnet tool install -g LogicalOptimizer.Cli
```

```bash
logical-optimizer "a & b | a & c"
# Original: a & b | a & c
# Optimized: a & (b | c)
# Equivalent: proven
# Minimality: proven
# Cost: 4 -> 3 literals
# CNF: a & (b | c)
# DNF: a & b | a & c
# Variables: [a, b, c]
```

Other flags: `--format=json` for a stable machine-readable report (CI-friendly; exit codes
`0`/`1`/`2`), `--cnf` / `--dnf` / `--anf` for a single normal form, `--advanced` for
XOR/IMP/EQV patterns, `--truth-table`, `--trace` to explain how the result was reached,
`--verbose` for metrics, and `--outputs=A,B` for multi-output CSV minimization. Run
`logical-optimizer --help` for the full list.

## Standard-format problem files

Four verbs read a DIMACS / WCNF / OPB file and dispatch it to the in-house SAT, MaxSAT,
pseudo-Boolean or d-DNNF engine, printing the usual `s` / `o` / `v` competition lines:

```bash
logical-optimizer solve problem.cnf                  # DIMACS CNF satisfiability
logical-optimizer maxsat problem.wcnf                # WCNF weighted partial MaxSAT
logical-optimizer solve-pb problem.opb               # OPB pseudo-Boolean feasibility
logical-optimizer count problem.cnf --engine dnnf    # exact #SAT via d-DNNF
```

## When to choose this package

Install the CLI for scripts, CI checks, running an existing DIMACS/WCNF/OPB corpus, and
quick one-off optimization or equivalence work without writing code. To call the same
engines from a .NET application, use the `LogicalOptimizer` library package instead.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
