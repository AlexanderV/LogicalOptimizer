# CLI Usage

Install the CLI as a global .NET tool (the command is `logical-optimizer`):

```bash
dotnet tool install -g LogicalOptimizer.Cli
```

The examples below use `logical-optimizer "<expr>"`. Running from a source checkout is
equivalent — substitute `dotnet run --project LogicalOptimizer.Cli -c Release -- "<expr>"`.
All outputs shown are verified against the built CLI.

## Default optimize

```bash
logical-optimizer "a & b | a & c"
```

```text
Original: a & b | a & c
Optimized: a & (b | c)
CNF: a & (b | c)
DNF: a & b | a & c
Variables: [a, b, c]
```

A truth table is appended for expressions with ≤ 6 variables; an `Advanced:` line appears
only when a pattern (XOR / implication / equivalence) is recognized.

## Main flags

| Flag | Effect |
|---|---|
| `--cnf` | Output only the Conjunctive Normal Form |
| `--dnf` | Output only the Disjunctive Normal Form |
| `--anf` | Output only the Algebraic Normal Form (Zhegalkin / Reed–Muller polynomial) |
| `--advanced` | Include advanced logical forms (XOR / `→` / `↔`) |
| `--truth-table` | Output only the truth table |
| `--cnf-mode=tseitin` | Equisatisfiable linear-size CNF (Tseitin) instead of the distributive CNF |
| `--cnf-mode=equivalent` | Distributive (logically equivalent) CNF — the default |
| `--outputs=Name1,Name2 <csv>` | Multi-output CSV minimization with shared cubes |
| `--csv "<csv>"` | Parse a CSV truth table (also auto-detected for `.csv` files) |
| `--verbose` | Detailed output: metrics, iterations, elapsed time, `Minimality:` status |
| `--demo` | Features demonstration |
| `--benchmark` | Performance testing |
| `--stress` | Extreme stress testing for large expressions |
| `--csv-example` | Print the expected CSV truth-table format |
| `--help`, `-h` | Usage and supported operators |

The complete flag set is locked by `DocExamplesTests.Cli_RecognizesEveryDocumentedFlag`.

### `--cnf`

```bash
logical-optimizer --cnf "a & b | c"
# (a | c) & (b | c)
```

### `--dnf`

```bash
logical-optimizer --dnf "(a | b) & c"
# a & c | b & c
```

### `--anf`

Emits the canonical XOR-of-AND-monomials (Zhegalkin / Reed–Muller) form:

```bash
logical-optimizer --anf "a & !b | !a & b"
# a XOR b

logical-optimizer --anf "a | b"
# (a XOR b) XOR (a & b)
```

### `--advanced`

```bash
logical-optimizer --advanced "a & !b | !a & b"
# a XOR b
```

Implication and equivalence patterns render as `a → b` and `a ↔ b` respectively.

### `--cnf-mode=tseitin`

Produces a linear-size equisatisfiable CNF (auxiliary variables) instead of the
distributive CNF — the right choice when handing the formula to a SAT solver, since it
avoids the exponential blow-up distribution can cause. The polarity-based
Plaisted–Greenbaum style cuts clause count up to ~2×.

### `--outputs` (multi-output CSV)

The CSV can be passed inline (using `\n` for row breaks) or as a file. Shared don't-cares
and PLA-style cube sharing are exploited across the output columns:

```bash
logical-optimizer --outputs=Sum,Carry "a,b,Sum,Carry\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1"
```

```text
Sum = a & !b | b & !a
Carry = a & b
```

### `--verbose`

Adds a metrics block including the explicit minimality status, for example
`Minimality: MinimalProven` (see [Operation contracts & statuses](contracts-and-statuses.md)).

## Operators

| Operator | Meaning | Precedence |
|---|---|---|
| `!` | NOT | 1 (highest) |
| `&` | AND | 2 |
| `\|` | OR | 3 (lowest) |
| `()` | grouping | — |
| `0`, `1` | constants | — |
