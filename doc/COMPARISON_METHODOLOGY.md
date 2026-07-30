# Cross-library comparison — methodology (roadmap P0.2)

This document is the **acceptance contract** for the controlled cross-library
comparison. It turns the qualitative comparison into a reproducible, artifact-backed
one. Read it together with:

- the OUR-side artifacts: [`doc/comparison/our-results.json`](comparison/our-results.json),
  [`doc/comparison/summary.md`](comparison/summary.md),
  [`doc/comparison/manifest.json`](comparison/manifest.json);
- the OUR-side harness: `LogicalOptimizer.Benchmarks/CrossLibraryComparisonHarness.cs`
  (`-- comparison-suite`);
- the competitor scaffolding: [`tools/comparison/`](../tools/comparison/) and
  [`tools/compare_sympy_pyeda.py`](../tools/compare_sympy_pyeda.py).

## Scope and honesty boundary

The **LogicalOptimizer ("OUR") column** is produced by the harness and committed, and
**no competitor number is ever fabricated** — an absent tool self-skips and its cell
stays `pending`, a tool over the budget records `timeout`. The competitor side is now
executed in one controlled Linux container ([`tools/comparison/Dockerfile`](../tools/comparison/Dockerfile),
driven by [`run_all_in_container.sh`](../tools/comparison/run_all_in_container.sh)) that
bundles the OUR harness with SymPy, PyEDA, CaDiCaL, Kissat, Z3, d4 and LogicNG, all
version-pinned, and the real numbers are merged into
[`doc/comparison/merged.md`](comparison/merged.md). The generated `summary.md` still ships
with competitor cells `pending` — it is the pure OUR-side artifact; `merged.md` is where
the competitor columns are filled from the same committed corpus.

The container is the reproducible **manual / periodic** path; CI stays OUR-only (a fast
gate) rather than building the solver toolchain on every run. `c2d` is proprietary and not
in the container, so its #SAT column self-skips (only `d4` runs).

## Participants

| Task | OUR engine | Competitors (pending) |
|------|------------|-----------------------|
| Symbolic optimization | `BooleanExpressionOptimizer.OptimizeExpression` (multi-level / factored) | SymPy, PyEDA |
| Two-level minimization | `result.DNF` (exact QM / SAT-cover / espresso-lite SOP) | SymPy `simplify_logic(form="dnf")`, PyEDA `espresso_tts` |
| SAT | built-in CDCL `SatSolver` on the equivalence miter | CaDiCaL, Kissat, Z3 |
| BDD / d-DNNF | `BinaryDecisionDiagram`, `KnowledgeCompilation.CompileToDnnf` | LogicNG (BDD), c2d / d4 / Z3 (#SAT) |

## Single-runner, one-CPU policy

- **One runner, one machine, per table.** OUR numbers and any one competitor's
  numbers that are compared head-to-head must come from the *same* machine. Numbers
  from different machines are never put in the same timing comparison.
- **Single-threaded.** The harness runs one function at a time on one thread; no
  parallelism across corpus functions.
- **Warm-up.** Each measured engine is warmed once (JIT + per-run caches) before the
  timed runs, so timings are steady-state, not cold-start.
- **Median.** OUR timings are the median of 7 runs (5 for the compile/query engines)
  after the warm-up.
- **Identical timeout.** The competitor side uses one per-function wall-clock budget
  (default 10 s), applied identically to every function and every tool. A function
  that exceeds it is recorded as `timeout`, **never** as a failure or a fabricated
  number.

## One committed corpus, fixed seeds

- The single corpus is [`tools/comparison_corpus.txt`](../tools/comparison_corpus.txt),
  read verbatim by **both** the OUR-side harness and every competitor adapter. Its
  SHA-256 is recorded in every artifact (`corpus.sha256`) so a reader can prove both
  sides ran on the identical file.
- 17 Boolean functions, `<zone> | <name> | <expression>` (syntax `!`/`&`/`|`, no
  constants), in two zones: `small` (≤ 10 vars, our exact-guarantee zone) and `mid`
  (11–24 vars, our SAT-based prime-cover zone). Capped at 24 vars because SymPy/PyEDA
  build a 2ⁿ truth table.
- The OUR-side harness is fully deterministic given the corpus (no RNG). Where a
  competitor uses randomness (e.g. a portfolio SAT solver), it must be run with a
  fixed seed and the seed recorded.

## Separate tables per task — the "do not mix" rules

The four result sets are **separate tables**; the following must never be mixed
inside one comparison:

1. **Default multi-level output vs two-level SOP.** OUR `Optimized` is multi-level
   (factored); SymPy/PyEDA emit a two-level SOP. They are compared in *different*
   tables (result set 1 vs result set 2). The apples-to-apples two-level comparison
   uses OUR `result.DNF` only.
2. **Equivalent CNF vs equisatisfiable CNF.** The SAT table uses the *equisatisfiable*
   Tseitin CNF of the miter (auxiliary variables added). It is never compared against
   an *equivalent* (POS) CNF as if the two were the same encoding.
3. **Cold-start (JIT) vs warmed steady state.** All OUR timings are warmed. A cold
   first-call number is never placed in the same column as a warmed one.
4. **Different machines.** Milliseconds from two machines are never subtracted or
   ranked against each other; only same-machine timings are compared. Sizes / counts /
   verdicts *are* machine-independent and may be compared across machines.

## The four result sets and their fields

1. **Symbolic optimization** — input/output literal count, input/output AST node
   count, `MinimizationStatus`, **equivalence verdict**, time, allocated bytes.
2. **Two-level minimization** — terms, literals, proof status (`MinimizationStatus`),
   `abandoned` (a DNF the converter declined to expand), **equivalence verdict**, time.
3. **SAT** — verdict, conflicts, proof availability (DRAT), proof length, time.
   Measured on the miter `Original XOR Optimized`: **UNSAT ⇒ the optimization is
   equivalence-preserving**, so this table is *also* a SAT-based, independent
   correctness proof.
4. **BDD / d-DNNF** — compile time, node count, exact model count, repeated-query
   time, for both engines. `modelCountsAgree` is the exact cross-check: the two
   independent engines must return the identical #SAT count.

## Correctness is verified independently of performance

Every OUR optimization/minimization row carries an `equivalence` verdict computed by
the library's own `EquivalenceChecker` (truth-table ≤ 12 variables, SAT-miter beyond)
— **not** by re-using any timing path. The harness exits non-zero if any row is not
`equivalent`, so a correctness regression fails the run regardless of timing. The
BDD/d-DNNF table adds a second independent correctness signal: two different exact
engines must agree on the model count.

## Acceptance rules (the contract)

- **Every number in the comparison comes from a committed artifact.** OUR numbers
  live in `doc/comparison/our-results.json`; the Markdown summary only re-renders
  them. No hand-typed numbers.
- **Each table has an exact reproduce command** (below and in `summary.md`).
- **A competitor timeout is marked `timeout`, never a failure.**
- **Correctness is verified independently of performance** (see above).
- **Determinism.** Re-running the OUR harness yields byte-identical results for every
  non-timing field. Verify with:

  ```powershell
  dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite --out a
  dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite --out b
  # compare a/our-results.json and b/our-results.json ignoring *Ms* / allocatedBytes lines
  ```

## Reproduce

### OUR side (single command, committed here)

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite
# writes doc/comparison/{our-results.json, summary.md, manifest.json}
# add --emit-sat-dimacs doc/comparison/sat-cnf to also write the miter DIMACS
```

### Competitor side (one container, or a single adapter where the tool is installed)

The whole competitor side runs from one command:

```bash
docker build -t logicopt-p0p2 tools/comparison
docker run --rm -v "$PWD:/work" logicopt-p0p2   # writes doc/comparison/merged.md
```

Or run one adapter standalone where its tool is installed:

| Table | Command | Requires |
|-------|---------|----------|
| 1 & 2 (SymPy / PyEDA) | `python tools/compare_sympy_pyeda.py --max-vars 16 --timeout 20` | `pip install sympy pyeda` (POSIX for the timeout) |
| 3 (CaDiCaL / Kissat) | `tools/comparison/run_sat_competitors.sh <dimacs-dir>` | `cadical` / `kissat` on `PATH` |
| 3 (Z3) | `python tools/comparison/run_z3_competitor.py <dimacs-dir>` | `pip install z3-solver` |
| 4 (d4 #SAT) | `tools/comparison/run_modelcount_competitors.sh <func-dimacs-dir>` | `d4` on `PATH` |
| 4 (LogicNG BDD) | `java -jar logicng-adapter.jar tools/comparison_corpus.txt` | JVM + `tools/comparison/logicng` |

Each competitor adapter reads the **same** `tools/comparison_corpus.txt` (or the
DIMACS the OUR harness emits from it), applies the identical per-function timeout,
prints a Markdown column, and **self-skips** cleanly if the tool is absent — never
fabricating a number. The merge fills the competitor columns of `merged.md`; `summary.md`
keeps its `pending` competitor cells as the pure OUR-side artifact.

### Verify the reproduction (step 3)

Self-skipping is honest but it means the container **exits 0 even if every competitor skipped** —
so "it ran" is not the same as "it reproduced". Check the run instead of trusting it:

```bash
pwsh tools/verify_comparison_reproduction.ps1 -RequireCompetitors 3 \
     -CompareWith committed/our-results.json
```

It asserts that the corpus really is the committed one (by SHA-256, not by filename), that the
environment is recorded, that **every** optimization/minimization row is `equivalent`, that every
equivalence miter is `unsat`, that the BDD and d-DNNF counters agree on every model count, and that
enough competitor columns are actually populated — failing an all-`pending` report instead of
letting it look like a success. With `-CompareWith` it also enforces the determinism claim above:
every non-timing field must match a previously committed run. Timing is never asserted.

The output is `comparison-reproduction-report.json`, which records what it checked *and its own
limitations*. The `reproduce-from-scratch` job in
[`comparison.yml`](../.github/workflows/comparison.yml) runs exactly the three commands on this page
from a clean checkout, so the documented sequence is rehearsed rather than assumed — but a run on
this project's own runner is a rehearsal, not independent reproduction. That still needs someone
else's machine.

### If you reproduced it, please say so

An independent reproduction is the single piece of evidence this project cannot produce for itself.
If you ran the three commands above — **including if they failed** — please
[open an issue](https://github.com/AlexanderV/LogicalOptimizer/issues/new) and attach
`comparison-reproduction-report.json`. It already contains everything needed: the corpus checksum,
your runtime and OS, which competitor columns populated, and every check's verdict. A report that
the sequence *did not* work is more useful than silence, because it names the thing to fix.
