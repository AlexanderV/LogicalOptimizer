# LogicalOptimizer — cross-library comparison, OUR-side detail

This is the **detailed LogicalOptimizer ("OUR") side** of the P0.2 comparison — each
engine's own sizes, statuses, exact model counts and (indicative) timings on the shared
corpus. This file has **no competitor columns**; the head-to-head against other libraries
lives in the digested [`merged.md`](merged.md), where each tool is compared **only where
it applies** (they are different kinds of tools): SymPy / PyEDA on result size
(Tables 1–2), CaDiCaL / Kissat / Z3 on SAT (Table 3), and d4 / LogicNG on model counting
(Table 4).

Corpus: `tools/comparison_corpus.txt` (`sha256 5964fdf799e9c4b4…`, 17 functions). 
Environment: .NET 10.0.10, Ubuntu 24.04.4 LTS, X64.

**Deterministic** (identical on any machine, safe to rely on): sizes, node/term counts,
statuses, verdicts, model counts. **Indicative** (machine-dependent, never asserted):
every `… (ms)` and the `Alloc` column — median of 7 runs after 1 warm-up.

Reproduce: `dotnet run -c Release --project LogicalOptimizer.Benchmarks -- comparison-suite`.

## 1. Symbolic optimization — default multi-level output

What the optimizer produces by default. **In/Out lits** and **In/Out nodes** are the
formula size before and after (fewer = simpler result). **Status** is the minimization
guarantee; **Equivalence** is proven independently of timing by `EquivalenceChecker`
(truth-table ≤ 12 vars, SAT-miter beyond) and must read `equivalent` on every row.

| Zone | Function | Vars | In lits | Out lits | In nodes | Out nodes | Status | Equivalence | Time (ms) | Alloc (B) |
|------|----------|-----:|--------:|---------:|---------:|----------:|--------|-------------|----------:|----------:|
| small | maj3 | 3 | 6 | 5 | 10 | 9 | MinimalProven | equivalent | 1.073 | 223272 |
| small | consensus3 | 3 | 6 | 4 | 11 | 8 | MinimalProven | equivalent | 0.762 | 164848 |
| small | xor2 | 2 | 4 | 4 | 9 | 9 | MinimalProven | equivalent | 0.206 | 93904 |
| small | xor3 | 3 | 12 | 10 | 23 | 21 | MinimalProven | equivalent | 3.738 | 750488 |
| small | maj4 | 4 | 12 | 8 | 17 | 13 | MinimalProven | equivalent | 15.868 | 734008 |
| small | mux2 | 3 | 4 | 4 | 8 | 8 | MinimalProven | equivalent | 1.160 | 116976 |
| small | pos6 | 6 | 6 | 6 | 10 | 10 | MinimalProven | equivalent | 2.437 | 757136 |
| small | eq5 | 5 | 10 | 10 | 18 | 18 | MinimalProven | equivalent | 1.003 | 319344 |
| small | pairs8 | 8 | 8 | 8 | 13 | 13 | MinimalProven | equivalent | 5.232 | 1159928 |
| small | pairs10 | 10 | 10 | 10 | 16 | 16 | MinimalProven | equivalent | 17.634 | 6303144 |
| mid | pairs12 | 12 | 12 | 12 | 19 | 19 | BudgetExceeded | equivalent | 51.815 | 16892448 |
| mid | collapse14 | 14 | 28 | 7 | 50 | 8 | Heuristic | equivalent | 1.703 | 631592 |
| mid | pairs16 | 16 | 16 | 16 | 25 | 25 | Heuristic | equivalent | 5.674 | 1858336 |
| mid | collapse18 | 18 | 36 | 9 | 64 | 10 | Heuristic | equivalent | 2.647 | 1051680 |
| mid | pairs20 | 20 | 20 | 20 | 31 | 31 | Heuristic | equivalent | 6.036 | 2900056 |
| mid | chain22 | 22 | 60 | 40 | 81 | 73 | Heuristic | equivalent | 58.346 | 26888768 |
| mid | pairs24 | 24 | 24 | 24 | 37 | 37 | Heuristic | equivalent | 3.462 | 3456736 |

## 2. Two-level (SOP) minimization

OUR `result.DNF` — the two-level sum-of-products form (exact QM / SAT-cover /
espresso-lite). **Terms** = products, **Literals** = total literals (both smaller is
better). **Abandoned** = `yes` when the converter declined to expand a DNF (reported,
never guessed). This is the same kind of form SymPy / PyEDA emit — compared head-to-head
in [`merged.md`](merged.md).

| Zone | Function | Vars | Terms | Literals | Proof status | Equivalence | Abandoned | Time (ms) |
|------|----------|-----:|------:|---------:|--------------|-------------|:---------:|----------:|
| small | maj3 | 3 | 3 | 6 | MinimalProven | equivalent | no | 1.842 |
| small | consensus3 | 3 | 2 | 4 | MinimalProven | equivalent | no | 0.390 |
| small | xor2 | 2 | 2 | 4 | MinimalProven | equivalent | no | 0.182 |
| small | xor3 | 3 | 4 | 12 | MinimalProven | equivalent | no | 4.251 |
| small | maj4 | 4 | 4 | 12 | MinimalProven | equivalent | no | 11.890 |
| small | mux2 | 3 | 2 | 4 | MinimalProven | equivalent | no | 0.279 |
| small | pos6 | 6 | 8 | 24 | MinimalProven | equivalent | no | 2.409 |
| small | eq5 | 5 | 2 | 10 | MinimalProven | equivalent | no | 1.215 |
| small | pairs8 | 8 | 4 | 8 | MinimalProven | equivalent | no | 3.013 |
| small | pairs10 | 10 | 5 | 10 | MinimalProven | equivalent | no | 14.853 |
| mid | pairs12 | 12 | 6 | 12 | BudgetExceeded | equivalent | no | 44.419 |
| mid | collapse14 | 14 | 7 | 7 | Heuristic | equivalent | no | 1.783 |
| mid | pairs16 | 16 | 8 | 16 | Heuristic | equivalent | no | 5.068 |
| mid | collapse18 | 18 | 9 | 9 | Heuristic | equivalent | no | 2.536 |
| mid | pairs20 | 20 | 10 | 20 | Heuristic | equivalent | no | 5.847 |
| mid | chain22 | 22 | 20 | 60 | Heuristic | equivalent | no | 26.395 |
| mid | pairs24 | 24 | 12 | 24 | Heuristic | equivalent | no | 3.551 |

## 3. SAT — equivalence proof (miter `original XOR optimized`)

Each optimization is proven correct by SAT: **Verdict `unsat` ⇒ the optimized formula is
logically identical to the original** (nothing was broken). **Conflicts** is the solver's
search effort; **Proof** = a DRAT certificate was emitted (checkable by drat-trim), with
**Proof lines** its size. External SAT solvers on the same miters are in [`merged.md`](merged.md).

| Zone | Function | Miter vars | Clauses | Verdict | Conflicts | Proof | Proof lines | Time (ms) |
|------|----------|-----------:|--------:|:-------:|----------:|:-----:|------------:|----------:|
| small | maj3 | 13 | 32 | unsat | 5 | yes | 9 | 0.069 |
| small | consensus3 | 11 | 26 | unsat | 4 | yes | 8 | 0.036 |
| small | xor2 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| small | xor3 | 17 | 51 | unsat | 9 | yes | 13 | 0.116 |
| small | maj4 | 17 | 48 | unsat | 7 | yes | 11 | 0.119 |
| small | mux2 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| small | pos6 | 11 | 19 | unsat | 1 | yes | 10 | 0.023 |
| small | eq5 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| small | pairs8 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| small | pairs10 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| mid | pairs12 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| mid | collapse14 | 33 | 75 | unsat | 8 | yes | 12 | 0.202 |
| mid | pairs16 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| mid | collapse18 | 41 | 93 | unsat | 10 | yes | 14 | 0.235 |
| mid | pairs20 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |
| mid | chain22 | 79 | 216 | unsat | 66 | yes | 70 | 0.559 |
| mid | pairs24 | 1 | 2 | unsat | 0 | yes | 1 | 0.000 |

## 4. BDD / d-DNNF — exact model counting

Both engines compile the function and count its satisfying assignments (**Model count** =
#SAT). **Agree** must be `yes`: the two independent engines' counts are identical — an
exact correctness cross-check independent of any timing. **Nodes** is the compiled size;
**compile/query (ms)** are indicative. d4 and LogicNG confirm these counts in [`merged.md`](merged.md).

| Zone | Function | Vars | BDD nodes | d-DNNF nodes | Model count | Agree | BDD compile (ms) | d-DNNF compile (ms) | BDD query (ms) | d-DNNF query (ms) |
|------|----------|-----:|----------:|-------------:|------------:|:-----:|-----------------:|--------------------:|---------------:|------------------:|
| small | maj3 | 3 | 10 | 20 | 4 | yes | 0.011 | 0.101 | 0.001 | 0.001 |
| small | consensus3 | 3 | 9 | 19 | 4 | yes | 0.008 | 0.057 | 0.001 | 0.001 |
| small | xor2 | 2 | 6 | 13 | 2 | yes | 0.005 | 0.017 | 0.001 | 0.000 |
| small | xor3 | 3 | 20 | 23 | 4 | yes | 0.015 | 0.052 | 0.001 | 0.001 |
| small | maj4 | 4 | 20 | 25 | 5 | yes | 0.017 | 0.089 | 0.003 | 0.002 |
| small | mux2 | 3 | 8 | 15 | 4 | yes | 0.009 | 0.039 | 0.001 | 0.001 |
| small | pos6 | 6 | 16 | 16 | 27 | yes | 0.012 | 0.173 | 0.003 | 0.001 |
| small | eq5 | 5 | 27 | 19 | 2 | yes | 0.019 | 0.052 | 0.003 | 0.001 |
| small | pairs8 | 8 | 25 | 40 | 175 | yes | 0.062 | 0.303 | 0.003 | 0.002 |
| small | pairs10 | 10 | 36 | 50 | 781 | yes | 0.025 | 0.360 | 0.003 | 0.002 |
| mid | pairs12 | 12 | 49 | 60 | 3367 | yes | 0.036 | 0.386 | 0.008 | 0.004 |
| mid | collapse14 | 14 | 416 | 673 | 16256 | yes | 0.339 | 11.331 | 0.009 | 0.109 |
| mid | pairs16 | 16 | 81 | 80 | 58975 | yes | 0.057 | 0.343 | 0.010 | 0.006 |
| mid | collapse18 | 18 | 1587 | 2603 | 261632 | yes | 1.390 | 49.912 | 0.012 | 0.329 |
| mid | pairs20 | 20 | 121 | 100 | 989527 | yes | 0.067 | 0.399 | 0.012 | 0.006 |
| mid | chain22 | 22 | 578 | 2572 | 3438828 | yes | 0.358 | 31.274 | 0.032 | 0.251 |
| mid | pairs24 | 24 | 169 | 120 | 16245775 | yes | 0.084 | 0.440 | 0.015 | 0.007 |

---

_One committed corpus, one run. Competitor comparison: [`merged.md`](merged.md); 
method: [`doc/COMPARISON_METHODOLOGY.md`](../COMPARISON_METHODOLOGY.md)._
