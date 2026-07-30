# Support

## Where to go

| Need | Channel |
|---|---|
| How do I do X? / API usage | [Documentation](https://AlexanderV.github.io/LogicalOptimizer/) — a runnable example per capability area, plus the [samples](samples/) |
| Something is wrong / a wrong result | [Bug report](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=bug_report.yml) |
| I need a capability | [Feature request](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=feature_request.yml) |
| Tell us what you use it for — or why you chose something else | [Use-case report](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=use_case_report.yml) |
| Open-ended question | [Discussions](https://github.com/AlexanderV/LogicalOptimizer/discussions) |
| Security vulnerability | **Do not** open an issue — see [SECURITY.md](SECURITY.md) |

The library collects **no telemetry**, so the use-case report is the *only* input to roadmap
decisions. Several items — a compiled evaluator, batch evaluation, reusable BDD/d-DNNF query
objects, additional engines — are deliberately not built until a real workload shows they are
needed, which makes one concrete report decisive rather than merely welcome. A report where
another tool won is just as useful. What is asked, why, and what the reports have changed so far:
[doc/ADOPTION.md](doc/ADOPTION.md).

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

### CLI contract

- **Exit codes are stable** across the 3.x line: `0` success, `1` usage error (bad arguments),
  `2` processing error (for example an invalid expression). Diagnostics always go to stderr, so
  `--format=json` stdout is safe to pipe. Adding a *new* non-zero code for a new failure kind is a
  minor-release change; changing the meaning of `0`, `1` or `2` is a breaking change.
- **The `--format=json` report has a published JSON Schema**
  ([`schema/`](schema/)), golden examples for every outcome, and CI tests that validate both the
  examples and freshly generated output against it. What may change within `schemaVersion: 1`
  (new optional fields, new enum members) and what requires a new version (renaming, removing or
  retyping a field) is spelled out in [`schema/README.md`](schema/README.md). A new schema version
  ships as a new file; the old one stays in the repository.
- **Command-line surface**: removing a flag or verb, or changing what an existing flag does, is a
  breaking change. New flags and new verbs are additive.

### Support window for the previous major

- When a new major ships, the previous major line stays supported for **12 months** from the new
  major's release date, or until the last .NET target framework it supports goes out of
  Microsoft's support — whichever comes first.
- During that window the previous major receives **security fixes and correctness fixes** (a wrong
  result, a false `MinimalProven`, a crash). It does **not** receive new features, new engines, or
  performance work.
- Fixes on the previous major ship as patch releases from a `release/<major>.x` branch. Version
  support status is recorded in [CHANGELOG.md](CHANGELOG.md) at each major release.
- After the window closes the line is end-of-life: issues against it are triaged only to confirm
  whether the current major is affected.

### Deprecation process

Nothing that is public is removed without going through all four steps:

1. **Announce** — the replacement ships first, in a minor release, and the CHANGELOG entry names
   both the deprecated member and what to use instead.
2. **Mark** — the member is annotated `[Obsolete("… Use X instead. Removed in vN.")]` as a
   *warning*, not an error, so an existing build keeps compiling. The `[Obsolete]` attribute shows
   up in the pinned public API baseline, which makes the deprecation itself a reviewed diff.
3. **Keep working** — the deprecated member keeps its documented behaviour for the remainder of the
   major line. A deprecation is never a behaviour change.
4. **Remove** — only in the next major, and only if step 2 shipped at least one minor release
   earlier. The removal is listed in the migration guide for that major with a concrete
   before/after.

Experimental surface is exempt from step 4's timing but not from being labelled: anything
documented as *experimental* (currently the binary BDD/d-DNNF `Save`/`Load`) says so in its XML
doc comment and in the CHANGELOG, and may change in a minor release. If you depend on
experimental surface, pin an exact version and say so in an issue — that is what promotes it to
supported.

## Response expectations

This is an open-source project maintained on a best-effort basis; there is no commercial SLA.
Security reports are prioritized (see [SECURITY.md](SECURITY.md)). Bug reports with a clear
reproduction are handled next, then feature requests weighed against the
[scope](README.md#choosing-a-tool) — the toolkit deliberately stays a propositional Boolean
reasoning library and does not grow into a general rules engine or a full SMT stack.
