# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Targeting 3.1.0 — deployment, credibility and interoperability. All changes are additive;
the public API baseline diff is additive-only.

### Added

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
