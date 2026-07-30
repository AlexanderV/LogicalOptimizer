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
Equivalent: proven
Minimality: proven
Cost: 4 -> 3 literals
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
| `--format=json` (alias `--json`) | Machine-readable JSON report on stdout (stable `schemaVersion`); diagnostics stay on stderr |
| `--trace` | Append the [diagnostic trace](diagnostic-trace.md): engine chosen and why, budgets, candidate costs, proof paths, fallbacks |
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

The complete flag set is locked by `DocExamplesTests.Cli_RecognizesEveryDocumentedFlag`, and the
[standard-format verbs](#standard-format-problem-files) below by
`DocExamplesTests.Cli_RecognizesEveryDocumentedStandardFormatVerb`.

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

### `--format=json`

Emits a stable, versioned report to stdout for CI and tooling (human diagnostics stay on
stderr). `--json` is an alias, and the spaced form `--format json` also works.

```bash
logical-optimizer --format=json "a & b | a & c"
```

```json
{
  "schemaVersion": 1,
  "input": "a & b | a & c",
  "sourceFormat": "expression",
  "optimized": "a & (b | c)",
  "equivalent": true,
  "minimality": "MinimalProven",
  "cost": { "originalLiterals": 4, "optimizedLiterals": 3 },
  "cnf": { "expression": "a & (b | c)", "status": "Computed", "minimality": "MinimalProven" },
  "dnf": { "expression": "a & b | a & c", "status": "Computed" },
  "variables": ["a", "b", "c"]
}
```

`advanced` appears only when an XOR/`→`/`↔` pattern is detected. On an invalid expression the
document carries an `error` object instead of the result fields. Fields are only added within
a `schemaVersion`, never renamed or removed.

#### JSON with a CSV truth table

`--format=json` accepts a CSV input — inline or a `*.csv` file — as one single-output report. The
document names the input the CLI **received**, and reports the expression derived from the table
separately, so an archived report stays traceable to the table it came from:

```bash
logical-optimizer --format=json --csv "a,b,Result\n0,0,0\n0,1,1\n1,0,1\n1,1,1"
```

```json
{
  "schemaVersion": 1,
  "input": "a,b,Result\\n0,0,0\\n0,1,1\\n1,0,1\\n1,1,1",
  "sourceFormat": "csv",
  "analyzedExpression": "(!a & b) | (a & !b) | (a & b)",
  "optimized": "a | b",
  "equivalent": true,
  "minimality": "MinimalProven"
}
```

Every verdict in the report — `optimized`, `equivalent`, `minimality`, `cost`, the normal forms —
is about `analyzedExpression`. For a `*.csv` file, `input` is the path as passed. With a plain
expression `sourceFormat` is `"expression"` and `analyzedExpression` is omitted, because `input`
already is the analyzed expression.

`--format=json` is **not** available with `--outputs`: that mode emits one expression per output
column, which a single-expression report cannot carry. The combination is rejected as a usage
error (exit code 1).

The report is a published contract, not just a convention:

- **JSON Schema** (Draft 2020-12):
  [`cli-report-v1.schema.json`](https://AlexanderV.github.io/LogicalOptimizer/schema/cli-report-v1.schema.json)
  — validate a report with, for example,
  `check-jsonschema --schemafile cli-report-v1.schema.json report.json`;
- **golden examples** for every outcome a consumer must handle — success, `BudgetExceeded`
  minimality, a `TooLarge` normal form, a CSV source, a structured parse error, and a bare
  processing error:
  [`schema/examples/`](https://github.com/AlexanderV/LogicalOptimizer/tree/main/schema/examples);
- **what may change and what may not**, in
  [`schema/README.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/schema/README.md).

The schema is closed, and CI validates both the committed examples and freshly generated output
against it, so a field cannot appear, disappear or change type without a reviewed schema diff.

### `--trace`

Explains how the result was reached — which engine ran and on what threshold, the budgets in
force, every candidate's cost, which one was adopted or rejected, how equivalence and
minimality were discharged, and any fallback. Works with both output formats:

```bash
logical-optimizer --trace "a & b | a & c"               # under a "Trace:" heading
logical-optimizer --format=json --trace "a & b | a & c" # as a "trace" array
```

The trace is diagnostic: unlike the JSON report's fields, its wording and ordering are not a
stability contract. See [Diagnostic Trace](diagnostic-trace.md).

## Standard-format problem files

Everything above operates on a Boolean *expression*. The CLI also takes four **verbs** that read a
problem **file** in a standard competition format through
[`LogicalOptimizer.Formats`](packages-and-architecture.md) and dispatch it to the in-house SAT,
MaxSAT, pseudo-Boolean or d-DNNF engine. The verb is the first argument, followed by exactly one
file path:

| Verb | Input format | Engine | Prints |
|---|---|---|---|
| `solve <file>` | DIMACS CNF | `SatSolver` (CDCL) | `s SATISFIABLE` + a `v` model line, `s UNSATISFIABLE`, or `s UNKNOWN` |
| `maxsat <file>` | WCNF (weighted partial MaxSAT) | `MaxSatSolver` | `s OPTIMUM FOUND` + `o <cost>` + a `v` model line |
| `solve-pb <file>` | OPB (pseudo-Boolean) | `PseudoBooleanEncoder` → `SatSolver` | as `solve` |
| `count <file> --engine dnnf` | DIMACS CNF | `KnowledgeCompilation` (d-DNNF) | the exact model count, one line |

The `s` / `o` / `v` line convention is the usual competition output, so existing tooling can
consume it unchanged.

### `solve` (DIMACS CNF)

```text
p cnf 3 2
1 -3 0
2 3 -1 0
```

```bash
logical-optimizer solve problem.cnf
```

```text
s SATISFIABLE
v -1 -2 -3 0
```

The `v` line lists one signed literal per variable declared in the header — negative for false,
positive for true — terminated by `0`.

### `maxsat` (WCNF)

```text
p wcnf 2 3 10
10 1 2 0
1 -1 0
1 -2 0
```

```bash
logical-optimizer maxsat problem.wcnf
```

```text
s OPTIMUM FOUND
o 1
v -1 2 0
```

`o` is the total weight of the falsified soft clauses in the optimal assignment. Unsatisfiable
hard clauses print `s UNSATISFIABLE`; a budget that runs out prints `s UNKNOWN` (with the best
`o` found so far, when there is one) rather than passing a non-optimal answer off as optimal.

### `solve-pb` (OPB)

```text
* #variable= 2 #constraint= 1
+1 x1 +1 x2 >= 1;
```

```bash
logical-optimizer solve-pb problem.opb
```

```text
s SATISFIABLE
v -1 2 0
```

The constraints are encoded to CNF and handed to the same CDCL solver. Only the problem's own
variables appear in the `v` line; the auxiliary variables the encoding introduces are not
reported.

### `count` (exact #SAT)

```bash
logical-optimizer count problem.cnf --engine dnnf
```

```text
5
```

The formula is compiled to a d-DNNF circuit and counted exactly — the result is a `BigInteger`,
so it does not overflow on large formulas. Variables declared in the header but absent from every
clause are free and are accounted for. `--engine` currently accepts one value, `dnnf`; the spaced
(`--engine dnnf`) and joined (`--engine=dnnf`) forms both work.

### Errors

A missing file, a malformed problem, an unknown option, an unsupported `--engine` value or an
exhausted budget is reported on **stderr** and exits with code `1`. A solved problem exits `0` —
including `s UNSATISFIABLE`, which is an answer, not a failure.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Usage error (invalid arguments) |
| `2` | Processing error (e.g. an invalid expression) |

The standard-format verbs use `0` and `1` only: every parse, file and budget failure is a `1`.

## Operators

| Operator | Meaning | Precedence |
|---|---|---|
| `!` | NOT | 1 (highest) |
| `&` | AND | 2 |
| `\|` | OR | 3 (lowest) |
| `()` | grouping | — |
| `0`, `1` | constants | — |
