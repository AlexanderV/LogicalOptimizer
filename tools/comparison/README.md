# Competitor-integration scaffolding (roadmap P0.2)

These adapters produce the **competitor columns** of the cross-library comparison
from the **same** committed corpus the OUR-side harness uses. They **never fabricate
numbers** — each one self-skips cleanly when its external tool is absent, exactly like
the OUR harness leaves competitor cells `pending`, and records a `timeout` when the
shared budget is exceeded.

See [`doc/COMPARISON_METHODOLOGY.md`](../../doc/COMPARISON_METHODOLOGY.md) for the
single-runner / one-CPU / identical-timeout / do-not-mix rules that every adapter
must respect, and [`doc/comparison/summary.md`](../../doc/comparison/summary.md) for
the OUR column the competitor columns merge into.

## One controlled environment (recommended): the container

The methodology requires a **single Linux runner** for every tool. [`Dockerfile`](Dockerfile)
bundles the OUR side (.NET SDK) with every competitor — SymPy, PyEDA, CaDiCaL, Kissat, Z3, d4
and LogicNG — and [`run_all_in_container.sh`](run_all_in_container.sh) runs the whole
comparison from the one committed corpus and writes the artifacts back under
`doc/comparison/` (including the merged [`merged.md`](../../doc/comparison/merged.md) and a
self-contained, auto-generated [`merged.html`](../../doc/comparison/merged.html) you can open
in a browser — it renders itself from the same run's data, so it never drifts). Every
competitor is **version-pinned** for reproducibility (base image by digest, Python tools by
exact version, SAT/#SAT solvers by commit hash, LogicNG by release):

```bash
docker build -t logicopt-p0p2 tools/comparison
docker run --rm -v "$PWD:/work" logicopt-p0p2
```

Every competitor is installed **best-effort**: a tool whose build fails is simply absent
and its column self-skips — the image still builds and the run still produces every other
column. The adapters below are what the container invokes; run them standalone where a
given tool is already installed.

## Shared inputs

- **Corpus:** [`tools/comparison_corpus.txt`](../comparison_corpus.txt) — one function
  per line, `<zone> | <name> | <expression>`, syntax `!`/`&`/`|`.
- **SAT / #SAT DIMACS:** produced deterministically from the corpus by the OUR harness:

  ```powershell
  dotnet run -c Release --project LogicalOptimizer.Benchmarks -- \
    comparison-suite --emit-sat-dimacs doc/comparison/sat-cnf \
                     --emit-function-dimacs doc/comparison/func-cnf
  ```

  `--emit-sat-dimacs` writes one `<name>.miter.cnf` per function (the miter
  `Original XOR Optimized`, UNSAT ⇒ equivalence-preserving) — the SAT instances CaDiCaL /
  Kissat solve. `--emit-function-dimacs` writes one `<name>.cnf` per function: the function's
  own **count-preserving** full-Tseitin CNF (every auxiliary variable is functionally
  determined, so its model count over all DIMACS variables equals the OUR `modelCount`) —
  the #SAT instances d4 / c2d count for a head-to-head against the BDD / d-DNNF table. Both
  directories are regenerable, so they are **not** committed.

## Adapters

| Table | Adapter | External tool | Fills |
|-------|---------|---------------|-------|
| 1 & 2 | [`../compare_sympy_pyeda.py`](../compare_sympy_pyeda.py) | SymPy, PyEDA (`pip`) | `SymPy lits`, `PyEDA lits` |
| 3 | [`run_sat_competitors.sh`](run_sat_competitors.sh) | CaDiCaL / Kissat (`PATH`) | `CaDiCaL`, `Kissat` |
| 3 | [`run_z3_competitor.py`](run_z3_competitor.py) | Z3 (`pip z3-solver`) | `Z3` |
| 4 (#SAT) | [`run_modelcount_competitors.sh`](run_modelcount_competitors.sh) | d4 / c2d (`PATH`) | `d4 #SAT` |
| 2, 3 & 4 | [`logicng/`](logicng/) (Maven/Java, LogicNG 2.4.1) | JDK + Maven | `LogicNG #SAT`, `LogicNG nodes`, `LogicNG SAT verdict`, `LogicNG min lits` |

### 1 & 2 — SymPy / PyEDA (already present)

```bash
python tools/compare_sympy_pyeda.py --max-vars 14 --timeout 10
```

Reads the corpus, runs SymPy `simplify_logic(form="dnf")` and PyEDA `espresso_tts`,
prints a Markdown column of literal count + ms per function, `timeout` for functions
that exceed the budget, and self-skips a tool that is not importable. POSIX only for
the per-function timeout (SIGALRM).

### 3 — CaDiCaL / Kissat (SAT)

```bash
tools/comparison/run_sat_competitors.sh doc/comparison/sat-cnf
```

Runs each `*.miter.cnf` through `cadical` and `kissat` under the identical timeout,
parses the `s SATISFIABLE` / `s UNSATISFIABLE` line, and prints verdict + time per
function. Every miter is expected UNSAT (⇒ equivalence-preserving); a `sat` here would
signal a real bug, not just a competitor difference. A run that exceeds the timeout is
recorded `timeout`.

### 3 — Z3 (SAT)

```bash
python tools/comparison/run_z3_competitor.py doc/comparison/sat-cnf 20
```

Solves each `*.miter.cnf` with Z3's Python bindings under the shared timeout and prints a
`Z3 verdict` + `Z3 ms` column. Same expectation as CaDiCaL / Kissat: every miter is UNSAT
(⇒ equivalence-preserving). Self-skips when the `z3` module is absent; a solve past the
budget is `timeout` (Z3 `unknown`), never a failure.

### 4 — c2d / d4 (#SAT model counting)

```bash
tools/comparison/run_modelcount_competitors.sh doc/comparison/func-cnf
```

Point it at the **`func-cnf`** directory (`--emit-function-dimacs`), not the miters: the
miters are all UNSAT (count 0), whereas the per-function CNFs are the count-preserving
full-Tseitin encoding, so d4 / c2d's count is directly comparable to `our-results.json`'s
`modelCount`. Count-preservation is proven exhaustively for the small corpus functions
(brute-force `#SAT` of the emitted CNF equals the OUR `modelCount`). It self-skips when
the counter is absent and records `timeout` when the budget is exceeded.

### 2, 3 & 4 — LogicNG (JVM): BDD #SAT, miter SAT, min-DNF size

LogicNG is a JVM library, so its adapter is a small self-contained Maven/Java project in
[`logicng/`](logicng/) (pinned to LogicNG 2.4.1). `mvn package` shades it and its
dependencies into one executable jar; the container bakes that jar in and runs:

```bash
java -jar logicng-adapter.jar tools/comparison_corpus.txt 20 doc/comparison/sat-cnf
# <corpus> [timeout-seconds] [miter-dimacs-dir]
```

It reads the shared corpus and prints one row per function with three independent
LogicNG measurements, each under the shared per-function timeout (over budget is
`timeout`, a parse/build error is `error` — never a fabricated number):

- `LogicNG nodes` / `LogicNG #SAT` / `LogicNG ms` — the function compiled to a BDD;
  its exact model count is an independent cross-check of the OUR count (Table 4).
- `LogicNG SAT verdict` / `LogicNG SAT ms` — LogicNG's MiniSat on the **same**
  `<name>.miter.cnf` DIMACS CaDiCaL / Kissat / Z3 solve (Table 3; every miter is
  expected `unsat`). When the miter directory is omitted or a file is missing, the
  cells self-skip to `pending`.
- `LogicNG min lits` / `LogicNG min ms` — the literal count of LogicNG's **minimum
  prime-implicant-cover DNF** of the function (the documented DNF-building core of its
  `AdvancedSimplifier`: prime implicants + smallest-MUS coverage). This is a two-level
  DNF, so the literal count is like-for-like with Table 2's `OUR DNF lits` / SymPy /
  PyEDA columns. The full `AdvancedSimplifier` is deliberately not used for this
  column: its goal differs (it may factor the result or keep the input form when that
  rates smaller), which would not be a two-level size.

## Merging

Each adapter prints a Markdown column keyed by function `name`. To fill the summary,
replace the matching `pending` cell for that function/tool with the printed value (or
`timeout`); no other field in the row changes. The OUR columns are never edited — they
come only from `doc/comparison/our-results.json`. A tiny merge helper is provided:

```bash
python tools/comparison/merge_results.py doc/comparison/our-results.json \
  --sympy sympy_out.md --sat sat_out.md --z3 z3_out.md \
  --modelcount mc_out.md --logicng logicng_out.md   # any subset; missing inputs stay `pending`
```

The container runs this for you and writes [`doc/comparison/merged.md`](../../doc/comparison/merged.md).
