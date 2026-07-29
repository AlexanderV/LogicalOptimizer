# Support

## Where to go

| Need | Channel |
|---|---|
| How do I do X? / API usage | [Documentation](https://AlexanderV.github.io/LogicalOptimizer/) — a runnable example per capability area, plus the [samples](samples/) |
| Something is wrong / a wrong result | [Bug report](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=bug_report.yml) |
| I need a capability | [Feature request](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=feature_request.yml) |
| Open-ended question, use-case feedback | [Discussions](https://github.com/AlexanderV/LogicalOptimizer/discussions) |
| Security vulnerability | **Do not** open an issue — see [SECURITY.md](SECURITY.md) |

Use-case feedback is genuinely useful: several roadmap items (batch/reuse APIs, additional
engines) are deliberately gated on evidence of real demand rather than speculation.

## What makes a report actionable

For a wrong or surprising result, include:

- the **exact input** — the expression string, CSV table, or DIMACS/WCNF/OPB file;
- the **observed** output and the **expected** output;
- the **package and version** (`LogicalOptimizer` 3.1.0, the CLI tool, …) and the target framework;
- for the CLI, the full command line — `--format=json` output is ideal, since it includes the
  minimality status and cost.

A one-line repro through the public API is worth more than a description:

```csharp
var result = new BooleanExpressionOptimizer().OptimizeExpression("your & expression");
Console.WriteLine($"{result.Optimized} | {result.IsEquivalent()} | {result.MinimizationStatus}");
```

## Versioning and compatibility policy

The project follows [Semantic Versioning](https://semver.org/):

- **Patch / minor** releases are **additive only** — no public API is renamed or removed, and
  no documented behaviour changes incompatibly. Upgrading within a major line is safe.
- **Major** releases may break the public API. Each one ships a migration guide (for example
  [MIGRATION-v2.md](MIGRATION-v2.md)) and a changelog entry.
- The public surface is **mechanically enforced**: a member-level baseline
  (`LogicalOptimizer.Tests/TestData/PublicApi.approved.txt`) and a documented type list are
  pinned by tests, so an accidental break fails CI. A baseline change is a deliberate release
  decision.
- **Target frameworks**: library packages multi-target `net8.0` and `net10.0`; the CLI tool
  targets `net10.0`. Dropping a target framework is treated as a breaking change.
- **Result quality is not a compatibility contract.** A minor release may return a *smaller*
  optimized expression for the same input (better rewriting), but it will always be verified
  equivalent to the input, and a `MinimalProven` status will never be weakened silently.
  Pin an exact version if you depend on the exact textual output — or assert on semantics
  (`IsEquivalent()`, literal count) rather than on a specific string.
- **Explicitly out of contract**: `internal` types, anything undocumented, exact wording of
  exception and diagnostic messages, and the shape of `--verbose` human-readable output.
  The `--format=json` CLI report *is* versioned via its `schemaVersion` field.

## Response expectations

This is an open-source project maintained on a best-effort basis; there is no commercial SLA.
Security reports are prioritized (see [SECURITY.md](SECURITY.md)). Bug reports with a clear
reproduction are handled next, then feature requests weighed against the
[scope](README.md#choosing-a-tool) — the toolkit deliberately stays a propositional Boolean
reasoning library and does not grow into a general rules engine or a full SMT stack.
