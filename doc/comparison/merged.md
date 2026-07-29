# LogicalOptimizer vs. other libraries

_Automated cross-library comparison. One committed corpus of **17 Boolean functions**, run once in a single controlled Linux container ([`tools/comparison/`](../../tools/comparison/)). **OUR** = LogicalOptimizer; every competitor number comes from a pinned external tool. Sizes, counts and verdicts are deterministic; timings are indicative and not compared._

Corpus fingerprint: `sha256 5964fdf799e9c4b4…` (17 functions).

## TL;DR — what this shows

- **Exact model counting is correct.** OUR `#SAT` equals **d4** and **LogicNG** on **17/17** of the functions all three counted — three independent engines agree on the exact number of satisfying assignments.
- **Every optimization preserves equivalence.** every equivalence miter is **UNSAT** under OUR solver plus CaDiCaL, Kissat, Z3 — independent SAT solvers confirm the optimized formula is logically identical to the original.
- **Compact output.** OUR multi-level result has **no more literals** than the best of SymPy/PyEDA on **12/12** comparable functions (strictly fewer on **4**).
- **Two-level parity.** Where SymPy/PyEDA finish, OUR two-level SOP matches their literal count (Table 2).

## How to read the tables

- **Different competitors appear in different tables** — each external tool is compared only where it applies (they are different kinds of tools): **SymPy / PyEDA** on result size (Tables 1–2), **CaDiCaL / Kissat / Z3** on SAT (Table 3), **d4 / LogicNG** on model counting (Table 4). No single table lists all seven.
- Each **row is a corpus function**. The families: `maj*` = majority, `xor*` = parity, `mux*` = multiplexer, `eq*`/`consensus*` = equality/consensus, `pairs*`/`chain*`/`collapse*`/`pos*` = structured scaling families (the trailing number is roughly the variable count).
- **OUR** columns are from the committed `our-results.json`; competitor columns are each tool's own output.
- Cell legend: **`pending`** = tool not run · **`timeout`** = exceeded the shared per-function budget · **`skipped(max-vars)`** = beyond a truth-table tool's 2ⁿ budget (OUR still handles it).

## 1. Result size — multi-level (fewer literals is better)

OUR emits **factored multi-level** output; SymPy/PyEDA emit two-level DNF/SOP, so on structured functions OUR can be much smaller. The last column flags OUR vs. the best competitor.

| Function | OUR out lits | SymPy | PyEDA | OUR vs best |
|----------|-------------:|:-----:|:-----:|:-----------:|
| maj3 | 5 | 6 | 6 | ✓ fewer |
| consensus3 | 4 | 4 | 4 | = equal |
| xor2 | 4 | 4 | 4 | = equal |
| xor3 | 10 | 12 | 12 | ✓ fewer |
| maj4 | 8 | 12 | 12 | ✓ fewer |
| mux2 | 4 | 4 | 4 | = equal |
| pos6 | 6 | 24 | 24 | ✓ fewer |
| eq5 | 10 | 10 | 10 | = equal |
| pairs8 | 8 | 8 | 8 | = equal |
| pairs10 | 10 | 10 | 10 | = equal |
| pairs12 | 12 | timeout | 12 | = equal |
| collapse14 | 7 | timeout | 7 | = equal |
| pairs16 | 16 | timeout | timeout |  |
| collapse18 | 9 | skipped(max-vars) | skipped(max-vars) |  |
| pairs20 | 20 | skipped(max-vars) | skipped(max-vars) |  |
| chain22 | 40 | skipped(max-vars) | skipped(max-vars) |  |
| pairs24 | 24 | skipped(max-vars) | skipped(max-vars) |  |

**What it means:** on comparable functions OUR output is at least as small as both tools (12/12) and strictly smaller on 4 — factoring pays off on structured logic (e.g. `pos6`). Larger functions are `skipped` by the 2ⁿ truth-table tools but handled by OUR engine.

## 2. Result size — two-level SOP (apples-to-apples)

Here OUR `result.DNF` is the **same kind** of two-level form SymPy `simplify_logic` and PyEDA `espresso` produce, so equal literal counts are the expected, correct outcome.

| Function | OUR DNF lits | SymPy | PyEDA | match |
|----------|-------------:|:-----:|:-----:|:-----:|
| maj3 | 6 | 6 | 6 | ✅ |
| consensus3 | 4 | 4 | 4 | ✅ |
| xor2 | 4 | 4 | 4 | ✅ |
| xor3 | 12 | 12 | 12 | ✅ |
| maj4 | 12 | 12 | 12 | ✅ |
| mux2 | 4 | 4 | 4 | ✅ |
| pos6 | 24 | 24 | 24 | ✅ |
| eq5 | 10 | 10 | 10 | ✅ |
| pairs8 | 8 | 8 | 8 | ✅ |
| pairs10 | 10 | 10 | 10 | ✅ |
| pairs12 | 12 | timeout | 12 | ✅ |
| collapse14 | 7 | timeout | 7 | ✅ |
| pairs16 | 16 | timeout | timeout |  |
| collapse18 | 9 | skipped(max-vars) | skipped(max-vars) |  |
| pairs20 | 20 | skipped(max-vars) | skipped(max-vars) |  |
| chain22 | 60 | skipped(max-vars) | skipped(max-vars) |  |
| pairs24 | 24 | skipped(max-vars) | skipped(max-vars) |  |

**What it means:** where all three finish, the literal counts match — OUR two-level minimizer reaches the same optimum as SymPy's Quine–McCluskey and PyEDA's Espresso.

## 3. Equivalence check via SAT (every miter should be UNSAT)

Each optimization is checked by solving the miter `original XOR optimized`: **UNSAT ⇒ the two formulas are logically identical**, i.e. the optimization changed nothing about the function. OUR solver and the external ones should all agree on `unsat`.

| Function | OUR | conflicts | CaDiCaL | Kissat | Z3 |
|----------|:---:|----------:|:-------:|:------:|:--:|
| maj3 | unsat | 5 | unsat | unsat | unsat |
| consensus3 | unsat | 4 | unsat | unsat | unsat |
| xor2 | unsat | 0 | unsat | unsat | unsat |
| xor3 | unsat | 9 | unsat | unsat | unsat |
| maj4 | unsat | 7 | unsat | unsat | unsat |
| mux2 | unsat | 0 | unsat | unsat | unsat |
| pos6 | unsat | 1 | unsat | unsat | unsat |
| eq5 | unsat | 0 | unsat | unsat | unsat |
| pairs8 | unsat | 0 | unsat | unsat | unsat |
| pairs10 | unsat | 0 | unsat | unsat | unsat |
| pairs12 | unsat | 0 | unsat | unsat | unsat |
| collapse14 | unsat | 8 | unsat | unsat | unsat |
| pairs16 | unsat | 0 | unsat | unsat | unsat |
| collapse18 | unsat | 10 | unsat | unsat | unsat |
| pairs20 | unsat | 0 | unsat | unsat | unsat |
| chain22 | unsat | 66 | unsat | unsat | unsat |
| pairs24 | unsat | 0 | unsat | unsat | unsat |

**What it means:** `unsat` across every column is the goal — multiple independent solvers confirm each optimization preserves the function exactly.

## 4. Exact model counting — #SAT (every count should match)

The number of satisfying assignments, computed three independent ways: OUR BDD/d-DNNF, the **d4** exact counter (on the count-preserving per-function CNF), and **LogicNG**'s BDD. Identical numbers cross-validate OUR exact counter. _(c2d is proprietary and not in the container, so only d4 runs.)_

| Function | OUR #SAT | d4 | LogicNG | match | LogicNG BDD nodes |
|----------|---------:|:--:|:-------:|:-----:|------------------:|
| maj3 | 4 | 4 | 4 | ✅ | 4 |
| consensus3 | 4 | 4 | 4 | ✅ | 3 |
| xor2 | 2 | 2 | 2 | ✅ | 3 |
| xor3 | 4 | 4 | 4 | ✅ | 5 |
| maj4 | 5 | 5 | 5 | ✅ | 6 |
| mux2 | 4 | 4 | 4 | ✅ | 5 |
| pos6 | 27 | 27 | 27 | ✅ | 6 |
| eq5 | 2 | 2 | 2 | ✅ | 9 |
| pairs8 | 175 | 175 | 175 | ✅ | 8 |
| pairs10 | 781 | 781 | 781 | ✅ | 10 |
| pairs12 | 3367 | 3367 | 3367 | ✅ | 12 |
| collapse14 | 16256 | 16256 | 16256 | ✅ | 7 |
| pairs16 | 58975 | 58975 | 58975 | ✅ | 16 |
| collapse18 | 261632 | 261632 | 261632 | ✅ | 9 |
| pairs20 | 989527 | 989527 | 989527 | ✅ | 20 |
| chain22 | 3438828 | 3438828 | 3438828 | ✅ | 60 |
| pairs24 | 16245775 | 16245775 | 16245775 | ✅ | 24 |

**What it means:** OUR = d4 = LogicNG on 17/17 functions — the exact model count is confirmed by two external engines.

---

_Reproduce: `docker build -t logicopt-p0p2 tools/comparison && docker run --rm -v "$PWD:/work" logicopt-p0p2`. Environment in [`manifest.json`](manifest.json); method in [`doc/COMPARISON_METHODOLOGY.md`](../COMPARISON_METHODOLOGY.md)._
