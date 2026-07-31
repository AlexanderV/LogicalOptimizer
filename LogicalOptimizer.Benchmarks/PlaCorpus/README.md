# Multi-output PLA corpus (generated)

A small, deterministic corpus of **multi-output** two-level functions in the classic
Espresso `.pla` cube format, added for competitive-assessment gap #3 ("corpus too small
and synthetic" — 30-day roadmap item 1, multi-output PLA family). It gives the minimizer
a repeatable multi-output regression signal: every output of every file is optimized
through the facade and its literal count is pinned.

> **These are GENERATED, structured functions — NOT downloaded industrial PLA benchmarks.**
> They are produced in-repo by a committed, deterministic generator (classic textbook
> blocks plus seeded pseudo-random cube lists), so the corpus is fully reproducible and
> free of external-download / provenance concerns. It closes the *multi-output* part of
> the corpus gap; it does **not** make the corpus an industrial workload.

## Format

Standard Espresso `.pla` subset: `.i`/`.o`/`.ilb`/`.ob`/`.p` headers, cube lines with
`0/1/-` inputs and `0/1` outputs, `.e` terminator, `#` comments. Semantics are the
Espresso **fd** default restricted to no output don't-cares: output *j* is the OR of the
cubes whose output column *j* is `1`. The reader
([`PlaFile`](../PlaFile.cs)) is deliberately dev-tool infrastructure in the (unpackaged)
Benchmarks project — the shipped library packages export BLIF/Verilog but have **no**
public PLA import, and the pinned public API is unchanged.

## How they were generated

Generator: [`PlaCorpusGenerator`](../PlaCorpusGenerator.cs). Classic members are written
from their well-known truth tables; `rnd*` members use `System.Random` with the fixed
seeds recorded below and in each file's `# seed` header. Regenerate (byte-identical) with:

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- generate-corpora
```

`PlaCorpusRegressionTests.Corpus_CommittedFiles_MatchTheDeterministicGeneratorExactly`
asserts the committed files equal the generator output on every gate run.

## Members

| File | Kind | In / Out | Cubes | Seed |
|------|------|:--------:|------:|-----:|
| `bcd7seg.pla` | BCD-to-7-segment decoder (digits 0–9) | 4 / 7 | 10 | — |
| `add2.pla` | 2-bit binary adder, full minterm table | 4 / 3 | 16 | — |
| `dec3to8.pla` | 3-to-8 line decoder, one-hot | 3 / 8 | 8 | — |
| `prio8.pla` | 8-line priority encoder + valid flag (uses `-`) | 8 / 4 | 8 | — |
| `cmp3.pla` | 3-bit magnitude comparator (lt/eq/gt), full table | 6 / 3 | 64 | — |
| `rnd6x4-01.pla` | seeded pseudo-random cube list | 6 / 4 | 24 | 9001 |
| `rnd6x4-02.pla` | seeded pseudo-random cube list | 6 / 4 | 24 | 9002 |
| `rnd8x3-01.pla` | seeded pseudo-random cube list | 8 / 3 | 28 | 9003 |

## How it is exercised

[`PlaCorpusRegressionTests`](../../LogicalOptimizer.Tests/Engines/Minimization/PlaCorpusRegressionTests.cs)
(deterministic, **gate-visible** — not `Category=Performance`) optimizes each output's
cube expansion via `BooleanExpressionOptimizer.OptimizeExpression` and asserts:

1. the equivalence self-check (`result.IsEquivalent()`) passes;
2. `MinimizationStatus == MinimalProven` (every member is inside the ≤ 10-variable
   exact-guarantee zone);
3. the optimized literal count never exceeds the raw cube-expansion literal count;
4. the optimized literal count equals the pinned golden value below — the regression
   signal — and each file's total across outputs matches the pinned sum (the
   multi-output collective view).

Pinned reference (from `-- generate-corpora`, which reprints this table):

| File | Cube-expansion literals (total) | Optimized literals (total) |
|------|-------------------------------:|---------------------------:|
| `bcd7seg.pla` | 196 | 56 |
| `add2.pla` | 88 | 23 |
| `dec3to8.pla` | 24 | 24 (already minimal) |
| `prio8.pla` | 76 | 25 |
| `cmp3.pla` | 384 | 34 |
| `rnd6x4-01.pla` | 258 | 97 |
| `rnd6x4-02.pla` | 214 | 82 |
| `rnd8x3-01.pla` | 318 | 164 |
