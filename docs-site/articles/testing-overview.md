# Testing Overview

LogicalOptimizer treats correctness as a first-class deliverable. The suite is built in two
layers: a **structured functional suite** organized by subject — the tests that say what the
library must *do* — and **ten systematic techniques** layered across all of it. The advanced
techniques do not stand on their own; they are cross-checks on top of a base that already pins
the behavior. Both layers have been audited (duplicates removed, weak oracles strengthened).
This page is a short map — the full strategy, actuality matrix, audit log, and mutation results
live in [`doc/TESTING.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/TESTING.md).

Discovery finds **1 230 test cases**; the CI gate runs **1 152** of them (the timing-sensitive
and exhaustive-sweep categories run separately — see [Running the tests](#running-the-tests)).

## Layer 1 — the functional suite

Organized by subject under `LogicalOptimizer.Tests/`. These are the tests that fail first when
a behavior changes; the techniques below then catch what a hand-written example missed.

| Area | What it pins | Cases |
|---|---|---:|
| **Core** | Lexer, parser and its diagnostics, n-ary AST node contracts (≥ 2 operands, defensive copy, order-sensitive equality, cached hash, `Clone == this`), `FormulaFactory` canonicalization and interning, truth-table generation and comparison | 134 |
| **Optimizers** | Every rewrite rule and pattern detector, construction-time canonical ordering, distributive expansion, consensus, rule completeness, and the soundness guard's rollback contract | 186 |
| **Engines** | SAT (CDCL, incremental, unsat cores, DRAT proofs) · BDD (operations, sifting) · AIG (cut enumeration, NPN canonicalization, rewriting) · d-DNNF (compilation, conditioning, sampling) · two-level minimization (Quine–McCluskey, SAT cover, Espresso-lite) · encodings (Tseitin, Plaisted–Greenbaum, cardinality / PB / MaxSAT) | 338 |
| **Facade & Analysis** | `BooleanExpressionOptimizer` end to end: statuses, metrics and the quality report, normal forms and ANF, the diagnostic trace, resource budgets and cancellation, backbone computation and model enumeration | 187 |
| **Formats** | CSV truth tables (single- and multi-output), C# / BLIF / Verilog export, DIMACS / OPB / WCNF, AST visualizer | 116 |
| **CLI** | Argument parsing and validation, output formatting, the JSON report and the published schema it must validate against | 77 |
| **Documentation** | Every code snippet in the README and in these articles, compiled, executed and asserted — the outputs shown are real | 47 |

The remaining cases are the technique suites (109, see below), the test infrastructure's own
tests (10 — the random-expression generators and oracles are themselves tested) and a
projected-model-counting spike (26).

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

The rules have teeth: the 2026-07-29 audit cut the gate suite from 1 158 to 1 152 cases while
adding assertions — including replacing a circular oracle in the flagship minimizer honesty test,
where the "reference optimum" had been the same routine at a larger budget and is now cross-checked
by an independent SAT prime-cover.

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

They live in `Techniques/` alongside three contract guards that use the same machinery: the
member-level public-API baseline, the claims-vocabulary check behind
[`doc/CLAIMS.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/CLAIMS.md), and the
package-contract audit — 109 cases in total.

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

CI enforces an 80% line-coverage floor (the suite sits around ~89%).
