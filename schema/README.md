# CLI JSON report contract

`logical-optimizer --format=json "<expression>"` writes exactly one JSON document to stdout.
This directory is the contract for that document:

| File | What it is |
|---|---|
| [`cli-report-v1.schema.json`](cli-report-v1.schema.json) | JSON Schema (Draft 2020-12) for `schemaVersion: 1` |
| [`examples/`](examples/) | One golden report per outcome a consumer must handle |

Both are enforced by
[`CliReportSchemaTests`](../LogicalOptimizer.Tests/Cli/CliReportSchemaTests.cs), which runs on
every CI build and checks that

- every example validates against the schema;
- the report the CLI produces **today** for each scenario validates **and** still byte-equals its
  committed example;
- the two outcomes are **mutually exclusive**: a document carrying both `optimized` and `error` is
  rejected, a success report missing `equivalent`/`minimality` is rejected, and an error report
  carrying a result-only field is rejected;
- the schema's enums are exactly the CLR enums (`MinimizationStatus`, `ComputationStatus`,
  `OptimizationTraceCategory`, `ParseErrorCode`) — so the schema cannot fall behind the code;
- an undeclared field is **rejected**, which is what makes the promises below verifiable rather
  than aspirational.

Those tests start from an already-computed result, so they cannot see how an input *reached* the
CLI. [`CliJsonInputContractTests`](../LogicalOptimizer.Tests/Cli/CliJsonInputContractTests.cs)
covers that seam by driving `Program.Main` itself and validating what lands on stdout — including
that stdout carries exactly one document and that progress messages stay on stderr.

## What `input` is, and what the verdicts are about

`input` is the argument **exactly as the CLI received it** — never something derived from it.
`sourceFormat` says what that argument was:

| `sourceFormat` | `input` | `analyzedExpression` |
|---|---|---|
| `expression` | the boolean expression, analyzed as written | absent |
| `csv` | the CSV truth table text, or the `*.csv` path | the sum-of-products derived from that table |

Everything else in the report — `optimized`, `equivalent`, `minimality`, `cost`, `cnf`, `dnf`,
`advanced`, `variables`, and `error.position`/`error.snippet` — is about **the analyzed
expression**: `analyzedExpression` when it is present, `input` otherwise. For the ordinary
expression invocation the two are the same string, which is why `analyzedExpression` is omitted
there rather than echoed.

This is what makes the document auditable: a report archived from a CI matrix names the CSV or the
file it came from, and its success and error forms agree on what `input` means.

> **Schema history.** Through the first published build, a run whose input was a CSV truth table
> wrote the *derived* sum-of-products into `input`, so the report could not say which CSV or which
> file produced it — while the error path for the same run wrote the received argument. The schema
> has always defined `input` as the argument as received, so this was the CLI disagreeing with the
> published contract, not a change of contract. `sourceFormat` and `analyzedExpression` are new
> additive fields; no report that validated before became invalid.

## Exactly one outcome

A report is **either** a success or a failure, never both:

| | success report | error report |
|---|---|---|
| Required | `optimized`, `equivalent`, `minimality` | `error` |
| Forbidden | `error` | `optimized`, `equivalent`, `minimality`, `cost`, `cnf`, `dnf`, `advanced`, `variables`, `trace` |
| Exit code | 0 | 2 |

`schemaVersion`, `input` and `sourceFormat` are in **both** — they describe the run, not its
outcome. `analyzedExpression` may appear in either: an error report carries it when the derivation
from a CSV succeeded and the failure came afterwards.

The schema enforces this with `oneOf` plus explicit `not` clauses. Branch on the presence of
`error` and nothing else — a validating document cannot contradict itself.

> **Schema history.** The first published copy of this file expressed the two outcomes as an
> `anyOf`, which accepts a document carrying *both* `optimized` and `error`. That was a defect in
> the schema, not a change of contract: the CLI has never emitted such a document, so no report
> that was valid before became invalid. Tightening it is therefore a fix, not a new schema version.

## Using it

```bash
logical-optimizer --format=json "a & b | a & c" > report.json
```

```csharp
using var doc = JsonDocument.Parse(File.ReadAllText("report.json"));
var root = doc.RootElement;

if (root.GetProperty("schemaVersion").GetInt32() != 1)
    throw new NotSupportedException("Unknown LogicalOptimizer report version");

if (root.TryGetProperty("error", out var error))
    return HandleFailure(error.GetProperty("code").GetString());   // exit code was 2

var optimized = root.GetProperty("optimized").GetString();
var proven = root.GetProperty("minimality").GetString() == "MinimalProven";
```

Validate a report against the schema from the command line, e.g. with
[`check-jsonschema`](https://github.com/python-jsonschema/check-jsonschema):

```bash
check-jsonschema --schemafile schema/cli-report-v1.schema.json report.json
```

## Examples

| Example | Shows |
|---|---|
| [`success-minimal-proven.json`](examples/success-minimal-proven.json) | The ordinary case: proven equivalent, proven minimal, cost before/after |
| [`budget-exceeded.json`](examples/budget-exceeded.json) | `minimality: "BudgetExceeded"` — sound result, optimality **not** proven |
| [`form-too-large.json`](examples/form-too-large.json) | `cnf.status: "TooLarge"` — the normal form hit its size budget |
| [`advanced-xor.json`](examples/advanced-xor.json) | The optional `advanced` field, present only for a real XOR/IMP/EQV pattern |
| [`csv-source.json`](examples/csv-source.json) | `sourceFormat: "csv"` — `input` is the truth table, `analyzedExpression` the expression derived from it |
| [`trace.json`](examples/trace.json) | `--trace`: how the result was reached (diagnostic, not contract — see below) |
| [`parse-error.json`](examples/parse-error.json) | A structured parse diagnostic: `code`, `position`, `length`, `expected`, `snippet` |
| [`processing-error.json`](examples/processing-error.json) | A failure with no structured diagnostic: `code: "processing_error"` |

There is deliberately **no example of `equivalent: false`.** The schema declares the field as a
boolean and a consumer should branch on it, but the optimize path is not expected to emit `false`:
the internal equivalence guard runs before the result is returned, so `false` would indicate a bug
in the library and is worth [reporting](../SUPPORT.md). Equivalence *checking* of two independent
expressions — including the counterexample for a non-equivalent pair — is a library API
(`EquivalenceChecker`), not part of this CLI report.

## What is stable within a version

Inside `schemaVersion: 1` the document is **additive only**:

- a field that is present today keeps its name, JSON type, and meaning;
- an existing enum member is never renamed or removed;
- a field is never made *newly* absent for an input that produces it today.

A consumer that ignores unknown fields and unknown enum members it does not care about will keep
working across every 1.x and 3.x release.

### Additive changes (minor/patch release, no version bump)

- a **new optional field**;
- a **new enum member** in `minimality`, `status`, `error.code`, or `trace[].category` — a new
  outcome the library can now report. Branch with a default case;
- a field that was previously absent for some inputs becoming present for more of them.

Adding one of these requires editing `cli-report-v1.schema.json` in the same commit: the schema is
closed (`unevaluatedProperties: false`), so CI fails until it is updated.

### Breaking changes (new schema version, new file, major release)

- renaming, removing, or retyping a field;
- changing what an existing field means;
- removing an enum member;
- making a field that is always present today optional.

These ship as `cli-report-v2.schema.json` with `schemaVersion: 2`. The previous schema file stays
in the repository, and the previous major line keeps emitting its own version for its support
window — see [SUPPORT.md](../SUPPORT.md#versioning-and-compatibility-policy).

## Explicitly outside the contract

Do not parse or assert on these; they change freely in any release:

- the wording of `error.message` — branch on `error.code`;
- the exact layout of `error.snippet` (caret position within the rendered line is cosmetic);
- the `trace` array: the number of entries, their order, and the text of `step`, `message`, and the
  keys of `data`. Only the shape (`category`/`step`/`message`/`data`) is stable, and `category` is
  the only field with an enumerated domain;
- the exact textual form of `analyzedExpression` — the derivation from a truth table may produce a
  different, equivalent cover in a later release. Its *presence* and meaning are contract; its
  spelling is not;
- the exact textual form of `optimized`, `cnf.expression`, and `dnf.expression`. A minor release
  may return a *smaller* expression for the same input. It will always be verified equivalent, and
  `MinimalProven` is never silently weakened — assert on `equivalent`, `minimality` and
  `cost.optimizedLiterals` rather than on a string;
- the human-readable `--format=text` and `--verbose` output, which is not covered by any schema.

## Exit codes

The exit code is part of the CLI contract and is stable across the 3.x line:

| Code | Meaning | Report |
|---:|---|---|
| 0 | Success | success report on stdout |
| 1 | Usage error (bad arguments) | none — message on stderr |
| 2 | Processing error (e.g. an invalid expression) | error report on stdout, message on stderr |

Diagnostics always go to stderr, so `--format=json` stdout is safe to pipe.
