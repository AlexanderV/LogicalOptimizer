# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Turning shipped capabilities into a versioned, independently verifiable product contract. No
public API change; no runtime behaviour change.

### Added

- **Published JSON Schema for the CLI report.** The `--format=json` document is now a contract
  rather than a convention: [`schema/cli-report-v1.schema.json`](schema/cli-report-v1.schema.json)
  (Draft 2020-12, served from the docs site at the `$id` it declares), seven golden example
  reports in [`schema/examples/`](schema/examples) covering success, `BudgetExceeded` minimality, a
  `TooLarge` normal form, the optional `advanced` field, `--trace`, a structured parse error and a
  bare processing error, and [`schema/README.md`](schema/README.md) spelling out what may change
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
