# Benchmarks

Machine-readable results (roadmap P0.3): every run of `LogicalOptimizer.Benchmarks`
emits `BenchmarkDotNet.Artifacts/results/*-report-full.json` (full measurement data)
alongside CSV/HTML/GitHub-markdown reports. Regenerate with:

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- --filter * [--job short]
```

Environment of the published run: BenchmarkDotNet v0.14.0, Windows 11, .NET 10.0.9,
X64 RyuJIT AVX2, ShortRun job (3 iterations - indicative, not publication-grade;
re-run with the default job for tighter error bars). Recorded 2026-07-30, from the same
run that produced [`doc/perf-baseline.json`](perf-baseline.json) — so the allocation
column of the three gated classes below and the committed baseline always agree. The
`SatSolverBenchmarks` table is from an earlier run and is not part of the gate.

## Performance regression gate (roadmap P0.3)

The regression gate is **allocation-based, not time-based**. BenchmarkDotNet's
`MemoryDiagnoser` reports *allocated bytes per operation* deterministically and
machine-independently, so comparing allocations is a sound gate that does **not** flake
on shared / loaded CI runners. Wall-clock time is captured and printed for information
only and is **never asserted** (same policy as the SAT corpus suite and
[doc/TESTING.md](TESTING.md) rule 4).

The committed baseline is [`doc/perf-baseline.json`](perf-baseline.json): per benchmark
id, the baseline `allocatedBytesPerOperation` plus, *for reference only*,
`meanNanoseconds`. Its top-level `note` states that time is informational and the gate is
allocation-based, and `environment` records the run manifest (BenchmarkDotNet, .NET
runtime + SDK, OS, architecture). The baseline covers three classes — `OptimizationBenchmarks`,
`NewEnginesBenchmarks` (the Espresso-lite / BDD / AIG / FormulaFactory hot paths) and
`ExactMinimizationBenchmarks` (the exact Quine–McCluskey hot path).

### Run the gate locally

```powershell
# 1. produce a fresh run (ShortRun is enough - allocations are exact even in ShortRun)
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- `
  --filter '*OptimizationBenchmarks*' '*NewEngines*' '*ExactMinimization*' --job short

# 2. compare it against the committed baseline (default threshold 10%)
pwsh tools/compare_benchmarks.ps1 -Current BenchmarkDotNet.Artifacts/results
```

The tool prints a per-benchmark allocation table (baseline / current / delta% /
`OK`|`REGRESSION`) and a separate informational time-delta table. It **exits 1** if any
benchmark's allocations exceed `baseline * (1 + Threshold)` (`-Threshold`, default
`0.10`); otherwise exit 0. New benchmarks (present in the run but not the baseline) are
reported but never fail; missing benchmarks (in the baseline but absent from the run) are
reported as a warning. `-Current` accepts either a single `*-report-full.json` file or a
directory of them.

### Regenerate the baseline (after an intentional hot-path change)

Analogous to the golden-master regeneration convention — only after a reviewed,
intentional change, and the updated `doc/perf-baseline.json` is committed with it:

```powershell
pwsh tools/compare_benchmarks.ps1 -Current BenchmarkDotNet.Artifacts/results -UpdateBaseline
```

### CI

[`.github/workflows/perf.yml`](../.github/workflows/perf.yml) runs the ShortRun
benchmarks and the gate **weekly** and on `workflow_dispatch` — deliberately **off the
PR hot path** (BenchmarkDotNet runs are slow and the allocation gate gains nothing from
per-PR cadence). A regression over threshold fails the job; machine noise does not,
because only allocations are gated. The manual dispatch has an `update_baseline` opt-in
that regenerates and uploads the baseline instead of gating.

Cross-library comparison methodology note: numeric comparison against SymPy/PyEDA
requires a shared corpus, one machine and one cost model. The differential
*correctness* corpus against SymPy lives in the test suite
(`SymPyDifferentialTests`, CI). The *result-size / time* comparison table now lives
in [Comparison vs SymPy / PyEDA](#comparison-vs-sympy--pyeda-roadmap-b2) below:
OUR column is measured under those controls; the SymPy/PyEDA columns are produced
by a committed, self-skipping script where the tools are installed (never fabricated).
## Controlled cross-library comparison (roadmap P0.2)

The controlled, artifact-backed cross-library comparison (four result sets — symbolic
optimization, two-level minimization, SAT, BDD/d-DNNF) lives under
[`doc/comparison/`](comparison/) with its acceptance contract in
[`doc/COMPARISON_METHODOLOGY.md`](COMPARISON_METHODOLOGY.md). Every OUR number there
comes from the committed [`our-results.json`](comparison/our-results.json), regenerated
by a single command:

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite
```

Competitor columns are left `pending (run <command>)` (never fabricated); the adapters
that fill them are in [`tools/comparison/`](../tools/comparison/). The SymPy/PyEDA
result-size table below is the earlier B2 slice of that comparison.

## Comparison vs SymPy / PyEDA (roadmap B2)

Result size (literal count) and optimization time on a **shared** function set,
against SymPy's `simplify_logic` (Quine–McCluskey / minterm two-level DNF) and
PyEDA's Espresso over a truth table (`expr2truthtable` + `espresso_tts`, a
two-level SOP; the truth-table path is bounded by 2ⁿ, avoiding the exponential
`to_dnf()` blow-up). Each function runs under a per-function timeout so a
pathological case is marked `timeout` rather than hanging the run.

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
# Default: multi-level (factored) output
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare
# Apples-to-apples two-level SOP (result.DNF), matching SymPy/PyEDA
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare --dnf
```

Competitor side (fills the SymPy/PyEDA columns where the tools are installed):

```powershell
python tools/compare_sympy_pyeda.py            # optionally: --max-vars 14 --timeout 10
```

`--max-vars N` skips functions above N variables (both tools build a 2ⁿ truth
table); `--timeout` caps each function (POSIX/SIGALRM) so a slow PyEDA Espresso
run is marked `timeout` instead of hanging. CI runs this step on the **Linux**
runner only (Windows lacks SIGALRM, so the per-function timeout would not fire
there).

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

### Head-to-head: result size (literals)

Competitor columns measured on the GitHub **Linux** CI runner (Python 3.12.3,
sympy 1.14.0, pyeda 0.29.0, `--max-vars 14 --timeout 10`); LogicalOptimizer
column from the OUR-results table above. **Literal count is machine-independent**
(it is the size of the result), so this side-by-side is the real comparison; the
timing note that follows is only indicative. Bold = LogicalOptimizer strictly
smaller.

| Zone | Function | Vars | LogicalOptimizer | SymPy | PyEDA |
|------|----------|-----:|:----------------:|:-----:|:-----:|
| small | maj3 | 3 | **5** | 6 | 6 |
| small | consensus3 | 3 | 4 | 4 | 4 |
| small | xor2 | 2 | 4 | 4 | 4 |
| small | xor3 | 3 | **10** | 12 | 12 |
| small | maj4 | 4 | **9** | 12 | 12 |
| small | mux2 | 3 | 4 | 4 | 4 |
| small | pos6 | 6 | **6** | 24 | 24 |
| small | eq5 | 5 | 10 | 10 | 10 |
| small | pairs8 | 8 | 8 | 8 | 8 |
| small | pairs10 | 10 | 10 | `timeout` | 10 |
| mid | pairs12 | 12 | 12 | `timeout` | 12 |
| mid | collapse14 | 14 | **7** | `timeout` | 7 |

**Reading it.** On result size LogicalOptimizer is **never larger** than the
two-level minimizers and often smaller: `maj3` 5 vs 6, `xor3` 10 vs 12, `maj4`
9 vs 12, and `pos6` **6 vs 24**. The reason is a genuine capability difference —
`OptimizeExpression` returns a **multi-level (factored)** form, while SymPy's
`simplify_logic` and PyEDA's Espresso return a **two-level SOP**. `pos6` is the
clearest case: a 6-literal product-of-sums stays 6 literals for us but expands to
a 24-literal DNF for a two-level tool. That multi-level advantage is real, but it
is **not** a like-for-like proof that our SOP minimizer beats theirs — for that,
see the apples-to-apples two-level table below.

### Head-to-head: two-level SOP result size (apples-to-apples, `--dnf`)

The table above compares our *default* multi-level output against two-level tools.
To compare **like for like**, run our two-level SOP path and count `result.DNF`
(the exact QM / SAT-cover / espresso-lite cover — the same two-level form SymPy and
PyEDA produce):

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare --dnf
```

OUR column below is the real `--dnf` literal count from that harness (Windows-local
run, .NET 10); the SymPy/PyEDA columns are the **same** Linux-CI numbers as the
multi-level table. Bold = LogicalOptimizer strictly smaller.

| Zone | Function | Vars | LogicalOptimizer (`--dnf`) | SymPy | PyEDA |
|------|----------|-----:|:--------------------------:|:-----:|:-----:|
| small | maj3 | 3 | 6 | 6 | 6 |
| small | consensus3 | 3 | 4 | 4 | 4 |
| small | xor2 | 2 | 4 | 4 | 4 |
| small | xor3 | 3 | 12 | 12 | 12 |
| small | maj4 | 4 | 12 | 12 | 12 |
| small | mux2 | 3 | 4 | 4 | 4 |
| small | pos6 | 6 | 24 | 24 | 24 |
| small | eq5 | 5 | 10 | 10 | 10 |
| small | pairs8 | 8 | 8 | 8 | 8 |
| small | pairs10 | 10 | 10 | `timeout` | 10 |
| mid | pairs12 | 12 | 12 | `timeout` | 12 |
| mid | collapse14 | 14 | 7 | `timeout` | 7 |

**Reading it (the honest claim).** On a genuine two-level basis LogicalOptimizer
**ties** the specialized two-level minimizers cube for cube on every function where
they finish — `maj3` 6 = 6, `xor3` 12 = 12, `maj4` 12 = 12, `pos6` 24 = 24, and it
matches PyEDA (which never times out) on all 12 rows. This is what the multi-level
table alone could not show: our exact SOP minimizer is not merely *smaller because
it is factored*, it is **at parity** with SymPy/PyEDA on the two-level form they
each emit. The multi-level column is then an *additional* win on top, not the whole
story.

**Timing (indicative, cross-machine — not a controlled benchmark).** SymPy builds
a 2ⁿ truth table for Quine–McCluskey, so it degrades sharply with variable count:
`pairs8` took ~125 ms and it **timed out (> 10 s)** from `pairs10` (10 vars)
onward. PyEDA (Espresso over the same truth table) and LogicalOptimizer both stay
in the low-millisecond range across the whole set (our numbers in the table above
are Windows-local; the competitor numbers are Linux-CI — do not compare the two
machines' milliseconds directly). Beyond 14 variables only LogicalOptimizer runs:
both competitor tools build 2ⁿ rows and are impractical there, which is why the
shared corpus caps the comparison.

Reproduce the competitor columns (Linux/POSIX, where the per-function timeout
fires) with `python tools/compare_sympy_pyeda.py`; the CI **Comparison table**
step runs both our harness and this script on the Linux runner and prints them to
the log.

### OptimizationBenchmarks

| Method                    | Mean        | Error      | StdDev    | Gen0     | Gen1     | Gen2    | Allocated  |
|-------------------------- |------------:|-----------:|----------:|---------:|---------:|--------:|-----------:|
| SmallExpression           |    97.05 μs |  26.07 μs  |  1.429 μs |  31.1279 |   0.3662 |       - |  190.94 KB |
| GuaranteeZoneTenVariables | 4,080.47 μs | 684.11 μs  | 37.498 μs | 246.0938 | 164.0625 | 82.0313 | 1449.12 KB |
| MidRangeFourteenVariables |   452.57 μs | 218.95 μs  | 12.001 μs |  97.6563 |   3.9063 |       - |  598.39 KB |

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

| Method                     | Mean     | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|--------------------------- |---------:|----------:|----------:|--------:|-------:|----------:|
| QuineMcCluskeyTenVariables | 2.467 ms | 0.3248 ms | 0.0178 ms | 39.0625 | 3.9063 | 253.06 KB |

### NewEnginesBenchmarks

| Method                             | Mean        | Error       | StdDev    | Gen0     | Gen1   | Allocated  |
|----------------------------------- |------------:|------------:|----------:|---------:|-------:|-----------:|
| EspressoLite_FortyVariableCover    | 6,103.01 μs | 384.432 μs  | 21.072 μs |  23.4375 |      - |  168.13 KB |
| BddSifting_TwelveVariablePairs     |   590.70 μs | 154.188 μs  |  8.452 μs | 218.7500 | 8.7891 | 1341.06 KB |
| FormulaFactory_ImportFortyVarCover |    34.52 μs |   5.495 μs  |  0.301 μs |  16.6931 | 0.9155 |  102.38 KB |
| Aig_FromFortyVarCover              |    21.37 μs |   4.959 μs  |  0.272 μs |  13.0768 | 0.7172 |   80.13 KB |
| Aig_FromFortyVarCover              |     62.24 μs |      6.092 μs |   0.334 μs |     8.7280 |    0.4272 |        - |     53.57 KB |


