# Testing Overview

LogicalOptimizer treats correctness as a first-class deliverable. The suite is built in two
layers: a **structured functional suite** organized by subject — the tests that say what the
library must *do* — and **ten systematic techniques** layered across all of it. The advanced
techniques do not stand on their own; they are cross-checks on top of a base that already pins
the behavior. Both layers have been audited (duplicates removed, weak oracles strengthened).
This page is a short map — the full strategy, actuality matrix, audit log, and mutation results
live in [`doc/TESTING.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/TESTING.md).

The CI gate runs **1 255 cases**, all green (the timing-sensitive and exhaustive-sweep categories
run separately on top of that — see [Running the tests](#running-the-tests)). The count is a
snapshot taken after the 2026-07-30 audit, not a contract.

## Layer 1 — the functional suite

Organized by subject under `LogicalOptimizer.Tests/`. These are the tests that fail first when
a behavior changes; the techniques below then catch what a hand-written example missed.

| Area | What it pins | Cases |
|---|---|---:|
| **Core** | Lexer, parser and its diagnostics, n-ary AST node contracts (≥ 2 operands, defensive copy, order-sensitive equality, cached hash, `Clone == this`), `FormulaFactory` canonicalization and interning, truth-table generation and comparison | 165 |
| **Optimizers** | Every rewrite rule and pattern detector, construction-time canonical ordering, distributive expansion, consensus, rule completeness, the extended-operator rule library under a truth-table sweep, and the final soundness guard's rollback contract | 196 |
| **Engines** | SAT (CDCL, incremental, unsat cores, DRAT proofs, core-guided MaxSAT) · BDD (operations, sifting) · AIG (cut enumeration, NPN canonicalization, rewriting) · d-DNNF (compilation, conditioning, sampling) · two-level minimization (Quine–McCluskey, SAT cover, Espresso-lite, multi-output sharing) · encodings (Tseitin, Plaisted–Greenbaum, cardinality / PB) · circuit serialization | 306 |
| **Facade & Analysis** | `BooleanExpressionOptimizer` end to end: statuses, metrics and the quality report, normal forms and ANF, the diagnostic trace, resource budgets and cancellation, equivalence checking, backbone computation and model enumeration | 190 |
| **Formats** | CSV truth tables (single- and multi-output), C# / BLIF / Verilog export, DIMACS / OPB / WCNF, AST visualizer | 116 |
| **CLI** | Argument parsing and validation, output formatting, the JSON report and the published schema it must validate against, the CSV-input contract | 92 |
| **Documentation** | Every code snippet in the README and in these articles, compiled, executed and asserted — plus the documented CLI flag set, the standard-format verb set, and the documented CLI transcripts compared line for line against the formatter the tool runs | 55 |

The remaining cases are the technique suites (101, see below), a projected-model-counting spike
(24) and the test infrastructure's own tests (10 — the random-expression generators and oracles
are themselves tested).

### What makes them *structured*

A subject folder is not a place to accumulate tests; five rules are enforced in review, and the
audits delete or strengthen whatever violates them:

1. **One behavior — one test.** A duplicate is deleted, keeping the stronger variant: an exact
   expected string plus an independent oracle beats an equivalence-only check, which beats
   `Contains`.
2. **No echo tests.** No asserting that a constructor stored its arguments, no compile-time
   facts (inheritance checks), no `Assert.NotNull(new X())`.
3. **No circular oracles.** A test may not re-implement production logic to check production
   logic. Oracles must be independent: brute-force enumeration, hand-computed expectations,
   textbook-known minimal costs, an external tool, or a different engine.
4. **No wall-clock vanity.** Scale tests assert correctness under a deterministic effort budget
   (conflict/node limits), never `Stopwatch < N ms`.
5. **Weak assertions are bugs.** `Assert.NotEmpty`, unfalsifiable disjuncts and
   `>= small-constant` checks get strengthened or deleted.

The rules have teeth, and an audit routinely ends with *fewer* cases asserting *more*:

- **2026-07-29** cut the gate suite from 1 158 to 1 152 while replacing a circular oracle in the
  flagship minimizer honesty test, where the "reference optimum" had been the same routine at a
  larger budget and is now cross-checked by an independent SAT prime-cover.
- **2026-07-30** cut 1 261 to 1 254. The headline finding was a *representativeness* one: the
  extended-operator rule library had 46 cases that asserted only the shape the implementation
  happens to build, so a rule firing on the wrong operand kind was invisible. Demonstrated rather
  than assumed — a planted mutant making `NAND(1, a)` return `1` instead of `!a` left all 52
  pre-existing cases green. Both rule files now sit on a truth-table sweep over a 64-pair operand
  grid with a per-rule firing counter, and the suite's own folder layout is enforced by a test
  instead of by convention.

## Layer 2 — the ten systematic techniques

| # | Technique | What it buys |
|---|---|---|
| 1 | **Property-Based** (CsCheck) | Invariants over generated formulas, including the extended-operator set |
| 2 | **Metamorphic** | Relations like permutation → byte-identical canonical output |
| 3 | **Mutation** (Stryker.NET) | Per-module runs with survivor triage; killer tests for real gaps |
| 4 | **Algebraic** | Every Boolean axiom encoded as a law |
| 5 | **Snapshot / Approval** (Verify) | Pinned golden output for rendering and pipelines |
| 6 | **Architecture** (ArchUnitNET) | Layering rules + the pinned public-API type list |
| 7 | **Differential** | Internal engine-vs-engine plus external oracles (SymPy in CI, Z3 locally) |
| 8 | **Fuzzing** | Deterministic fuzzers that check factory invariants (dedup / complement / sortedness) |
| 9 | **Characterization** | A golden-master corpus of expressions |
| 10 | **Combinatorial / Pairwise** | Covering array over the CLI/option axes |

They live in `Techniques/` alongside four contract guards that use the same machinery: the
member-level public-API baseline, the claims-vocabulary check behind
[`doc/CLAIMS.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/CLAIMS.md), the
package-forwarding contract check, and the suite-layout guard that keeps a test class in a file named
after it, under the folder for its subject — 101 cases in total.

The two **external** oracles no longer skip silently. `LOGICALOPTIMIZER_REQUIRE_SYMPY` and
`LOGICALOPTIMIZER_REQUIRE_Z3` turn an absent oracle into a failing test where it is expected to be
present; CI sets the SymPy one exactly when `pip install sympy` succeeded, so an oracle that stops
importing cannot quietly leave the differential technique asserting nothing.

## Verification is part of the product, not just the tests

Two invariants are enforced *in production*, not only under test:

- **Every optimization is verified equivalent** to its input before return (truth table
  ≤ 12 vars, SAT miter beyond) — see
  [Operation contracts & statuses](contracts-and-statuses.md).
- **The public API is pinned.** `ApiSurfaceTests.PublicApi_MatchesApprovedBaseline` fixes
  the full member-level surface, and an architecture rule pins the documented type list.
  A failing baseline is a release decision, not a test to silence. Regenerate an intended
  change with `LOGICALOPTIMIZER_REGENERATE_API=1` and review the diff.

## Running the tests

```bash
# Full suite
dotnet test

# Filtered
dotnet test --filter "TruthTable"

# Mutation testing (report in StrykerOutput/)
dotnet tool restore
cd LogicalOptimizer.Tests && dotnet stryker
```

Timing-sensitive and exhaustive-sweep categories run outside the CI gate:

```bash
dotnet test --filter "Category=Performance"
dotnet test --filter "Category=Exhaustive"   # e.g. all 4-variable functions
```

CI enforces an 80% line-coverage floor on the `LogicalOptimizer` facade assembly, which currently
measures 92.7% line / 84.6% branch / 96.1% method.
