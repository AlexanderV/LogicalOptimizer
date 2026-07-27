# SATLIB-style SAT corpus (generated)

A small, deterministic corpus of hard-ish SAT instances used by the nightly
perf-regression job (roadmap **B1**). It backs the "modern CDCL" claim (VSIDS heap,
LBD, Luby restarts, subsumption preprocessing) with a repeatable correctness-under-budget
signal on non-trivial instances.

> **These are SATLIB-*style* GENERATED instances — NOT the literal SATLIB `uf`/`uuf`
> archive files.** They are produced in-repo by a committed, seeded generator so the
> corpus is fully reproducible and free of external-download / provenance concerns. The
> file names borrow SATLIB's `uf` (satisfiable) / `uuf` (unsatisfiable) convention only
> as a familiar labelling scheme. Every expected verdict is known **by construction** or
> **independently brute-force-verified** at generation time — no solver-under-test was
> used as the verdict oracle.

## How they were generated

Generator: [`SatCorpusGenerator`](../../LogicalOptimizer.Tests/TestInfrastructure/SatCorpusGenerator.cs)
(in the test project). All randomness comes from `System.Random` with the fixed seeds
recorded below and in each file's `c seed` header. Regenerate with:

```powershell
$env:LOGICALOPTIMIZER_REGENERATE_SATCORPUS = '1'
dotnet test LogicalOptimizer.Tests --filter "FullyQualifiedName~RegenerateCorpus"
```

Each `.cnf` is standard DIMACS with a self-describing header: `c kind`, `c expected`
(SAT/UNSAT — the source of truth for the regression test), `c variables`, `c clauses`,
`c ratio`, `c seed`, `c note`.

### uf\* — satisfiable (planted random 3-SAT)

Random 3-SAT at the phase-transition ratio **≈ 4.26** (the empirical hardness peak). A
random assignment is planted first; every clause is drawn uniformly and, if the planted
assignment would falsify it, one literal is set to its satisfying polarity. The planted
assignment therefore satisfies the whole formula — **SAT by construction**. The
regression test additionally re-verifies the solver's returned model against every clause.

| File | Variables | Clauses | Seed |
|------|----------:|--------:|-----:|
| `uf075-01.cnf` | 75  | 320 | 12000 |
| `uf080-01.cnf` | 80  | 341 | 12001 |
| `uf090-01.cnf` | 90  | 383 | 12002 |
| `uf095-01.cnf` | 95  | 405 | 12003 |
| `uf100-01.cnf` | 100 | 426 | 12004 |
| `uf105-01.cnf` | 105 | 447 | 12005 |
| `uf110-01.cnf` | 110 | 469 | 12006 |
| `uf115-01.cnf` | 115 | 490 | 12007 |
| `uf120-01.cnf` | 120 | 511 | 12008 |
| `uf125-01.cnf` | 125 | 532 | 12009 |

### uuf\* (random) — unsatisfiable (forced, brute-force-verified)

Uniform random 3-SAT near the transition ratio, with seeds searched upward from a fixed
start until an exhaustive **2ⁿ brute-force oracle** confirms UNSAT. Kept ≤ 18 variables so
the brute-force verification is cheap and fully independent of the solver under test. The
regression test re-runs that same brute-force check (≤ 20 vars) as an independent oracle.

| File | Variables | Clauses | Seed |
|------|----------:|--------:|-----:|
| `uuf016-01.cnf` | 16 | 68 | 71000 |
| `uuf016-02.cnf` | 16 | 68 | 72009 |
| `uuf017-03.cnf` | 17 | 72 | 73000 |
| `uuf017-04.cnf` | 17 | 72 | 74001 |
| `uuf018-05.cnf` | 18 | 77 | 75008 |
| `uuf018-06.cnf` | 18 | 77 | 76000 |

### uuf-php\* — unsatisfiable (pigeonhole, structured)

Pigeonhole **PHP(n+1 → n)**: n+1 pigeons into n holes, with "each pigeon in some hole" and
"no hole holds two pigeons" clauses. **UNSAT by construction** (pigeons exceed holes) and
famously **exponentially hard for resolution / CDCL**, so it exercises the learnt-clause
machinery rather than just unit propagation.

| File | Pigeons → Holes | Variables | Clauses |
|------|:---------------:|----------:|--------:|
| `uuf-php5-4.cnf` | 5 → 4 | 20 | 45  |
| `uuf-php6-5.cnf` | 6 → 5 | 30 | 81  |
| `uuf-php7-6.cnf` | 7 → 6 | 42 | 133 |
| `uuf-php8-7.cnf` | 8 → 7 | 56 | 204 |

## How it is exercised

[`SatCorpusRegressionTests`](../../LogicalOptimizer.Tests/Engines/Sat/SatCorpusRegressionTests.cs)
(`Category=Performance`, excluded from the PR gate) loads every instance, solves it under a
**fixed deterministic conflict budget** (5,000,000 conflicts) and asserts:

1. the verdict is not `Unknown` (the budget must suffice) and matches the recorded
   `c expected` SAT/UNSAT — this is the regression signal;
2. for SAT instances, the returned model satisfies every clause (independent re-check);
3. for UNSAT instances ≤ 20 vars, brute force confirms no satisfying assignment exists.

There are **no wall-clock assertions** (doc/TESTING.md Part 2, rule 4); per-instance
conflict counts and timings are written to the test output for human inspection only.
Run locally with `dotnet test --filter "Category=Performance"` or in the nightly
`.github/workflows/sat-benchmarks.yml` job.
