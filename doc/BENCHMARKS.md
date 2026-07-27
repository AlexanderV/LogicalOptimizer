# Benchmarks

Machine-readable results (roadmap P0.3): every run of `LogicalOptimizer.Benchmarks`
emits `BenchmarkDotNet.Artifacts/results/*-report-full.json` (full measurement data)
alongside CSV/HTML/GitHub-markdown reports. Regenerate with:

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- --filter * [--job short]
```

Environment of the published run: BenchmarkDotNet v0.14.0, Windows 11, .NET 10.0.9,
X64 RyuJIT AVX2, ShortRun job (3 iterations - indicative, not publication-grade;
re-run with the default job for tighter error bars).

Cross-library comparison methodology note: numeric comparison against SymPy/PyEDA
requires a shared corpus, one machine and one cost model. The differential
*correctness* corpus against SymPy lives in the test suite
(`SymPyDifferentialTests`, CI). The *result-size / time* comparison table now lives
in [Comparison vs SymPy / PyEDA](#comparison-vs-sympy--pyeda-roadmap-b2) below:
OUR column is measured under those controls; the SymPy/PyEDA columns are produced
by a committed, self-skipping script where the tools are installed (never fabricated).
## Comparison vs SymPy / PyEDA (roadmap B2)

Result size (literal count) and optimization time on a **shared** function set,
against SymPy's `simplify_logic` (Quine–McCluskey / minterm two-level DNF) and
PyEDA's `espresso_exprs` (Espresso two-level SOP).

### Shared corpus

The corpus is a single committed file,
[`tools/comparison_corpus.txt`](../tools/comparison_corpus.txt) — read verbatim by
**both** sides so the comparison is genuinely on the same functions. 17 Boolean
functions, one per line (`<zone> | <name> | <expression>`, syntax `!`/`&`/`|`, no
constants), spanning:

- **small** — 10 functions, ≤ 10 variables: our *exact guarantee* zone, where the
  result is provably minimal (`MinimizationStatus == MinimalProven`).
- **mid** — 7 functions, 11–24 variables: our SAT-based prime-cover zone
  (verified, not truth-table-exhaustive).

The set is deliberately capped at 24 variables: SymPy's `simplify_logic` builds a
2ⁿ truth table, so beyond that a QM comparison is infeasible — our > 24-variable
heuristic zone has no practical SymPy comparator and is intentionally out of this
shared table.

### Methodology (exact commands to reproduce)

Two independent, single-machine, single-cost-model measurements. **Cost model:**
literal count = number of variable occurrences (`AstMetrics.CountLiterals`; for
SymPy/PyEDA, the count of literal leaves in the minimized expression). Our result
size is measured on the optimized form returned by `OptimizeExpression` — which may
be **multi-level (factored)**, so on some functions ours is smaller than a purely
two-level SOP; that is a genuine product difference, not a measurement artifact.

Our side (real numbers below):

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare
```

Competitor side (fills the SymPy/PyEDA columns where the tools are installed):

```powershell
python tools/compare_sympy_pyeda.py            # optionally: --max-vars 16
```

The Python script **self-skips** any tool that is not importable (printing a clear
note, exit 0) and never fabricates numbers — mirroring `SymPyDifferentialTests`'
skip behavior. Timings are wall-clock, machine-dependent, and indicative (our
harness reports the median of 7 runs after a warm-up); they are **not**
publication-grade error bars — the BenchmarkDotNet suites below remain the
authority for tight measurement.

### OUR measured results (real)

Environment: .NET 10 (SDK 10.0.301), Windows 11, X64. Single run of the `compare`
harness; median-of-7 per function.

| Zone | Function | Vars | Input literals | LogicalOptimizer literals | Status | Time (ms) |
|------|----------|-----:|---------------:|--------------------------:|--------|----------:|
| small | maj3 | 3 | 6 | 5 | MinimalProven | 0.545 |
| small | consensus3 | 3 | 6 | 4 | MinimalProven | 0.411 |
| small | xor2 | 2 | 4 | 4 | MinimalProven | 0.200 |
| small | xor3 | 3 | 12 | 10 | MinimalProven | 1.623 |
| small | maj4 | 4 | 12 | 9 | MinimalProven | 1.304 |
| small | mux2 | 3 | 4 | 4 | MinimalProven | 0.315 |
| small | pos6 | 6 | 6 | 6 | MinimalProven | 2.548 |
| small | eq5 | 5 | 10 | 10 | MinimalProven | 0.452 |
| small | pairs8 | 8 | 8 | 8 | MinimalProven | 2.189 |
| small | pairs10 | 10 | 10 | 10 | MinimalProven | 11.020 |
| mid | pairs12 | 12 | 12 | 12 | Heuristic | 11.654 |
| mid | collapse14 | 14 | 28 | 7 | Heuristic | 1.723 |
| mid | pairs16 | 16 | 16 | 16 | Heuristic | 4.750 |
| mid | collapse18 | 18 | 36 | 9 | Heuristic | 1.536 |
| mid | pairs20 | 20 | 20 | 20 | Heuristic | 5.319 |
| mid | chain22 | 22 | 60 | 40 | Heuristic | 79.119 |
| mid | pairs24 | 24 | 24 | 24 | Heuristic | 8.752 |

Reading the table: within the guarantee zone every result is `MinimalProven`
(e.g. `consensus3` 6 → 4, `maj4` 12 → 9). In the mid zone the reductions come from
the verified SAT prime cover — e.g. `collapse14` 28 → 7 and `collapse18` 36 → 9
collapse the `pᵢ&qᵢ | pᵢ&!qᵢ` pairs to `pᵢ`. The `pairsN` families are already
minimal on input, so a correct minimizer leaves the literal count unchanged.

### SymPy / PyEDA columns — PENDING (measured where tools exist)

**Not measured in this environment.** `python -c "import sympy"` and
`import pyeda` both fail here (PyPI is blocked on the maintainer's network; SymPy
is pip-installed only in CI). These columns are therefore left as an explicit
placeholder — no numbers are invented. To fill them, run the committed script
where the tools are installed and paste its markdown output here:

```powershell
python tools/compare_sympy_pyeda.py
```

| Zone | Function | Vars | SymPy literals | SymPy ms | PyEDA literals | PyEDA ms |
|------|----------|-----:|:--------------:|:--------:|:--------------:|:--------:|
| — | *(all 17 rows)* | — | — (run script) | — | — (run script) | — |

Once produced, join on `(Zone, Function, Vars)` with the OUR-results table above
for the side-by-side result-size / time comparison.

### OptimizationBenchmarks

| Method                    | Mean        | Error       | StdDev    | Gen0     | Gen1     | Gen2     | Allocated  |
|-------------------------- |------------:|------------:|----------:|---------:|---------:|---------:|-----------:|
| SmallExpression           |    87.94 μs |    31.98 μs |  1.753 μs |  35.8276 |   0.3052 |        - |  219.77 KB |
| GuaranteeZoneTenVariables | 7,791.00 μs | 1,244.70 μs | 68.226 μs | 578.1250 | 328.1250 | 242.1875 | 3976.35 KB |
| MidRangeFourteenVariables |   443.08 μs |   157.77 μs |  8.648 μs | 166.5039 |   3.9063 |        - | 1020.68 KB |

### SatSolverBenchmarks

| Method                         | Mean     | Error    | StdDev  | Gen0    | Gen1   | Allocated |
|------------------------------- |---------:|---------:|--------:|--------:|-------:|----------:|
| PhaseTransition60Variables     | 471.6 μs | 20.36 μs | 1.12 μs | 29.7852 | 4.8828 | 184.87 KB |
| DeMorganEquivalence30Variables | 167.7 μs |  8.94 μs | 0.49 μs | 28.3203 | 3.4180 | 174.32 KB |

### SAT corpus (SATLIB-style, generated) — perf-regression

Roadmap **B1**. A small, deterministic corpus of hard-ish SAT instances is vendored at
[`LogicalOptimizer.Benchmarks/SatCorpus/`](../LogicalOptimizer.Benchmarks/SatCorpus/README.md)
and driven by `SatCorpusRegressionTests` (`Category=Performance`, excluded from the PR
gate; run nightly by `.github/workflows/sat-benchmarks.yml`).

**Generated, not downloaded.** These are SATLIB-*style* instances produced in-repo by a
committed, seeded generator (`SatCorpusGenerator`) — **not** the literal SATLIB `uf`/`uuf`
archive files. That keeps the corpus fully reproducible and free of external-download /
provenance concerns; the `uf`/`uuf` naming is borrowed only as a labelling convention.
Every expected verdict is known **by construction** or **independently brute-force
verified** at generation time (the solver under test is never the verdict oracle):

- **10 satisfiable** `uf*` — planted random 3-SAT at the phase-transition ratio ≈ 4.26,
  75–125 variables (320–532 clauses). SAT by construction (a planted assignment satisfies
  every clause); the solver's returned model is re-verified against the CNF.
- **6 unsatisfiable** `uuf0*` — forced random 3-SAT (16–18 variables), seed-searched until
  an exhaustive 2ⁿ brute-force oracle confirms UNSAT.
- **4 unsatisfiable** `uuf-php*` — pigeonhole PHP(n+1 → n) for n = 4..7 (20–56 variables),
  UNSAT by construction and exponentially hard for CDCL.

The regression suite solves each instance under a **fixed deterministic conflict budget
(5,000,000)** and asserts the verdict is not `Unknown` and matches the known SAT/UNSAT
(plus the independent model / brute-force re-checks). There are **no wall-clock
assertions** (doc/TESTING.md rule 4). Run locally with:

```powershell
dotnet test --filter "Category=Performance"
```

Indicative local numbers (machine-dependent; Release, single run, .NET 10, x64 —
logged by the test, never asserted on):

| Instance | Verdict | Conflicts | Time |
|----------|:-------:|----------:|-----:|
| `uf075-01` (75 v)  | SAT   |    82 |   1 ms |
| `uf120-01` (120 v) | SAT   | 1,016 |  14 ms |
| `uf125-01` (125 v) | SAT   |   392 |  18 ms |
| `uuf018-05` (18 v) | UNSAT |     9 |  <1 ms |
| `uuf-php6-5` (30 v)| UNSAT |   165 |   1 ms |
| `uuf-php7-6` (42 v)| UNSAT |   803 |  24 ms |
| `uuf-php8-7` (56 v)| UNSAT | 3,287 | 101 ms |

All 20 instances resolve to the correct verdict far inside the budget (the hardest,
pigeonhole PHP(8→7), needs ≈ 3.3k conflicts). The budget carries wide headroom on purpose:
a future change that makes the solver need materially more search to decide any of these is
precisely the regression this corpus is meant to surface.

### ExactMinimizationBenchmarks

| Method                     | Mean     | Error    | StdDev   | Gen0      | Gen1     | Allocated |
|--------------------------- |---------:|---------:|---------:|----------:|---------:|----------:|
| QuineMcCluskeyTenVariables | 81.65 ms | 9.980 ms | 0.547 ms | 1750.0000 | 125.0000 |   11.2 MB |

### NewEnginesBenchmarks

| Method                             | Mean         | Error         | StdDev     | Gen0       | Gen1      | Gen2     | Allocated    |
|----------------------------------- |-------------:|--------------:|-----------:|-----------:|----------:|---------:|-------------:|
| EspressoLite_FortyVariableCover    | 66,879.39 μs | 13,336.972 μs | 731.044 μs | 18727.2727 | 1090.9091 | 272.7273 | 115013.61 KB |
| BddSifting_TwelveVariablePairs     |  2,875.13 μs |    392.783 μs |  21.530 μs |   822.2656 |   76.1719 |        - |   5048.98 KB |
| FormulaFactory_ImportFortyVarCover |  1,689.61 μs |    225.850 μs |  12.380 μs |    50.7813 |   15.6250 |        - |     323.4 KB |
| Aig_FromFortyVarCover              |     62.24 μs |      6.092 μs |   0.334 μs |     8.7280 |    0.4272 |        - |     53.57 KB |


