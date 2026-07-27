# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
