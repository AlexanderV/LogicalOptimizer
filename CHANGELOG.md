# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **The release workflow no longer finishes green without a GitHub release.** Its last step
  attached the evidence bundle and the attested `.nupkg`/`.snupkg`/`SHA256SUMS.txt` only when a
  release for the tag already existed, and otherwise printed a note and succeeded. So v3.2.2
  published nine packages, went green, and had no release at all — leaving the bundle's
  `verifying-provenance.md` pointing at assets that were never uploaded anywhere, which is the very
  thing [3.2.2] set out to fix. The workflow now creates the release when it is missing (notes from
  `claim-changes.md`, this version's CHANGELOG section) and then asserts the attached asset count
  instead of assuming the uploads landed. A separate check at the **top** of the job fails the
  release when `CHANGELOG.md` has no section for the version: the notes are only needed at the last
  step, which runs after `dotnet nuget push`, and failing there would leave the packages
  permanently published with the release unmade.

## [4.0.0] - Unreleased

### Changed (breaking distribution; no API change)

- **One library package.** The whole toolkit now ships as the single `LogicalOptimizer`
  package carrying all seven assemblies (Core, Sat, Bdd, Dnnf, Formats, Minimization,
  facade); `LogicalOptimizer.Cli` stays the tool package. The pre-4.0 per-layer IDs
  (`.Core`/`.Sat`/`.Bdd`/`.Dnnf`/`.Formats`/`.Minimization`/`.Full`) continue to be
  published as **deprecated forwarding shells** with a single dependency on
  `LogicalOptimizer`, so upgrading consumers keep compiling without edits. The public API
  surface, namespaces and assemblies are unchanged. Decision and evidence:
  `doc/decisions/package-consolidation-v4.md`.
- **Single `net8.0` target for the libraries.** The seven library assemblies target
  `net8.0` only (previously `net8.0;net10.0` with identical IL): a `net8.0` asset is
  consumed unchanged by newer runtimes, and no target-specific code existed. CLI, tests
  and benchmarks stay on `net10.0`. Decision and measurements:
  `doc/decisions/net8-single-target.md`.

### Fixed

- **External SAT seam hardened at the trust boundary.** `ExternalSatProblem` rejects
  `int.MinValue` literals with the documented `ArgumentOutOfRangeException` (previously an
  `OverflowException` escaped `Math.Abs`), treats an `int.MinValue` in an untrusted model
  as unsatisfied instead of throwing, snapshots clauses/assumptions before validating them
  (mutation of the caller's collections after construction can no longer bypass
  validation), and hands out defensive copies via `Clauses`. `ExternalSatResult.Satisfiable`
  snapshots the model, so verify-then-decode always sees the verified assignment.
  `ExternalSatEquivalenceChecker.Check` observes the cancellation token before building
  the miter and after the adapter returns.

### Infrastructure

- Canonical test entry point `tools/test.ps1` (fast gate by default; `-Performance` /
  `-Exhaustive` / `-Full` run the expensive categories sequentially with long-running-test
  diagnostics); CI test jobs gained timeouts, `--blame-hang` and always-uploaded TRX
  artifacts.
- DocFX pinned via the tool manifest; the docs site covers all seven assemblies and its
  workflow triggers on every package directory.
- GitHub Actions pinned to commit SHAs; Dependabot (NuGet + actions) and a weekly
  `dotnet list package --vulnerable` audit workflow added.

## [3.2.2] - 2026-07-31

### Performance

No behaviour change anywhere below: every result, every cover and every proof status is
identical. Validated after each step by the gate suite (1255) and the full `Exhaustive`
category — all 65534 four-variable functions asserted `MinimalProven` **and** equivalent.

- **The 10-variable optimize path is at least 1.7x faster** (`GuaranteeZoneTenVariables`
  6.75 ms → 2.94–3.94 ms; allocation 1.75 MB → 1.41 MB). A phase breakdown put 91% of that
  benchmark in prime-implicant generation — not, as assumed, in the final soundness guard, which
  costs nothing here because the winning candidate is the interned input node and the guard
  returns on reference equality. The function has exactly five prime implicants but the tabular
  method grinds 26,281 intermediate cubes through nine levels to reach them, and each level
  paired its cubes quadratically. It no longer searches for a merge partner: cubes in a mask
  group share one Mask and Value is always a subset of Mask, so a partner is the cube whose Value
  has exactly ONE more bit set, and the candidates are just the free bits — at most
  `variableCount` probes instead of a whole bucket scan. A second HashSet, which every merge hit
  twice to mark both parents as combined (73,950 merges against 26,281 cubes), became a flag
  array indexed by position in the level.
  The order in which primes are emitted is a behavioural contract — it decides the candidate
  order of the cover search and so which of several equally costed minimum covers is returned —
  so partners are replayed in bucket order, and the resulting prime sequence was dumped for both
  implementations over a dense and a sparse function and diffed: byte-identical, covers included.
- **Exact minimization is 7.7x faster** (`QuineMcCluskeyTenVariables` 18.75 ms → 2.44 ms).
  Profiling put 83% of the time in the covering-table reductions, and the cause was control
  flow rather than arithmetic: column dominance removed a *single* dominated candidate and then
  restarted the whole fixpoint — rescanning essentials, rebuilding both bitmask tables (rows x
  candidates coverage tests apiece) and redoing row dominance — to delete one more column. With a
  few hundred candidates that is a few hundred full rounds. Dominated columns are now collected in
  one pass and dropped together, which is sound because the dominance relation is transitive. The
  essentials pass had already been changed this way; columns had been missed.
- **Espresso-style minimization is 3.0x faster** (`EspressoLite_FortyVariableCover` 14.88 ms →
  4.98 ms). Choosing the most binate variable for the tautology recursion was O(variables x cubes)
  and ran at nearly every node — 22,219 scans across 34,544 nodes, ~44 million bit tests per call.
  Cubes are sparse (a couple of literals out of forty variables), so the tally now walks only the
  set bits.
- **Allocation is down 77% across the gated benchmarks** (18.67 MB → 4.28 MB per operation in
  total, as committed in [`doc/perf-baseline.json`](doc/perf-baseline.json)), chiefly by not
  rebuilding working memory inside the hottest loops: EspressoLite 6,596,502 → 172,163 bytes
  (-97%), Quine–McCluskey 3,426,380 → 259,136 (-92%), the 10-variable
  optimize path 6,291,632 → 1,483,896 (-76%). The largest single item was in truth-table
  evaluation, where `Operands.All/Any` captured the assignment dictionary and so allocated a
  closure, a delegate and an enumerator per n-ary node **per row** — 1024 rows per table, two
  tables per equivalence check.

### Fixed

- **The performance gate no longer fails on measurement noise.** It flagged a regression whenever
  a benchmark exceeded its baseline by 10%, which was reasonable when baselines were megabytes and
  is not now that several are in the hundreds of kilobytes: measured bytes/op for the smaller
  benchmarks is *bimodal* across runs of the same binary — `FormulaFactory_ImportFortyVarCover`
  reports either ~102 KB or ~110 KB, an 8% spread, and the same run composition produces both. A
  regression must now clear an absolute floor as well (32 KB, about four times the observed
  spread); rows over the relative threshold but under the floor are reported as `noise` rather
  than failing the build. Demonstrated both ways: +25% on a small benchmark (22,728 bytes) passes,
  +25% on a large one (367,331 bytes) still fails.
- **`doc/perf-baseline.json` re-recorded** for the improvements above. Without this the gate would
  have stayed green while being blind — a regression of EspressoLite from 172 KB back to 6.5 MB
  would still have been under the old 6.6 MB baseline and reported OK.
- **The post-publish smoke test no longer races nuget.org's indexing.** `verify_nuget.ps1` returns
  as soon as a package is fetchable from the flat-container index, but `dotnet tool install` needs
  the package indexed further, and that lags behind by seconds to minutes. Running the two back to
  back meant the tool install could report `Version 3.2.1 of package logicaloptimizer.cli is not
  found in NuGet feeds` for a package that was on nuget.org and installed fine a minute later —
  which is how the 3.2.1 release run failed *after* a completely successful publish, skipping the
  evidence-bundle steps behind it. [`tools/smoke_install.ps1`](tools/smoke_install.ps1) now retries
  the tool install (10 attempts, 30 s apart) the way the index check already retried, and fails
  only if the package is still not installable after that.
- **"Verify this release yourself" is now actually executable.** nuget.org *repository-signs* every
  package it accepts, appending ~13 KB and therefore changing its SHA-256 — so the provenance
  attestation and `SHA256SUMS.txt`, which describe the bytes the workflow *pushed*, could not be
  verified against a copy downloaded from nuget.org. `gh attestation verify` on such a copy fails
  with a bare `HTTP 404`, which reads like a missing attestation rather than a different digest.
  Three of the six steps in the bundle's `verifying-provenance.md` were broken this way (the third
  additionally needed the `.snupkg`, which nuget.org serves from the symbol server, not next to the
  `.nupkg`), while the document claimed every step could be run against nuget.org downloads. The
  release now attaches the `.nupkg`, `.snupkg` and `SHA256SUMS.txt` to the GitHub release, the
  instructions say which byte stream each check belongs to, and a new step verifies the nuget.org
  copy with `dotnet nuget verify` (which is what proves the repository signature and the owning
  account). The same correction is applied to the README and `RELEASING.md`. Verified by running
  the rewritten steps against the v3.2.1 release as an outside consumer: attestation verifies, 17
  of 17 checksums match, and the contract audit passes 169 checks.

### Changed

- **Allocation baseline re-recorded for the mid-range zone.**
  `OptimizationBenchmarks.MidRangeFourteenVariables` is the one gated benchmark that grew:
  583,472 → 612,748 bytes/op (+5.0%) as finally recorded. This is the cost of a feature, not a
  leak: the old baseline predates the **final soundness guard**, and above 12 variables that guard
  proves the returned result equivalent to the input with a SAT miter — which is exactly what a
  14-variable benchmark exercises. The attribution is visible in the numbers:
  `GuaranteeZoneTenVariables` (10 variables, where the guard uses the cheap truth-table path) did
  not move for this reason, and the untouched benchmarks are flat. Verifying every result is a
  headline guarantee, so the allocation is accepted and
  [`doc/perf-baseline.json`](doc/perf-baseline.json) re-recorded rather than the threshold widened.
  What set this off was an intermediate recording of 646,881 bytes/op (+10.9%) that tripped the
  gate; later runs of the same binary settled at 612,748, which is itself part of why the gate
  gained an absolute floor above.

## [3.2.1] - 2026-07-30

Packaging fix. **This is the release to use; [3.2.0] never fully published** — see the note under
it. No source change to any library: the assemblies, the public API and the runtime behaviour are
exactly those described under [3.2.0], which is the section to read for what is new.

### Fixed

- **The meta-package no longer packs an empty symbol package.** `LogicalOptimizer.Full` ships no
  code, so the `.snupkg` it produced contained no `.pdb`, and nuget.org rejects such a symbol
  package outright (`400 The package does not contain any symbol (.pdb) files.`). Because
  `dotnet nuget push` walks its glob in order and aborts on the first failure, that rejection
  stopped the 3.2.0 publish part-way through. The cause was a side effect of centralizing package
  metadata: `LogicalOptimizer.Full.csproj` previously carried its own metadata and never set
  `IncludeSymbols`, so no `.snupkg` existed for it; moving `IncludeSymbols` /
  `SymbolPackageFormat` into `Directory.Build.props` applied them to every project. The
  meta-package now opts out, rather than the shared setting being weakened for the packages that
  legitimately ship symbols.
- **The pre-publish gate now catches this class of failure.**
  [`tools/verify_package_contract.ps1`](tools/verify_package_contract.ps1) ran its symbols check
  only for packages of kind `library`, so it never opened the meta-package's `.snupkg`: it
  asserted that libraries *have* `.pdb`s, and nothing asserted that a produced `.snupkg` is not
  empty. It now checks every `.snupkg` it finds, whatever the package kind — the audit is 169
  checks across the nine packages, and copying a `.nupkg` over
  `LogicalOptimizer.Full.<version>.snupkg` makes it fail with `symbols-package-not-empty`.

## [3.2.0] - 2026-07-30

> **Partially published; superseded by [3.2.1].** The publish step failed part-way through, so
> only `LogicalOptimizer.Minimization`, `LogicalOptimizer.Dnnf` and `LogicalOptimizer.Full`
> reached nuget.org at 3.2.0 — and those three depend on `LogicalOptimizer.Core` /
> `LogicalOptimizer.Sat` 3.2.0, which were never pushed, so all three are uninstallable. They have
> been unlisted. **Use 3.2.1**, which is the same source with the packaging fix. Everything
> described below shipped in 3.2.1.

Turning shipped capabilities into a versioned, independently verifiable product contract. The
public API grows additively — `OptimizationTrace` / `OptimizationTraceEntry` /
`OptimizationTraceCategory`, `OptimizationOptions.IncludeTrace`, `OptimizationResult.Trace`,
`FormulaFactory.TryParse` with `ParseDiagnostic` / `ParseErrorCode`, and `FormulaParseException`
(an `ArgumentException`, so the throwing parser API stays source- and binary-compatible). No
existing member changed shape and no runtime behaviour changed, so an upgrade from 3.1.x needs no
code change.

### Added

- **Published JSON Schema for the CLI report.** The `--format=json` document is now a contract
  rather than a convention: [`schema/cli-report-v1.schema.json`](schema/cli-report-v1.schema.json)
  (Draft 2020-12, served from the docs site at the `$id` it declares), eight golden example
  reports in [`schema/examples/`](schema/examples) covering success, `BudgetExceeded` minimality, a
  `TooLarge` normal form, the optional `advanced` field, a CSV source, `--trace`, a structured parse
  error and a bare processing error, and [`schema/README.md`](schema/README.md) spelling out what may change
  within a version and what requires a new one. `CliReportSchemaTests` validates the committed
  examples *and* freshly generated output against the schema, checks the schema's enums are exactly
  the CLR enums (`MinimizationStatus`, `ComputationStatus`, `OptimizationTraceCategory`,
  `ParseErrorCode`), and proves the schema is closed — so a field cannot be added, renamed, retyped
  or dropped without a reviewed schema diff.
- **Package contract audit.** [`tools/verify_package_contract.ps1`](tools/verify_package_contract.ps1)
  opens every `.nupkg` as a zip and asserts 161 checks across the nine packages: a
  package-specific README that is actually present in the package, a substantial and **distinct**
  description, tags, project/repository URLs, an Apache-2.0 SPDX expression, a `.snupkg` carrying a
  `.pdb` per framework, the contracted target frameworks in `lib/` (`tools/` +
  `DotnetToolSettings.xml` + `packageType=DotnetTool` for the CLI), no third-party runtime
  dependency anywhere, and that the meta-package transitively reaches every library package. It
  writes a machine-readable report — including what it does *not* prove — runs on every pull
  request, and **gates the release before `nuget push`**, since a published package cannot be
  withdrawn.
- **Native AOT and modular-install smoke tests from the published packages.**
  `tools/smoke_install.ps1` now installs **every** modular package into its own project and asserts
  the assemblies it should bring load with public types (for `LogicalOptimizer.Full`, all seven
  library assemblies through one reference), and with `-IncludeAot` compiles the consumer program
  with `PublishAot=true` and runs the native binary. `aot.yml` only proves AOT compatibility through
  an in-repo project reference, which cannot catch a packaging-level break.
- **Release evidence bundle.** [`tools/build_evidence_bundle.ps1`](tools/build_evidence_bundle.ps1)
  collects the package contract audit, the nuget.org index check, the AOT smoke result, test counts
  parsed from the release `.trx`, `SHA256SUMS.txt`, this version's changelog section as
  `claim-changes.md`, and `verifying-provenance.md` (how to re-verify the attestation, checksums,
  package contract, install/AOT smoke and CLI schema yourself) into one directory with an `INDEX.md`
  and a `manifest.json` carrying each file's SHA-256. Missing inputs are recorded as `absent` and
  `-RequireAll` fails the release, so a bundle cannot look complete when it is not. Attached to the
  tag's GitHub release when one exists, and always uploaded as a run artifact.
- **Claims glossary with mechanical enforcement.** [`doc/CLAIMS.md`](doc/CLAIMS.md) defines every
  public claim — `verified`, `minimal`, `dependency-free`, `Native AOT support`, `benchmark result`,
  `no silent fallbacks` — with the approved wording, the executable test / CI check / versioned
  artifact that backs it, and the limits it deliberately does not assert.
  [`ClaimsConsistencyTests`](LogicalOptimizer.Tests/Techniques/ClaimsConsistencyTests.cs) fails the
  build when a public-facing document uses a banned phrasing, and when a cited piece of evidence
  stops resolving — the file *and* the named test method are both checked, so a claim cannot outlive
  its backing. A `<!-- claim-ok: reason -->` escape covers legitimate uses such as stating a
  limitation.
- **Exhaustive 4-variable minimality proof.**
  `TruthTableMinimizerTests.OptimizeExpression_AllFourVariableFunctions_MinimalProven` (`Exhaustive`
  category) checks that all 65534 non-constant 4-variable functions report `MinimalProven` **and**
  stay equivalent to the input. The README already claimed this for 3- and 4-variable functions
  while only the 3-variable sweep existed, so the claim is now backed rather than reworded.
- **Adoption feedback channel.** A dedicated
  [use-case report](.github/ISSUE_TEMPLATE/use_case_report.yml) issue form asks for formula kind and
  size, operation frequency, which guarantees the caller actually depends on, deployment
  constraints, why LogicalOptimizer was chosen *or rejected*, capability gaps separately from
  documentation gaps, and citation permission. [`doc/ADOPTION.md`](doc/ADOPTION.md) records how each
  field maps to a specific decision, and that aggregation is manual and public — the library still
  collects **no telemetry**, so this is the only roadmap input.
- **Compatibility and lifecycle policy** in [SUPPORT.md](SUPPORT.md): CLI exit-code stability, the
  JSON schema contract, a 12-month support window for the previous major with its fix scope, and a
  four-step deprecation process (announce → `[Obsolete]` warning → unchanged behaviour for the rest
  of the major → removal only in the next major).

- **Comparison reproduction is now checked, not trusted.**
  [`tools/verify_comparison_reproduction.ps1`](tools/verify_comparison_reproduction.ps1) asserts that
  a comparison run used the committed corpus (by SHA-256, not by filename), recorded its
  environment, produced an `equivalent` verdict on every optimization/minimization row, an `unsat`
  verdict on every equivalence miter, agreeing model counts from the two independent counting
  engines, and **enough populated competitor columns** — every adapter self-skips by design, so the
  container previously exited 0 even when every competitor cell stayed `pending`, making "it
  reproduced" unfalsifiable. With `-CompareWith` it also enforces the determinism claim the
  methodology states: every non-timing field must match the committed run. Timing is never asserted.
  A new `reproduce-from-scratch` job in [`comparison.yml`](.github/workflows/comparison.yml) runs the
  documented sequence verbatim from a clean checkout, so an outside reproducer meets working scripts
  rather than being the one who discovers they rotted.

### Fixed

- **The equivalence guarantee did not cover the returned result.** `RewriteEngine`'s soundness guard
  only compares the rewrite phase's output to its own input. Everything after it — the exact
  Quine–McCluskey candidate, the SAT prime cover, the subcircuit library, AIG rewriting — imports an
  expression built by a *different* engine, and the winner of the cost comparison was never compared
  to the parsed input at all. The documented promise ("every optimization is verified equivalent to
  the input before it is returned") therefore described a stronger guarantee than the code provided;
  `IsEquivalent()` only checked afterwards, and only if the caller asked.
  A **final guard** now runs immediately before `OptimizationResult` is built: truth table up to 12
  variables (reusing the exact path's ON-set, so no second table is built), SAT miter above. Failing
  to *prove* equivalence — refuted, or the SAT budget exhausted — rolls the result back to the input,
  drops the minimality status to `Heuristic`, and re-derives the normal forms from the input, so a
  returned document can never mix a rolled-back expression with forms of a refuted candidate.
- **Claim-critical exhaustive evidence never ran automatically.** README and `doc/CLAIMS.md` cite the
  exhaustive 4-variable sweeps as what backs the `verified` and `MinimalProven` claims, but both are
  `Exhaustive`-category and every automated filter excluded them — nothing re-proved them for the
  commit being published. They now also carry a `ReleaseEvidence` category, run as a gate **before
  `dotnet pack`** (~2 minutes for all 65534 functions, twice), and land in the evidence bundle as
  `exhaustive-evidence.json`; `-RequireAll` fails the release without it. The full `Exhaustive`
  category runs nightly ([`exhaustive.yml`](.github/workflows/exhaustive.yml)).
- **The published JSON Schema accepted a self-contradictory report.** The two outcomes were expressed
  as `anyOf`, which validates a document carrying *both* `optimized` and `error` — so schema
  validation alone could not tell a consumer which outcome occurred. Now `oneOf` with explicit `not`
  clauses: a success report requires `optimized`/`equivalent`/`minimality` and forbids `error`; an
  error report forbids every result-only field. Negative tests cover all three cases. This is a
  schema defect fix, not a contract change — the CLI has never emitted such a document, so nothing
  that was valid before became invalid.
- **A JSON report for a CSV truth table could not name its own input.** The schema defines `input`
  as "the argument exactly as received", but the CSV path overwrote the received argument with the
  sum-of-products it derived from the table, and the report wrote *that* into `input` — so a
  consumer could not tell which CSV, or which `*.csv` file, a report came from, and the error path
  (which always wrote the received argument) disagreed with the success path about what the field
  meant. Single-output CSV plus `--format=json` is a supported combination — only `--outputs` is
  rejected with JSON — so this was a real gap, not an invalid invocation. `input` is now the
  received argument on both paths; two additive fields carry the rest: **`sourceFormat`**
  (`expression` | `csv`, present in every report) and **`analyzedExpression`** (the derived
  expression, present only when it differs from `input`). Every verdict in the report is about the
  analyzed expression, which the schema now states field by field. New
  [`CliJsonInputContractTests`](LogicalOptimizer.Tests/Cli/CliJsonInputContractTests.cs) drives
  `Program.Main` for inline CSV, a `*.csv` file, auto-detected CSV, malformed CSV and a plain
  expression, and checks stdout carries exactly one schema-valid document with the CSV progress
  messages kept on stderr — the seam the writer-level tests structurally could not reach.
- **Documented CLI transcripts had drifted from the CLI.** `docs-site/articles/cli-usage.md` claims
  "All outputs shown are verified against the built CLI", yet its default-output block (and
  `introduction.md`'s) predated the `Equivalent` / `Minimality` / `Cost` lines the formatter now
  always prints. Both are updated, and `DocumentedCliOutputTests` makes the promise executable: every
  documented transcript is compared line-by-line against output from the same formatter the CLI uses,
  with a guard test so a reformatted block cannot silently turn the check into a no-op.
- **The release evidence bundle silently omitted benchmark provenance.**
  `build_evidence_bundle.ps1` only recorded a `benchmarks` item when `-BenchmarkManifest` was
  passed, and `release.yml` never passed it — so every bundle would have shipped without the
  benchmark manifest and raw results that the bundle's own contract lists, and without so much as an
  `absent` marker. The item is now always recorded, and the release passes `doc/comparison`.

### Changed

- **Removed absolute competitive claims** from the documentation site: "the most complete managed
  .NET Boolean-optimization toolkit" and "best-in-niche" are gone, replaced by a concrete capability
  list plus a link to the pinned comparison and to *Choosing a tool*. No external comparative
  evidence exists for a superlative, and the test above now prevents one from reappearing.
- **Qualified the dependency claim everywhere.** "zero production dependencies" / "zero runtime
  dependencies" become "no third-party runtime dependency" in README, `SECURITY.md`, the facade
  package README and three documentation pages — a shipped LogicalOptimizer package does reference
  other LogicalOptimizer packages, so the unqualified form was not true.
- **Narrowed the deterministic-build wording** in `RELEASING.md`: `ContinuousIntegrationBuild=true`
  normalizes source paths, which makes the build deterministic *for a fixed SDK, OS and dependency
  graph* — it is not a full reproducible-build guarantee across environments, and is no longer
  described as one. What each release does verify (checksums, provenance attestation) is stated
  instead.
- **`git diff --check` no longer flags Markdown hard line breaks.** Two trailing spaces are
  Markdown's line-break syntax and the README/docs hero lines depend on it; `*.md
  whitespace=-trailing-space` in `.gitattributes` keeps the check meaningful everywhere else instead
  of drowning a real defect in expected warnings.

### Documentation

- **The standard-format CLI verbs are documented.** `solve` (DIMACS CNF), `maxsat` (WCNF),
  `solve-pb` (OPB) and `count --engine dnnf` (exact `#SAT`) existed and were listed in
  `--help`, but appeared in no README and on no documentation-site page — a whole capability
  area was reachable only by running `--help`. They now have a section in
  [`docs-site/articles/cli-usage.md`](docs-site/articles/cli-usage.md) with the input file, the
  verified output and the error/exit-code behaviour of each, a short section in the README and
  in the CLI package README, a pointer from the `LogicalOptimizer.Formats` package README, and
  a parse-level pin — `DocExamplesTests.Cli_RecognizesEveryDocumentedStandardFormatVerb` — so a
  renamed verb fails the build the way a renamed flag already does.
- **Published figures re-measured against the current suite.** Test count `1175 → 1254` and audit
  date `2026-07-29 → 2026-07-30` in the README; the whole per-area table in
  [`docs-site/articles/testing-overview.md`](docs-site/articles/testing-overview.md) (it still
  reported the pre-reorganization 1 152-case layout). Coverage was published as "~89% line" with
  no statement of *what* was measured; it is now the measured 92.7% line / 84.6% branch on the
  `LogicalOptimizer` facade assembly — the module the CI gate actually covers and applies its 80%
  floor to.
- **Corrected an exporter example that had gone stale.** `doc/ADVANCED_FEATURES.md` showed
  `ToLatex("a & b | c")` returning `a \land b \lor c`; since exporters parse through
  `FormulaFactory` the real output is canonically ordered, `c \lor a \land b`. The same file's
  feature list predated the SAT / BDD / d-DNNF / AIG engines and claimed "1150+ tests"; it is now
  scoped to the tooling it actually documents, with the invented per-expression timings replaced
  by a pointer to the recorded benchmark artifacts.
- **The documentation-site map is complete again.** `docs-site/index.md` omitted four articles
  that are in the table of contents and shipped: diagnostic trace, benchmarks & comparison,
  choosing a tool, and case studies.
- **The facade's package scope is stated correctly.** `docs-site/articles/introduction.md` said the
  `LogicalOptimizer` facade installs "everything"; it installs Core + Sat + Bdd + Minimization, and
  `.Dnnf` / `.Formats` have to be added alongside it (or `LogicalOptimizer.Full` used instead).
- **`[3.1.1]` added below** — the tag was published but the release had no changelog section.
- **Historical planning documents removed from the repository root.** Fourteen point-in-time
  files — code-review reports, refactoring and positioning plans, four roadmaps, `TODO.md` and
  three superseded library comparisons — were deleted. They recorded intermediate states that the
  shipped documentation now covers: measured comparison results live in [`doc/comparison/`](doc/comparison)
  and [Choosing a Tool](docs-site/articles/choosing-a-tool.md), the claim inventory in
  [`doc/CLAIMS.md`](doc/CLAIMS.md), release history here, and the projected-model-counting design
  in [`doc/decisions/`](doc/decisions) and [`doc/spikes/`](doc/spikes). The handful of prose
  citations pointing into them (in `doc/CLAIMS.md`, `doc/ADOPTION.md`, the comparison workflow,
  the evidence-bundle and reproduction-verifier scripts, the spike and decision records, and
  `ClaimsConsistencyTests`) were rewritten to state the substance inline, so no claim lost its
  backing — `Claims_EveryEvidenceReference_StillResolves` still passes. `MIGRATION-v2.md` is
  kept: it is live upgrade documentation, not a plan.

## [3.1.1] - 2026-07-29

Whitespace-only patch release. No public API change, no behaviour change, no new or removed
functionality — recorded here because the tag was published and every released version belongs in
this file.

### Fixed

- Whitespace formatting in `LogicalOptimizer.Dnnf/DnnfCircuit.cs`,
  `LogicalOptimizer.Sat/CardinalityEncoder.cs`, `LogicalOptimizer.Sat/MaxSatSolver.cs` and three
  test files, so `dotnet format --verify-no-changes` passes in CI.

## [3.1.0] - 2026-07-29

Deployment, credibility and interoperability. All changes are additive; the public API
baseline diff is additive-only.

### Added

- **Controlled cross-library benchmark, executed (P0.2).** The competitor side of the comparison
  now runs in one reproducible, **version-pinned** Linux container
  ([`tools/comparison/Dockerfile`](tools/comparison/Dockerfile)) bundling the .NET harness with
  SymPy, PyEDA, CaDiCaL, Kissat, Z3, d4 and a new self-contained LogicNG BDD adapter
  ([`tools/comparison/logicng/`](tools/comparison/logicng)), driven from the single committed
  corpus; real competitor numbers are merged into a human-readable
  [`doc/comparison/merged.md`](doc/comparison/merged.md) (TL;DR, per-table interpretation, and
  agreement/size markers). The comparison harness gains `comparison-suite --emit-function-dimacs`,
  which writes the **count-preserving** per-function Tseitin CNF (auxiliary variables functionally
  determined, so a #SAT counter's result equals the exact `modelCount`). Headline cross-checks:
  OUR `modelCount` = d4 = LogicNG on all 17 functions; every equivalence miter is UNSAT under
  CaDiCaL, Kissat **and** Z3; OUR two-level SOP matches SymPy/PyEDA literal counts where they
  finish. Running the previously documentation-only adapters for the first time uncovered and
  fixed latent bugs in them (a `printf` separator that aborted under `set -e`, d4v2's `-mc`
  counting flag, a merge that keyed SymPy/PyEDA rows by the wrong column, and an unhashable
  `CheckSatResult` dict key in the Z3 adapter). Dev-tooling only — no public API or runtime change.
- **Core-guided MaxSAT (P2.2).** `MaxSatSolver` gains an opt-in core-guided search alongside the
  existing linear one, selected through a new overload
  `Solve(MaxSatAlgorithm algorithm, int maxConflictsPerCall, CancellationToken)`. **The
  parameterless `Solve(int, CancellationToken)` is unchanged** — it still runs the linear search
  byte-for-byte, so no existing caller's behaviour changes; core-guided is reached only through the
  new overload. The new public surface is additive:
  `enum MaxSatAlgorithm { Auto, Linear, CoreGuided }` plus two new read-only bounds on
  `MaxSatResult` (`long LowerBound`, `long UpperBound`) that bracket the optimum. The core-guided
  path implements an MSU3-style lower-bound search: each soft clause gets a blocking variable, the
  solver iteratively extracts UNSAT cores under soft-selector assumptions and relaxes only the cores
  with a cardinality bound (unweighted — textbook MSU3) or a pseudo-Boolean bound raised in unit
  steps (a sound weighted MSU3 variant), proving the optimum when the formula becomes SAT. The
  three-valued completion status is explicit: a proven `Optimal`, a sound incumbent under a spent
  budget/cancellation (`Unknown`, with `LowerBound < UpperBound`), and `HardClausesUnsatisfiable` —
  and an incumbent is **never** reported as optimal (hard-UNSAT is checked up front, distinct from
  budget exhaustion). `Auto` currently routes to `Linear`. Both algorithms observe the conflict
  budget and `CancellationToken`. Validated against brute-force enumeration and Z3 `Optimize` on
  hundreds of seeded random weighted partial instances (unweighted and weighted), with pigeonhole
  regression cases where the linear and core-guided searches take visibly different paths yet reach
  the same proven optimum, plus explicit hard-UNSAT and tiny-budget-incumbent tests.
- **Encoding portfolio (P2.1).** `CardinalityEncoder` and `PseudoBooleanEncoder` gain
  encoding-selecting overloads that pick from a portfolio of semantically equivalent CNF
  encodings and report the size they introduce. Cardinality adds `Pairwise` (binomial),
  `Product` (Chen 2010, at-most-one) and `Totalizer` (Bailleux & Boufkhad 2003) alongside the
  existing `SequentialCounter`; pseudo-Boolean adds `BinaryMerge` (a binary adder network
  compared against the bound) and `GeneralizedTotalizer` (Joshi et al. 2015) alongside the
  existing `DynamicProgramming` decision-diagram encoding. The new public surface is additive:
  `enum CardinalityEncoding { Auto, Pairwise, SequentialCounter, Product, Totalizer }`,
  `enum PseudoBooleanEncoding { Auto, DynamicProgramming, BinaryMerge, GeneralizedTotalizer }`,
  a `readonly struct EncodingStats { int Clauses; int AuxiliaryVariables; int Cost }`, and one
  encoding-taking overload per `AtMostK`/`AtLeastK`/`ExactlyK` and `AtMost`/`AtLeast`. **The
  parameterless methods are unchanged** — they keep their byte-identical sequential-counter /
  dynamic-programming output, so no existing caller's CNF changes; the portfolio and `Auto` are
  opt-in. `Auto` is deterministic: it measures each applicable encoding and keeps the smallest by
  `Cost` (clauses + auxiliary variables), with the current default always among the candidates,
  so it is **never larger than the default** (its choice may change between minor releases, but
  only with a CHANGELOG note and never worse than the default on the fixed calibration corpus in
  `tools/encoding_corpus.txt`). Every encoding is verified assignment-by-assignment (313,000+
  exhaustive satisfiability checks over all small-n cardinality and random weighted PB shapes);
  characterization tests pin the default output and each encoding's clause / auxiliary-variable
  counts, and the calibration-corpus gate confirms `Auto ≤ default` on every shape. The
  calibration corpus is now a **frozen baseline**: a checksum test pins the exact set of shapes,
  so any edit, addition, removal or reordering must be a deliberate, reviewed change (the pinned
  hash moves in the same change) rather than silent re-tuning. The OPB feasibility path
  (`PseudoBooleanProblem`) continues to route through the unchanged default.
- **Circuit serialization (P2.3) — EXPERIMENTAL until v4.** `BinaryDecisionDiagram` and
  `DnnfCircuit` each gain `Save(Stream)` and
  `static Load(Stream, ResourceBudget?, CancellationToken)` over a compact, hand-written binary
  format (magic · format version · engine byte · variable table · node table · root · CRC-32),
  letting a service compile a circuit once and reuse it across restarts. The format is
  **experimental**: it may change before v4 and carries no cross-version compatibility guarantee
  other than the version gate, which makes a build **refuse** (never misread) a blob written by a
  newer build. Output is **deterministic** (the same circuit always yields identical bytes) and
  **little-endian** (documented, via `BinaryPrimitives`); the engine byte makes loading a d-DNNF
  blob as a BDD — or vice versa — a typed error. `Load` is fully validated: it verifies the CRC-32
  (which catches corruption but does **not** replace structure checks), and enforces semantic
  validity — variable indices in range, a genuine variable-order permutation (BDD), children
  strictly before their parent (acyclic/topological) and at a deeper level (reduced-ordered BDD),
  and a valid root and terminals. There is **no reflection or object deserialization**, and the
  read is **budgeted**: a hostile size header is checked against `ResourceBudget.BddNodeLimit` and
  the actual stream, so it aborts with `NodeBudgetExceededException` (or a truncation error) rather
  than pre-allocating. A loaded BDD is a valid hash-consed manager whose queries answer
  identically. Any malformed input is the new typed `CircuitSerializationException`. Validated by a
  differential Save/Load round-trip (model count, variables, evaluation) on random formulas,
  deterministic-bytes and committed golden-blob drift tests, forward-version and wrong-engine
  rejection, a 24000+-iteration corrupted-input fuzz (truncations and bit-flips — only a clean load
  or the typed/budget exception, never a hang or unbounded allocation), and budgeted-load tests.
- **Projected model counting (P1.4).** `FormulaAnalysis.CountProjectedModels(formula,
  projectedVariables, ResourceBudget?, CancellationToken)` counts the number of DISTINCT
  assignments over a chosen subset of variables that extend to some model of the formula —
  the remaining variables are existentially forgotten. It returns
  `ProjectedModelCountResult { BigInteger? Count; ProjectedCountStatus Status }` with `Count`
  non-null iff `Status == Exact`. The engine is SAT blocking enumeration (one incremental
  solve per distinct projected model, blocking over the projection literals only), which is
  sound by construction against the overcount trap — different full models that agree on the
  projection are counted once. Scope semantics: projection names not in the formula are free
  (each multiplies the count by 2, never an error); an empty projection is `1` for a
  satisfiable formula and `0` for an unsatisfiable one; projecting all of the formula's
  variables equals `CountModels()`. The shared `ResourceBudget` maps to an enumerated-model
  bound (`CoverStepLimit`) and a per-solve conflict bound (`SatConflictLimit`); exhausting
  either yields `BudgetExhausted` with `Count == null` — a partial run is never reported as
  exact — and cancellation surfaces as `OperationCanceledException`. Validated against an
  independent brute-force projected-and-deduplicated oracle exhaustively for all functions up
  to 4 variables and every projection subset, plus randomized (≤10-variable) and edge cases.
- **d-DNNF marginal probabilities and model sampling.** `DnnfCircuit` gains
  `MarginalProbability(variable, weights)` — the weighted marginal
  `WeightedModelCount(weights, {variable = true}) / WeightedModelCount(weights)`, which for
  uniform weights is the fraction of models with the variable true — and top-down weighted
  samplers `SampleModel(Random, weights = null)` and
  `SampleModels(count, seed, weights = null, CancellationToken)`. Unweighted sampling
  (`weights == null`) is uniform over satisfying models; weighted sampling draws each model
  proportional to its weighted-count share; every returned model assigns exactly `Variables`.
  `SampleModels(count, seed, …)` is fully deterministic for a seed (no cryptographic claim).
  A zero total weight (an unsatisfiable formula or all-zero weights) is an explicit
  `InvalidOperationException` — never a fabricated model — an unknown marginal variable or a
  negative/NaN/infinite weight is an `ArgumentException`. Validated against exhaustive
  weighted enumeration for marginals and wide, fixed-seed statistical bands for sampling.
- **d-DNNF conditioning and evidence queries.** `DnnfCircuit` gains
  `Condition(IReadOnlyDictionary<string, bool>, CancellationToken)`, returning a NEW circuit
  with the named variables pinned (the source is never mutated and the variable universe is
  unchanged), plus single-pass `CountModels(evidence)` and
  `WeightedModelCount(weights, evidence)` overloads that count/weight only the models
  consistent with the evidence. Empty evidence reproduces the unconditioned result; an
  unknown variable name is an `ArgumentException`. Validated against the BDD oracle and
  brute-force enumeration across many seeds.
- **`LogicalOptimizer.Formats` package — DIMACS / WCNF / OPB import.** `DimacsParser`,
  `WcnfParser` and `OpbParser` (`Parse(TextReader, ResourceBudget?, CancellationToken)`)
  stream standard SAT / MaxSAT / pseudo-Boolean datasets into `CnfProblem` /
  `WeightedCnfProblem` / `PseudoBooleanProblem`, which hand off to the existing
  `SatSolver` / `MaxSatSolver` / `CardinalityEncoder` engines (no new solver). Precise
  line/column errors (`FormatParseException`), a streaming budget-aware tokenizer bounded
  by the new `ResourceBudget.ParseTokenLimit`, and round-trip `Write` writers. New CLI
  verbs `solve`, `maxsat`, `solve-pb` and `count --engine dnnf`.
- **`LogicalOptimizer.Full` meta-package.** A code-less bundle that depends on the facade,
  `.Dnnf` and `.Formats` so `dotnet add package LogicalOptimizer.Full` installs the whole
  managed toolkit; the facade gains no new dependency and modular users keep referencing
  individual layers.
- **Native AOT / trimming certification.** The seven library packages enable
  `IsAotCompatible` / `IsTrimmable` and the trim/AOT/single-file analyzers, so every build
  (`TreatWarningsAsErrors`) guards the AOT contract; the reflection-free code is
  analyzer-clean. A `LogicalOptimizer.AotSmoke` harness and the `aot.yml` workflow publish
  and run a Native AOT binary for `win-x64` and `linux-x64`.
- **Allocation-based performance regression gate.** A committed `doc/perf-baseline.json`,
  `tools/compare_benchmarks.ps1` (fails when a benchmark exceeds baseline allocations by a
  threshold; wall-clock is informational only) and a `perf.yml` workflow (dispatch +
  weekly, off the PR path).
- **Reproducible cross-library comparison.** A `comparison-suite` harness emits committed
  OUR-side artifacts (JSON + Markdown + environment manifest) for symbolic optimization,
  two-level minimization, SAT (equivalence miter with DRAT proof) and BDD/d-DNNF, each row
  independently equivalence-verified; competitor columns stay `pending` with reproduce
  commands (`doc/comparison/`, `doc/COMPARISON_METHODOLOGY.md`).
- **Projected model counting design spike** (`doc/spikes/projected-model-counting.md`):
  validated SAT-blocking-enumeration and BDD-existential-abstraction prototypes with an
  exhaustive ≤4-variable proof; recommends the MVP and status contract. No public API yet.

### Changed

- **NuGet post-publish verification window widened** from ~5 min to ~30 min and extended to
  all nine packages, so real indexing lag no longer reds a successful release while a
  genuine publish failure is still caught.

### Performance

- **Espresso-lite two-level minimizer** is ~3.4× faster with ~8× fewer allocations on the
  40-variable cover (a shared "eliminated" variable bitmask replaces per-recursion cube
  cloning in the tautology check; cofactor-by-cube becomes a seed mask + conflict filter).
- **Exact Quine–McCluskey** is ~2.3× faster with ~3× fewer allocations on a dense
  10-variable function (popcount-bucketed adjacent-pair prime generation, batched
  essential-prime extraction, and bitmask covering-table dominance). Output is
  byte-identical: the prime set and `(literals, terms)` cover cost are unchanged.

## [3.0.0] - 2026-07-29

### Fixed (contract honesty — the documented guarantees now match the implementation)

- **A SAT `Unknown` soundness-guard verdict is no longer treated as success.** Beyond the
  12-variable truth-table range a rewrite is accepted only when the SAT miter *proves*
  equivalence; a budget-exhausted `Unknown` now rolls back to the input (recorded as
  `SoundnessRollback`) instead of shipping an unverified result. "Every optimization is
  verified equivalent" is now literally true.
- **Quine–McCluskey work-budget exhaustion is reported as `MinimizationStatus.BudgetExceeded`,
  not silently masked as `Heuristic`.** The exact path now throws a dedicated
  `ComputationBudgetExceededException` (deriving from `InvalidOperationException` for
  compatibility) and the facade catches only that type — a genuine `InvalidOperationException`
  from a real defect is no longer swallowed.
- **Expected engine fallbacks are keyed on dedicated exception types instead of the broad
  `InvalidOperationException`.** BDD/d-DNNF node-budget exhaustion now throws
  `NodeBudgetExceededException` and normal-form distribution blow-ups throw
  `NormalFormTooLargeException`; every fallback catch (BDD best-order/sifting, the BDD
  equivalence checker, the facade's CNF/DNF `TooLarge` handling, the DIMACS exporter's Tseitin
  fallback, and the CSV minimizer's budget fallback) now catches only the specific type, so an
  unrelated invariant violation surfaces instead of being silently absorbed.
- **The ≤10-variable guarantee zone runs a genuinely unbounded exact cover search**
  (`GUARANTEE_COVER_STEP_LIMIT` raised to `int.MaxValue`), so `MinimalProven` holds for every
  function in that zone as documented, rather than degrading to `BudgetExceeded` at a
  2,000,000-step cap.
- **`OptimizationResult.IsEquivalent()` works across the whole facade range.** It now routes
  through the scalable `EquivalenceChecker` (truth table ≤12 vars, SAT-miter beyond) instead of
  throwing past the 20-variable truth-table limit; it returns true only when equivalence is
  positively proven.
- **The documented 10-second maximum processing time is now a global, cooperative deadline.**
  A single linked `CancellationToken` bounds the whole call (rewrite, truth-table build, QM,
  cover search, SAT, normal forms, AIG…) and surfaces as `TimeoutException`. The token reaches
  every optimizer sub-pass (including the SAT-cover factoring candidate) and is checked at the
  boundaries of the synchronous phases that do not poll it internally (normal-form conversion,
  Tseitin, pattern recognition, truth-table generation). README documents the cooperative
  semantics rather than promising hard-real-time preemption.
- **The equivalent-CNF (minimal POS) uses the same cover-search budget as the SOP** — unbounded
  in the ≤10-variable guarantee zone — so the "provably minimal POS" claim holds wherever the
  SOP minimum is proven, instead of silently running the POS search under the smaller default
  budget.
- **The POS proof status is no longer discarded by the facade.** The equivalent-CNF minimality
  is reported through the new `OptimizationResult.CnfMinimizationStatus` instead of being folded
  into (or hidden behind) the SOP-scoped `MinimizationStatus`, so a POS cover search that hits
  its budget at 11–12 variables is visible to the caller rather than implicitly claimed minimal.
- **`OptimizationQualityAnalyzer.IsOptimal` is now a proven property**, true only for
  `MinimizationStatus.MinimalProven` (two-level cost model), not a score ≥ 85. The heuristic
  0–100 rating remains available as `OptimalityScore` and is labelled as such in the report.

### Added

- `TruthTableMinimizer.MinimalPosWithStatus` — POS minimization that also reports whether the
  cover search proved minimality (mirrors `MinimalSopWithStatus`); the POS path is no longer
  silently unproven.
- `OptimizationMetrics.AllocatedBytes` (thread allocation measurement, captured as late as
  possible so it covers result-artifact construction too) and a populated `OptimizationSteps`
  convergence trace (node count per fixpoint iteration); both surfaced in `GenerateQualityReport`
  and covered by direct tests. Backs the README "convergence analysis" and "memory usage" claims
  with real data.
- `OptimizationResult.CheckEquivalence()` — three-valued self-check returning
  `EquivalenceCheckResult`, so callers can tell a refutation (`false`) apart from a
  budget-exhausted `Unknown` (`null`); `IsEquivalent()` remains as the boolean convenience.
- `ComputationBudgetExceededException`, `NodeBudgetExceededException` and
  `NormalFormTooLargeException` public types (all derive from `InvalidOperationException`), so
  budget/size fallbacks are distinguishable from genuine invariant violations.

### Changed

- **BREAKING: AIG DAG-aware cut rewriting is now ENABLED BY DEFAULT
  (`OptimizationOptions.EnableAigRewriting` defaults to `true`).** This is a major
  (v3.0) change: the default optimizer output may now be a smaller multi-level form than
  the two-level/multi-level result produced before v3.0, because the DAG-aware AIG rewrite
  candidate is computed on the default path. As before, the candidate is adopted only when it
  is verified equivalent to the input (belt-and-suspenders `EquivalenceChecker`) *and* strictly
  cheaper by the existing cost metric, so every result stays equivalence-verified and never
  regresses. Set `EnableAigRewriting = false` to restore the exact pre-3.0 behavior. The only
  behavior change is the default value of this one flag; there is no public API change.

- **The internal AIG cut-rewrite library is now provably AND-minimal for every ≤4-input
  function.** The previous constructive Shannon/ITE template synthesis (correct but not
  minimal) is replaced by a baked table of minimum two-input-AND recipes, one per NPN class
  over 1..4 inputs (2, 4, 14 and 222 classes). The table is precomputed offline by a SAT-based
  exact-synthesis generator with complete breadth-first lower bounds (kept in the repo as
  `LogicalOptimizer.Tests/AigMinLibraryGenerator`, regenerable via the Exhaustive
  `AigMinLibraryTests`), so each stored recipe is certified to use the fewest possible AND
  nodes (the hardest 4-input NPN classes need 10; 4-input parity needs 9). Runtime lookup
  stays O(1) with no heavy static initialisation. This yields smaller cut-rewrite replacements
  — better rewrite quality when AIG rewriting is enabled — with no public API change;
  everything remains internal.

### Documentation

- **Verified examples and descriptions for the full public API.** Every capability area of
  the public surface (`PublicApi.approved.txt`) now has both a description and a runnable
  example: new docs-site articles for formula construction, the optimizer & options (incl.
  AIG rewriting being on by default in v3.0), normal forms & transformations, two-level
  minimization, SAT/cardinality/PB/MaxSAT, BDDs, equivalence & backbones, and exporters,
  registered in the articles TOC and linked from the site index and README capability
  guide. `cli-usage.md` now lists every CLI flag (including `--anf`). Every documented code
  snippet is mirrored by an executed, asserted test in
  `LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`, so the shown outputs are real
  and cannot silently drift. No library behavior or public API changed.

## [2.5.0] - 2026-07-28

### Added

- **Opt-in DAG-aware AIG cut rewriting in the optimizer (`OptimizationOptions.EnableAigRewriting`).**
  Experimental multi-level structural rewriting that reduces AND-node count via cut-based
  replacement (ABC-style `rewrite`): each AND node's ≤4-input cuts are enumerated, the local
  truth table is NPN-canonicalized, an exact library template is instantiated onto the cut
  leaves, and the move is applied only when it strictly shrinks the graph. Fanouts are
  redirected by a whole-graph rebuild-with-substitution (inherently correct, no fanout index),
  and the pass is function-preserving with a non-increasing AND count by construction. Wired
  into the facade as one additional multi-level candidate, adopted only when it is both verified
  equivalent to the input (belt-and-suspenders `EquivalenceChecker`) and strictly cheaper by the
  existing cost metric — so it can only ever improve the result. Off by default: enabling it
  never changes existing behavior; the only new public surface is the one boolean flag.

- **Internal AIG rewriting infrastructure (reference counting + MFFC).** The internal
  `AndInverterGraph` now maintains a per-node reference (fanout) count — the number of
  fanin edges pointing at each node, one per consuming AND node plus one for the Root
  primary output — kept in sync as the graph is built. On top of it, recursive
  `Dereference`/`Reference` primitives (exact inverses) and maximum fanout-free cone
  computation (`ComputeMffc`, `MffcSize`) provide the gain-evaluation primitive needed by
  the upcoming DAG-aware cut rewriter. This is internal bookkeeping only: there is no
  public API change and the existing AIG semantics (`And`/`Or`/`Ite`/`FromAst`/`ToAst`/
  `Evaluate`/`Cleanup`/`AndNodeCount`) are unchanged.
- **Internal AIG cut enumeration, NPN canonicalization and rewrite library.** New internal
  machinery for cut-based rewriting: bottom-up k-feasible cut enumeration (`AigCutEnumerator`,
  default k = 4) with dominated-cut pruning and a per-node cut cap, plus exact local
  truth-table extraction for each cut (verified against `Evaluate`). NPN canonicalization
  (`NpnCanonicalizer`) reduces a ≤4-input truth table to its canonical representative under
  the 2^m·m!·2 negation/permutation/output-negation group, returning both the forward and
  the inverse transform (round-trip exact); the 2^16 four-input functions form the classic
  222 NPN classes. A compact rewrite library (`AigRewriteLibrary`) synthesizes an exact
  `AigTemplate` (a small AND/complement recipe over the cut leaves) for any ≤4-input function
  via memoized Shannon decomposition of the canonical form, cached per NPN class and
  re-labeled onto concrete leaves by the NPN transform. All of this is internal to
  `LogicalOptimizer.Core` — no public API change — and feeds the forthcoming apply/rewrite
  loop.

## [2.4.0] - 2026-07-28

### Changed

- **In-place BDD variable sifting.** `BinaryDecisionDiagram.BuildWithSiftedOrder` now
  reorders variables with true adjacent-level swaps (Rudell-style dynamic reordering, à la
  CUDD) instead of rebuilding the whole diagram from the AST for every trial position. Each
  swap rewrites only the two affected levels and reuses node handles, so sifting is far
  cheaper while producing the same — canonical — result. Variable position is now decoupled
  from variable identity internally, and dead nodes left by a swap are garbage-collected so
  `NodeCount` stays an honest reachable-node metric. The public signature (including the
  `maxRebuilds` bound, now a cap on trial swaps) and results are unchanged; the node budget
  and cancellation token are still honored.

## [2.3.0] - 2026-07-28

### Added

- **d-DNNF knowledge compilation (new `LogicalOptimizer.Dnnf` package).** A top-down
  decision-DNNF compiler with unit propagation, connected-component decomposition and
  component caching turns a formula into a compact, hash-consed d-DNNF circuit.
  `KnowledgeCompilation.CompileToDnnf(AstNode, nodeBudget, CancellationToken)` produces a
  `DnnfCircuit` that answers exact `#SAT` model counting (`CountModels`, `BigInteger`),
  weighted model counting (`WeightedModelCount`) and lazy model enumeration
  (`EnumerateModels`) in time linear in the circuit size. Compilation goes through the
  equisatisfiable Tseitin CNF, which is equi-count over the input variables, so the model
  count matches the original formula with no projection needed; counts are verified exactly
  against the ROBDD oracle. The package depends only on Core and Sat (zero new runtime
  dependencies) and is published as the seventh NuGet package.

## [2.2.0] - 2026-07-28

### Added

- **Algebraic Normal Form (Zhegalkin polynomial) conversion.** New public
  `Transformations.ToAlgebraicNormalForm(AstNode, CancellationToken)` computes the
  canonical XOR-of-AND-monomials (Reed–Muller) form via a fast Möbius transform over
  the truth table (supported up to `TruthTable.MaxVariables`). Exposed on the CLI as
  the `--anf` flag.
- **Two-level `--dnf` comparison mode** in the benchmark comparison harness. The
  head-to-head against SymPy/PyEDA can now emit an apples-to-apples two-level SOP
  table (`dotnet run --project LogicalOptimizer.Benchmarks -- compare --dnf`),
  counting literals of the two-level `result.DNF` rather than the default
  multi-level factored output. The default (no-flag) behavior is unchanged. CI runs
  both tables; `doc/BENCHMARKS.md` and the docs site document the two-level result.

### CI

- **Post-publish NuGet verification.** The release workflow now runs
  `tools/verify_nuget.ps1` after `dotnet nuget push`, polling the nuget.org flat
  container (with backoff for indexing lag) until all six packages appear at the
  released version, and fails the release if any never show up. The script can also
  be run locally: `pwsh tools/verify_nuget.ps1 -Version <ver>`.

## [2.1.0] - 2026-07-27

### Added

- **BDD complement edges.** BDD edges now carry a complement bit (encoded in the
  low bit of the edge, with a single terminal node — FALSE is the complemented
  edge to TRUE) under the canonical rule that every stored node's THEN edge is
  regular. A function and its negation share the same node, so representing both
  polarities costs no extra nodes, and `Negate` is an O(1) complement-bit flip
  instead of a full `ite` recursion. All operations (evaluation, model counting
  and enumeration, restriction, quantification, composition) decode the
  complement bit; public behavior is unchanged and canonicity is preserved
  (equivalent formulas produce the identical edge, complement bit included).

### Fixed

- **Cancellation granularity in exact minimization.** `TruthTableMinimizer`'s
  branch-and-bound cover search (`CoverSearch`) observed only its own step
  limit, so a dense function with a large cyclic core could run for tens of
  seconds ignoring a mid-flight `CancellationToken` (a dense 13-variable case
  ran ~41 s). The cover search now checks the token periodically, and the
  covering-table row/column-dominance passes and the greedy fallback check it
  too; `MinimalPos` now forwards the token to its cover search.

### Tests

- New complement-edge tests: O(1) structural negation, complement-bit canonicity,
  `modelCount(f) + modelCount(!f) == 2^n`, and zero-extra-node sharing, all
  cross-checked against the differential and fuzzing brute-force oracles.
- Regression test for the cancellation-granularity fix above (dense 13-variable
  cover search, in the Performance mid-flight cancellation suite).
- Removed the gate-visible mid-flight cancellation test: reliably exercising
  *mid-flight* (not entry-point) cancellation needs a tuned multi-second
  workload, which is timing/input-dependent and belongs in the Performance
  suite (`MidFlightCancellationTests`), not the fast PR gate. Deterministic
  entry-point cancellation for the facade, SAT solver, QM minimizer and BDD
  stays covered in the gate by `ResourceBudgetAndCancellationTests`
  (pre-cancelled tokens).

## [2.0.0] - 2026-07-27

First exercised major break under the SemVer policy. Migration guide:
[MIGRATION-v2.md](MIGRATION-v2.md). The facade behavior contract (zone routing,
minimality statuses, budgets, equivalence verification of every result) is unchanged.

### Breaking

- **N-ary AST core**: `AndNode`/`OrNode` are now `NaryNode` subclasses with
  `IReadOnlyList<AstNode> Operands` instead of `Left`/`Right`. `BinaryNode` remains
  only as the base of the derived operators (`XorNode`, `ImpNode`, `EqvNode`,
  `NandNode`, `NorNode`), which keep `Left`/`Right`.
- **`ForceParentheses` removed** from AST nodes; the AST is fully immutable.
  Rendering is precedence-based via the new public `AstFormatter`.
- **Canonicalization at construction**: all And/Or trees are built through
  `FormulaFactory`, which flattens, sorts operands into a stable canonical order,
  removes duplicates, folds constants and complements, and interns results
  (structurally equal factory trees are reference-equal). Degenerate formulas fold
  to constants at parse time (`a | !a` parses to `1`). Output strings are
  canonically ordered; semantics are unchanged.
- **API narrowing** (~56 → 53 public types): internalized `Lexer`, `Parser`,
  `Token`, `TokenType`, `AndInverterGraph` (Core); `TseitinConverter`,
  `SatProofStep` and the `SatSolver.Proof` accessor (Sat; DRAT text via `ToDrat()`
  stays public); the raw int-handle BDD API (`Root`, `Ite`, `Compose`, `Restrict`,
  `Exists`/`ForAll`, `Negate`, `FromAst`, int overloads). Public entry for parsing
  is `FormulaFactory.Parse`.
- **Removed types**: `IOptimizer`, `ExpressionOptimizer`, `AstUtilities` and the ten
  optimizer classes (Constants, Associativity, Commutativity, Complement, DeMorgan,
  Absorption, Consensus, Redundancy, Distributive, Factorization) — replaced by the
  internal `LogicalOptimizer.Rewrite` layer.
- **`CsvTruthTableParser.ParseCsvToPartialTable`** now returns the typed
  `PartialTruthTable` (`Variables`/`OnSet`/`DontCareSet`) instead of a `ValueTuple`.
- **N-ary cost model**: an n-ary node counts as 1 AST node in metrics and
  optimization cost comparisons (was n−1 binary nodes).

### Added

- `NaryNode` base type with order-sensitive structural equality and cached hashes.
- `AstFormatter` — the single public precedence-based renderer used by `ToString`,
  the exporters and the visualizer.
- `FormulaFactory.Parse` and canonical operand ordering + thread-safe interning in
  the factory.
- `PartialTruthTable` result type for CSV parsing.
- Public root-based BDD members: `IsTautology()`, `IsContradiction()`,
  `CountSatisfyingAssignments()`, `Evaluate(assignment)`,
  `EnumerateSatisfyingAssignments()`, `FindSatisfyingAssignment()`.
- New canonical-invariant test suite (import idempotence, interning, canonical
  shape, print-parse fixpoint, pinned n-ary Tseitin clause counts).

### Changed

- Single-traversal internal `RewriteEngine` replaces the ten-optimizer fixpoint
  coordinator; rule order per node: De Morgan → absorption → consensus →
  redundancy → factorization (with growth rollback), plus the bounded
  expand-reduce step and the soundness guard, preserved 1:1 from v1.
  Cycle detection now uses interned references instead of strings.
- Tseitin/Plaisted–Greenbaum encode n-ary gates directly (n+1 clauses per n-ary
  AND/OR gate): identical CNF for binary chains, fewer auxiliary variables and
  clauses for wider gates — never more than v1.
- BLIF and Verilog exports emit n-ary gates.
- AST→AIG conversion uses balanced folding of n-ary operands (smaller depth).

### Fixed

- `SubcircuitLibrary` rewriting now greedily groups n-ary operands by ≤3-variable
  support, so subcircuits hidden by n-ary flattening are found again (v1 relied on
  binary subtree boundaries).
- The factorization growth guard compares literals before node count; under the
  n-ary cost model the old node-only guard rejected literal-reducing
  factorizations.

### Tests

- Full post-migration test re-audit (six parallel reviewers, all ten techniques
  re-run against the changed functionality; see [doc/TESTING.md](doc/TESTING.md)
  Part 4). Fixed two green-masking weakenings introduced by the migration (a
  consensus test softened to a count no-op; a SAT-miter cross-check that had
  degenerated to truth-table-vs-truth-table), removed exact-duplicate cases, and
  added the missing v2 coverage (flat n-ary rendering, n-ary Tseitin counts,
  canonical-order tie-breaks, real multi-output cube sharing, a gate-visible
  mid-flight cancellation test, a >12-variable SAT-miter differential). Suite:
  880 cases green; facade line coverage 90.15%.

## [1.0.0] - 2026-07-25

- Initial public package release: `LogicalOptimizer.Core` / `.Sat` / `.Bdd` /
  `.Minimization` / `LogicalOptimizer` (facade) / `LogicalOptimizer.Cli`.
