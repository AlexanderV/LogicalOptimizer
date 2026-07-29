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

Only the **LogicalOptimizer ("OUR") column** is produced here and committed. The
competitor tools (LogicNG on the JVM, Z3, SymPy, PyEDA, CaDiCaL / Kissat, c2d / d4)
are **not** run as part of this deliverable and **no competitor numbers are
fabricated**. Every competitor cell in the summary is `pending (run <command>)`; the
exact command that fills it is documented below and in `tools/comparison/`.

This is deliberate: OUR side is locally verifiable and reproducible; the competitor
side must be produced on a machine where the external tool is installed (e.g. CI),
against the *same committed corpus*, and merged back under the same controls.

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

### Competitor side (pending — run where the tool is installed)

| Table | Command | Requires |
|-------|---------|----------|
| 1 & 2 (SymPy / PyEDA) | `python tools/compare_sympy_pyeda.py --max-vars 14 --timeout 10` | `pip install sympy pyeda` (POSIX for the timeout) |
| 3 (CaDiCaL / Kissat) | `tools/comparison/run_sat_competitors.sh <dimacs-dir>` | `cadical` / `kissat` on `PATH` |
| 4 (c2d / d4 #SAT) | `tools/comparison/run_modelcount_competitors.sh <dimacs-dir>` | `d4` / `c2d` on `PATH` |
| 4 (LogicNG BDD) | see `tools/comparison/README.md` | JVM + LogicNG |

Each competitor adapter reads the **same** `tools/comparison_corpus.txt` (or the
DIMACS the OUR harness emits from it), applies the identical per-function timeout,
prints a Markdown column, and **self-skips** cleanly if the tool is absent — never
fabricating a number. Merging the competitor column into `summary.md` replaces the
matching `pending` cell; nothing else in the row changes.
