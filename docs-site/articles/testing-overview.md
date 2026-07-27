# Testing Overview

LogicalOptimizer treats correctness as a first-class deliverable: alongside example-based
tests, the suite layers **ten systematic techniques**, and the whole suite has been audited
(duplicates removed, weak oracles strengthened). This page is a short map — the full
strategy, actuality matrix, audit log, and mutation results live in
[`doc/TESTING.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/TESTING.md).

## The ten techniques

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
