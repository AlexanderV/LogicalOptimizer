# Benchmarks & Comparison

LogicalOptimizer is a **dependency-free managed .NET** Boolean toolkit. This page
summarizes how its result quality and speed compare to two widely-used two-level
minimizers — Python's [SymPy](https://www.sympy.org) (`simplify_logic`) and
[PyEDA](https://pyeda.readthedocs.io) (Espresso) — on a shared corpus of Boolean
functions. The full methodology, the machine-readable BenchmarkDotNet suites and
the SAT-corpus perf-regression live in
[`doc/BENCHMARKS.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/doc/BENCHMARKS.md).

## Head-to-head: result size (literals)

Both sides read the **same** committed corpus
([`tools/comparison_corpus.txt`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/tools/comparison_corpus.txt)).
Literal count — the number of variable occurrences in the minimized expression —
is machine-independent, so it is the real comparison. Bold = LogicalOptimizer
strictly smaller. Competitor numbers are from the Linux CI run
(sympy 1.14.0, pyeda 0.29.0).

| Function | Vars | LogicalOptimizer | SymPy | PyEDA |
|----------|-----:|:----------------:|:-----:|:-----:|
| maj3 | 3 | **5** | 6 | 6 |
| consensus3 | 3 | 4 | 4 | 4 |
| xor2 | 2 | 4 | 4 | 4 |
| xor3 | 3 | **10** | 12 | 12 |
| maj4 | 4 | **9** | 12 | 12 |
| mux2 | 3 | 4 | 4 | 4 |
| pos6 | 6 | **6** | 24 | 24 |
| eq5 | 5 | 10 | 10 | 10 |
| pairs8 | 8 | 8 | 8 | 8 |
| pairs10 | 10 | 10 | `timeout` | 10 |
| pairs12 | 12 | 12 | `timeout` | 12 |
| collapse14 | 14 | **7** | `timeout` | 7 |

## What the numbers say

**Result size — never larger, often smaller.** On every function LogicalOptimizer
matches or beats the two-level minimizers: `maj3` 5 vs 6, `xor3` 10 vs 12, `maj4`
9 vs 12, and `pos6` **6 vs 24**. The reason is a genuine capability difference —
`OptimizeExpression` returns a **multi-level (factored)** form, while SymPy and
PyEDA return a **two-level SOP**. `pos6` is the clearest case: a 6-literal
product-of-sums stays 6 literals for us but expands to a 24-literal DNF for a
two-level tool. That advantage is real, but it is not a like-for-like SOP proof —
for that, see the apples-to-apples two-level table below.

## Head-to-head: two-level SOP result size (apples-to-apples, `--dnf`)

To compare **like for like**, run our two-level SOP path (`compare --dnf`) and count
`result.DNF` — the exact QM / SAT-cover / espresso-lite cover, the same two-level
form SymPy and PyEDA emit. OUR column is the real `--dnf` literal count; the
SymPy/PyEDA columns are the same Linux-CI numbers as the table above. Bold =
LogicalOptimizer strictly smaller.

| Function | Vars | LogicalOptimizer (`--dnf`) | SymPy | PyEDA |
|----------|-----:|:--------------------------:|:-----:|:-----:|
| maj3 | 3 | 6 | 6 | 6 |
| consensus3 | 3 | 4 | 4 | 4 |
| xor2 | 2 | 4 | 4 | 4 |
| xor3 | 3 | 12 | 12 | 12 |
| maj4 | 4 | 12 | 12 | 12 |
| mux2 | 3 | 4 | 4 | 4 |
| pos6 | 6 | 24 | 24 | 24 |
| eq5 | 5 | 10 | 10 | 10 |
| pairs8 | 8 | 8 | 8 | 8 |
| pairs10 | 10 | 10 | `timeout` | 10 |
| pairs12 | 12 | 12 | `timeout` | 12 |
| collapse14 | 14 | 7 | `timeout` | 7 |

On a genuine two-level basis LogicalOptimizer **ties** the specialized two-level
minimizers cube for cube on every function where they finish (and matches PyEDA,
which never times out, on all 12 rows). The multi-level column above is then an
*additional* win on top — not the whole story.

**Scale.** SymPy builds a 2ⁿ truth table for Quine–McCluskey, so it degrades
sharply with variable count and **times out (> 10 s)** from 10 variables onward
(`pairs10`+). PyEDA (Espresso over the same truth table) and LogicalOptimizer both
stay in the low-millisecond range across the whole corpus. Beyond 14 variables
only LogicalOptimizer runs — both competitor tools build 2ⁿ rows and are
impractical there.

> [!NOTE]
> Timings are wall-clock and machine-dependent; the head-to-head above compares
> **literal counts** (machine-independent). For controlled timing, see the
> BenchmarkDotNet suites in `doc/BENCHMARKS.md`.

## Reproduce

```bash
# Our side (result size, minimality status, time per corpus function):
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare
# Apples-to-apples two-level SOP (result.DNF), matching SymPy/PyEDA:
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- compare --dnf

# Competitor side (SymPy + PyEDA; Linux/POSIX, where the per-function timeout fires):
python tools/compare_sympy_pyeda.py --max-vars 14 --timeout 10
```

The CI **Comparison table** step runs both on the Linux runner and prints them to
the workflow log. The Python script self-skips any tool that is not importable and
never fabricates numbers.
