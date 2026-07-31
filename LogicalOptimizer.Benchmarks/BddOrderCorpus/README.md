# Adversarial BDD variable-order corpus (generated)

A small, deterministic corpus of formulas whose ROBDD size is **order-sensitive**, added
for competitive-assessment gap #3 (30-day roadmap item 1, adversarial BDD family). It
turns the documented budget envelope — "exceeding the node budget throws a typed
exception rather than exhausting memory" ([docs-site/articles/bdd.md](../../docs-site/articles/bdd.md))
— into a pinned regression suite: the same function builds a tiny diagram in a good
order and provably blows a small explicit budget in the adversarial order.

> **These are GENERATED, structured formulas — classic textbook adversaries, not
> industrial workloads.** No randomness at all: the generator is purely structural, so
> regeneration is byte-identical.

## Structures

Two classic order-sensitive families over *n* bit pairs (a_i, b_i):

- **eq-comparator** — `EQ(a,b) = AND_i (a_i <-> b_i)` written as SOP factors
  `(a_i & b_i | !a_i & !b_i)`. Interleaved order `a1,b1,a2,b2,…` gives a linear-size
  ROBDD; separated order `a1..an,b1..bn` is exponential (~2ⁿ).
- **disjoint-pairs** — `OR_i (a_i & b_i)`, Bryant's textbook example: linear when pairs
  are adjacent in the order, ~2ⁿ when separated.

The engine's public `Build` sorts variables **alphabetically**, so each structure is
committed twice with different variable *names*:

- `*-interleaved.expr` names the bits `x01a, x01b, x02a, …` — alphabetical order is the
  good interleaved order;
- `*-separated.expr` names them `a01..a(n), b01..b(n)` — alphabetical order is the
  adversarial separated order.

The formula **text** always lists each pair together, so first-appearance order (one of
`BuildWithBestOrder`'s candidate heuristics) stays interleaved even in the adversarially
named files — which is exactly what the recovery regression exercises.

## Files and pinned reference values

File format: `#` header lines (kind, bits, order, note) and the expression on the last
line, parser syntax `!`/`&`/`|`. Reference numbers (reprinted by `-- generate-corpora`;
"allocated nodes" is `NodeCount` after `Build`, which includes construction
intermediates — the value the node budget meters):

| File | Vars | Allocated nodes (alphabetical `Build`) | `BuildWithBestOrder` | `BuildWithSiftedOrder` | Models |
|------|-----:|-----:|-----:|-----:|-------:|
| `eq10-interleaved.expr` | 20 | 186 | 78 | 30 | 1,024 |
| `eq10-separated.expr` | 20 | 6,138 | 78 | 30 | 1,024 |
| `eq12-interleaved.expr` | 24 | 259 | 94 | 36 | 4,096 |
| `eq12-separated.expr` | 24 | 24,570 | 94 | 36 | 4,096 |
| `pairs12-interleaved.expr` | 24 | 169 | 59 | 25 | 16,245,775 |
| `pairs12-separated.expr` | 24 | 12,286 | 59 | 25 | 16,245,775 |
| `pairs14-interleaved.expr` | 28 | 225 | 69 | 29 | 263,652,487 |
| `pairs14-separated.expr` | 28 | 49,150 | 69 | 29 | 263,652,487 |

## How they were generated

Generator: [`BddOrderCorpusGenerator`](../BddOrderCorpusGenerator.cs) (deterministic,
no seeds needed). Regenerate (byte-identical) with:

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- generate-corpora
```

`BddOrderCorpusRegressionTests.Corpus_CommittedFiles_MatchTheDeterministicGeneratorExactly`
asserts the committed files equal the generator output on every gate run.

## How it is exercised

[`BddOrderCorpusRegressionTests`](../../LogicalOptimizer.Tests/Engines/Bdd/BddOrderCorpusRegressionTests.cs)
(deterministic, **gate-visible**) uses a deliberately small explicit budget of **1,500
nodes** (not the 1,000,000 default; comfortably above every good-order build, far below
every adversarial one, so failures are fast) and asserts:

1. **good order** — `Build` succeeds inside the small budget at the pinned allocated
   node count and pinned exact model count;
2. **adversarial order** — the same build throws the documented typed
   `NodeBudgetExceededException` quickly (never a hang or memory blowup): the
   "typed budgets + honest statuses" claim, evidenced;
3. **recovery** — `BuildWithBestOrder` (first-appearance heuristic) and
   `BuildWithSiftedOrder` (Rudell sifting, pinned at 36 reachable nodes for
   `eq12-separated`) recover a small diagram from the adversarially named file under
   the same small budget.
