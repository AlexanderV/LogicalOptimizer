# Choosing a Tool

An honest answer to "should I use LogicalOptimizer?" — including the cases where you
should not. Three questions, then the evidence behind each, kept in separate buckets
because they are separate claims: **features**, **output quality**, **performance**,
**platform/deployment**, and **maturity/adoption**.

## 1. Where LogicalOptimizer has a justified advantage

| Scenario | Why it wins |
|---|---|
| A **managed .NET** application that must reason about Boolean logic | No third-party runtime dependency, `net8.0`/`net10.0`, Native-AOT- and trim-verified in CI. No native binary, no JVM, no Python, no P/Invoke. |
| You must **prove** an optimization did not change behaviour | Every result is equivalence-verified before it is returned (truth table ≤12 variables, SAT miter beyond), and a failed check rolls back to the input instead of shipping an unverified result. |
| You need to know **whether the result is optimal** | `MinimizationStatus` reports `MinimalProven` / `BudgetExceeded` / `Heuristic`. Most tools return a smaller expression and say nothing about optimality. |
| **Equivalence checking with a counterexample** between two rule versions | `EquivalenceChecker.Check` returns the concrete assignment where old and new disagree — a ready-made regression test. The CLI [`check` verb](cli-usage.md#equivalence-check-check) does the same from a script, with the verdict in the exit code. |
| You need to **explain** a result in production | The opt-in [diagnostic trace](diagnostic-trace.md) records the engine chosen, the thresholds, budgets, candidate costs, proof paths and fallbacks. |
| A **dependency-free CLI** for Boolean work in CI | One `dotnet tool install`, a versioned `--format=json` report and documented exit codes. |

## 2. Where it is competitive

| Scenario | Standing |
|---|---|
| General-purpose propositional toolkit | Broad and well integrated (parsing, minimization, SAT, MaxSAT, BDD, d-DNNF, formats), but **LogicNG** on the JVM is the more mature framework with deeper engines. |
| Exact small-scale SOP/POS minimization | On par with the best available: provably minimal covers up to 10 variables (budgeted 11–12), with the proof status reported. SymPy is comparable in result, slower at scale. |
| Model counting / knowledge compilation | Real ROBDD and decision-DNNF implementations with exact `#SAT`, weighted counting and enumeration — younger and narrower than LogicNG's, far narrower than CUDD for pure BDD scale. |
| Incremental SAT usage | A complete CDCL feature set (assumptions, unsat cores, DRAT) — enough for embedded use, not a competition-grade solver. |

## 3. Where you should choose something else

| If you need | Use |
|---|---|
| Arithmetic, bit-vectors, arrays, quantifiers — full SMT | **Z3** |
| Maximum raw SAT throughput on hard industrial instances | **Kissat** or **CaDiCaL** (via PySAT or directly) — and you can keep the toolkit for everything around the solver: the [external-solver seam & DIMACS hand-off](external-solvers.md) routes the CNF query to them while parsing, Tseitin encoding and counterexample decoding stay here |
| Industrial logic synthesis: technology mapping, retiming, sequential flow | **Berkeley ABC** |
| Reference-grade large two-level / multi-output PLA minimization | **Espresso** (or PyEDA, which wraps it) |
| Very large BDDs with deep reordering | **CUDD** (or Python `dd`) |
| A mature JVM propositional ecosystem | **LogicNG** |

These are not gaps to be apologetic about: the toolkit deliberately stays a propositional
Boolean reasoning library for managed .NET rather than chasing an SMT stack or an EDA flow.
Where a dedicated engine is the right answer, the hand-off is designed in, not bolted on —
see [External SAT Solvers & the DIMACS Hand-off](external-solvers.md). The
[engine operating envelope](budgets-and-zones.md#engine-operating-envelope) states, per
engine, exactly where that point is: intended use, the enforced budget, what happens when
it is exceeded, and which of the tools above to hand the instance to.

## The evidence, by dimension

### Feature comparison

Verified against the code and each competitor's official documentation, most recently for
v3.0.0. LogicalOptimizer covers: canonical n-ary AST with construction-time canonicalization
and interning, exact QM with covering-table reductions and branch-and-bound, SAT prime cover
(13–24 variables), Espresso-style cube lists beyond, AIG multi-level rewriting, CDCL SAT with
DRAT, cardinality/PB/MaxSAT encodings, ROBDD with quantification and sifting, decision-DNNF
with weighted counting, DIMACS/WCNF/OPB interoperability, six export formats, typed resource
budgets, and CLI access to all of it.

What it does **not** have: theory solvers, technology mapping or sequential synthesis, ZDD/ADD
support, a parallel or portfolio solver, and multi-year production hardening.

### Output-quality comparison

The claim "never larger, often smaller" is measured on a committed corpus against SymPy and
PyEDA, split into a like-for-like two-level table and the default multi-level output — see
[Benchmarks & Comparison](benchmarks.md). Summary: on a genuine two-level basis it **ties**
the specialized minimizers cube for cube wherever they finish; the default multi-level
(factored) output is an *additional* win on top (for example `pos6`: 6 literals vs 24).

### Performance benchmark

Wall-clock timing is machine-dependent and reported separately from correctness. The corpus
run stays in the low-millisecond range through 24 variables (see the
[case studies](case-studies.md) for measured figures). SymPy times out from ~10 variables
because it builds a 2ⁿ table; PyEDA and LogicalOptimizer do not. This says nothing about raw
SAT throughput on hard instances, where dedicated solvers are far ahead — no benchmark here
claims otherwise.

### Platform / deployment comparison

This is the clearest structural advantage and the least subjective:

| | LogicalOptimizer | Z3 | LogicNG | PyEDA / SymPy | Kissat / ABC / CUDD |
|---|---|---|---|---|---|
| Runtime | managed .NET | native + bindings | JVM | Python | native binaries |
| Runtime dependencies | **none** | native library | JVM | Python runtime | build toolchain |
| Native AOT / trimming | **verified in CI** | n/a | n/a | n/a | n/a |
| Single-file / embedded deploy | yes | needs the native lib | needs a JVM | needs Python | process/FFI integration |

### Maturity / adoption

The weakest axis, stated plainly: this is a young project without multi-year production
adoption, without third-party benchmark reproductions, and without an external formal audit.
What exists instead is mechanical discipline — a pinned member-level public API baseline,
enforced package layering, ~1210 gate tests across ten techniques including mutation testing,
every documented example executed as a test, and deterministic, provenance-attested releases.
That is evidence of *care*, not a substitute for adoption history. If you need a dependency
with a decade of field use, LogicNG or Z3 is the safer institutional choice.

## Next steps

- [Benchmarks & Comparison](benchmarks.md) — the measured numbers and how to reproduce them
- [Case Studies](case-studies.md) — worked examples with sizes, statuses, time and memory
- [Operation Contracts & Statuses](contracts-and-statuses.md) — exactly what each claim guarantees
- [Resource Budgets & the Zone Model](budgets-and-zones.md#engine-operating-envelope) — the per-engine operating envelope: soft thresholds, hard budgets, exceed behavior, hand-off points
