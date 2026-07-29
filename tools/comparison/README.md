# Competitor-integration scaffolding (roadmap P0.2)

These adapters produce the **competitor columns** of the cross-library comparison
from the **same** committed corpus the OUR-side harness uses. They are documented and
committed here, but **not executed** as part of the OUR-side deliverable and they
**never fabricate numbers** — each one self-skips cleanly when its external tool is
absent, exactly like the OUR harness leaves competitor cells `pending`.

See [`doc/COMPARISON_METHODOLOGY.md`](../../doc/COMPARISON_METHODOLOGY.md) for the
single-runner / one-CPU / identical-timeout / do-not-mix rules that every adapter
must respect, and [`doc/comparison/summary.md`](../../doc/comparison/summary.md) for
the OUR column the competitor columns merge into.

## Shared inputs

- **Corpus:** [`tools/comparison_corpus.txt`](../comparison_corpus.txt) — one function
  per line, `<zone> | <name> | <expression>`, syntax `!`/`&`/`|`.
- **SAT / #SAT DIMACS:** produced deterministically from the corpus by the OUR harness:

  ```powershell
  dotnet run -c Release --project LogicalOptimizer.Benchmarks -- \
    comparison-suite --emit-sat-dimacs doc/comparison/sat-cnf
  ```

  This writes one `<name>.miter.cnf` per function (the miter `Original XOR Optimized`,
  UNSAT ⇒ equivalence-preserving). These are the SAT instances CaDiCaL / Kissat solve
  and the model-counters count. They are regenerable, so they are **not** committed.

## Adapters

| Table | Adapter | External tool | Fills |
|-------|---------|---------------|-------|
| 1 & 2 | [`../compare_sympy_pyeda.py`](../compare_sympy_pyeda.py) | SymPy, PyEDA (`pip`) | `SymPy lits`, `PyEDA lits` |
| 3 | [`run_sat_competitors.sh`](run_sat_competitors.sh) | CaDiCaL / Kissat (`PATH`) | `CaDiCaL`, `Kissat` |
| 4 (#SAT) | [`run_modelcount_competitors.sh`](run_modelcount_competitors.sh) | d4 / c2d (`PATH`) | `c2d/d4 #SAT` |
| 4 (BDD) | LogicNG (JVM) — documented below | LogicNG | `LogicNG BDD` |

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

### 4 — c2d / d4 (#SAT model counting)

```bash
tools/comparison/run_modelcount_competitors.sh doc/comparison/sat-cnf
```

**Note on semantics:** the emitted DIMACS are the *equivalence miters* (all UNSAT, so
their model count is 0) — useful for the SAT table but **not** the same function whose
models the BDD/d-DNNF table counts. To compare #SAT head-to-head against c2d / d4,
emit a *function* CNF (not the miter) for each corpus line and count that; this adapter
shows the invocation and parsing, and documents that the function-CNF emitter is the
change required to make the counts directly comparable to `our-results.json`'s
`modelCount`. It self-skips when the counter is absent.

### 4 — LogicNG BDD (JVM)

LogicNG is a JVM library, so its adapter is a small Java/Kotlin program rather than a
shell script. Documented invocation:

```java
// build.gradle: implementation 'org.logicng:logicng:2.4.1'
FormulaFactory f = new FormulaFactory();
PropositionalParser p = new PropositionalParser(f);
Formula formula = p.parse(expression.replace("!", "~").replace("&", " & ").replace("|", " | "));
BDDFactory bdd = new BDDFactory(100000, 100000, f);
BDD compiled = formula.bdd(bdd);
long nodeCount = compiled.nodeCount();
BigInteger modelCount = compiled.modelCount();
```

Print `<name> | <nodeCount> | <modelCount> | <ms>` per corpus line, apply the shared
timeout, and skip cleanly if LogicNG is not on the classpath.

## Merging

Each adapter prints a Markdown column keyed by function `name`. To fill the summary,
replace the matching `pending` cell for that function/tool with the printed value (or
`timeout`); no other field in the row changes. The OUR columns are never edited — they
come only from `doc/comparison/our-results.json`. A tiny merge helper is provided:

```bash
python tools/comparison/merge_results.py doc/comparison/our-results.json \
  --sat sat_out.md --sympy sympy_out.md   # any subset; missing inputs stay `pending`
```
