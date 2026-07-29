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

Other flags: `--cnf` / `--dnf` / `--anf` for a single normal form, `--advanced` for
XOR/IMP/EQV patterns, `--truth-table`, `--verbose` for metrics, and `--outputs=A,B` for
multi-output CSV minimization. Run `logical-optimizer --help` for the full list.

## When to choose this package

Install the CLI for scripts, CI checks and quick one-off optimization or equivalence work
without writing code. To call the same engines from a .NET application, use the
`LogicalOptimizer` library package instead.

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
