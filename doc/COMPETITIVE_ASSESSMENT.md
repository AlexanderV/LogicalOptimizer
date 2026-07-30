# Competitive assessment

_Assessment date: 2026-07-30. Scope: propositional Boolean reasoning, expression
minimization, SAT, model counting, and knowledge compilation. This is a product
assessment, not a claim that one tool is universally better._

## Executive judgment

LogicalOptimizer has a credible, narrow lead for one buyer and does not lead the
broader solver market:

- **Best fit:** a .NET team that needs Boolean formulas embedded in an application,
  wants no third-party runtime dependency, and treats equivalence, explicit proof
  status, budgets, diagnostics, and Native AOT as product requirements.
- **Competitive fit:** configuration/rule analysis where formulas are propositional,
  workloads are small or medium, and a single coherent API is more valuable than the
  deepest implementation of any one engine.
- **Poor fit:** full SMT, competition-scale SAT, large industrial BDDs, or hardware
  synthesis. Z3, CaDiCaL/Kissat, CUDD, and ABC respectively are structurally better
  choices.

The defensible differentiation is therefore not “a stronger solver.” It is
**verified Boolean transformation as a managed .NET product contract**. The strongest
parts of that contract are: every returned optimization is equivalence-checked; an
optimality claim has an explicit status and cost model; resource exhaustion is
reported; and the shipped packages need no third-party runtime. Competitors reproduce
individual parts of this proposition, but the comparison has not found another tool
that packages all of them as the same .NET-native workflow.

The largest commercial and technical risk is **trust, not feature count**. The project
has no external production case study, independent benchmark reproduction, formal
audit, or long adoption history. Its extensive internal tests reduce engineering risk
but cannot substitute for external use.

## Market map: who is actually competing

| Category | Representative tools | What the user is buying | Competitive relationship |
|---|---|---|---|
| Managed Boolean toolkit | LogicalOptimizer | One embedded API for parsing, transformation, proofs, SAT, BDD/d-DNNF, and formats | The product's intended category |
| General constraint/SMT platform | Z3 | Rich theories, modeling power, mature solving | Substitute when the problem extends beyond Boolean formulas |
| Propositional application framework | LogicNG | A mature JVM framework for formulas, SAT/MaxSAT, explanations, BDD/DNNF, and configuration | Closest functional peer, on another runtime |
| Symbolic/minimization library | SymPy, PyEDA/Espresso | Interactive algebra or two-level minimization in Python | Direct competitor for small minimization tasks only |
| Dedicated SAT engine | CaDiCaL, Kissat | Raw CNF solving throughput and solver-grade integration | Engine competitor, not toolkit competitor |
| Knowledge-compilation engine | CUDD, d4 | Large BDDs or exact model counting | Specialist competitor to individual packages |
| Logic-synthesis system | Berkeley ABC | Circuit optimization, verification, technology mapping, sequential flows | Adjacent competitor only when “optimization” means hardware |

This distinction matters. Comparing every tool in one feature-count table rewards
scope rather than fitness. The scorecards below instead model three concrete buying
decisions.

## Evidence levels

Every conclusion is tagged implicitly by the type of evidence behind it:

| Level | Meaning | Evidence used here |
|---|---|---|
| A — reproduced | Same committed corpus, pinned tools, automated verification | 17-function container run in [`comparison/merged.md`](comparison/merged.md) |
| B — executable | Verified in this repository, but not independently reproduced | Package/AOT/proof/status contracts in [`CLAIMS.md`](CLAIMS.md) |
| C — documented | Capability stated in the competitor's official documentation | Links under [Sources](#sources-and-version-boundaries) |
| D — judgment | Product or strategy inference from A–C | Weighted scorecards and recommendations below |

The existing A-level evidence proves correctness agreement and output size on the
committed corpus. It does **not** prove industrial scalability, general speed
superiority, ecosystem maturity, or buyer preference.

## Capability comparison

Legend: **strong** = central, mature capability; **yes** = supported; **partial** =
supported with material limits; **no** = outside scope; **unknown** = not established
by the evidence reviewed.

| Dimension | LogicalOptimizer | Z3 | LogicNG | SymPy | PyEDA | CaDiCaL / Kissat | ABC | CUDD / d4 |
|---|---|---|---|---|---|---|---|---|
| Native developer environment | .NET | Native with .NET and other bindings | JVM | Python | Python + C extension | C/C++ | C/C++ CLI/library | C/C++ CLI/library |
| Propositional formula API | **strong** | yes | **strong** | yes | yes | CNF literals | circuit/network | specialist structures |
| Exact small SOP minimization | **strong**, status reported | no product-level equivalent | partial | yes, exponential guard | Espresso heuristic | no | synthesis objectives differ | no |
| Multi-level expression rewriting | yes, cost-gated and checked | simplifiers, different goal | transformations | symbolic simplification | limited | no | **strong**, circuit-oriented | no |
| Per-result equivalence guard | **yes, default optimizer contract** | can be modeled | can be modeled | callable equivalence | callable equivalence | can solve a supplied miter | CEC workflows | no unified optimizer contract |
| Counterexample on inequivalence | yes | yes | yes via SAT/model APIs | partial | yes | model from supplied miter | yes | not primary |
| Explicit minimization proof status | **yes** | no comparable expression contract | unknown | no | heuristic result | n/a | no comparable expression contract | n/a |
| SAT | embedded CDCL | **strong SMT/SAT** | **strong** | basic/general symbolic use | PicoSAT integration | **strong specialist** | integrated engines | d4 is #SAT, not SAT toolkit |
| Incremental assumptions / cores | yes | yes | yes | no | limited | CaDiCaL strong; Kissat oriented to bare-metal solving | workflow-specific | no unified surface |
| DRAT/proof artifact | yes | proof support depends on route/configuration | unknown | no | no | strong proof ecosystem | workflow-specific | no |
| MaxSAT / PB / cardinality | yes | optimization/SMT | **strong** | limited symbolic | limited | no high-level modeling layer | synthesis-specific | no |
| ROBDD | yes, budgeted and reorderable | no central BDD product | **strong** | limited | yes | no | AIG-centric | **CUDD strong** |
| d-DNNF / exact #SAT | yes | no central compiler | yes | no | no | no | no | **d4 strong** |
| Weighted/evidence queries | yes after compilation | model through encoding | yes/varies by representation | no integrated KC workflow | no integrated KC workflow | no | no | specialist-dependent |
| Standard interchange | DIMACS, WCNF, OPB; BLIF/Verilog exports | SMT-LIB and APIs | propositional formats/APIs | Python expressions | DIMACS and expressions | DIMACS | **strong EDA formats** | specialist formats |
| Typed budgets/cancellation | **yes across expensive engines** | timeouts/limits | handlers | limited | limited | limits/signals/config | commands/options | specialist APIs |
| Diagnostic decision trace | **yes for optimizer pipeline** | statistics/traces, different goal | handlers/results | no comparable pipeline | no comparable pipeline | solver statistics | command logs | specialist statistics |
| No third-party runtime dependency in shipped .NET packages | **yes** | no, native component | n/a | n/a | n/a | n/a | n/a | n/a |
| Native AOT/trimming evidence | **win-x64 and linux-x64 CI** | not an equivalent deployment model | n/a | n/a | n/a | n/a | n/a | n/a |
| Adoption / institutional confidence | **weakest axis** | **strong** | **stronger** | **strong** | established but older release line | **strong specialist** | **strong specialist** | **strong specialist** |
| Commercial support | community | Microsoft/open source ecosystem | available from BooleWorks | community | community | community/research | community/research | varies |

### What the matrix says

1. **LogicNG is the closest product analogue.** It spans the same application-level
   territory and has a stronger maturity story. LogicalOptimizer's reason to exist is
   not broader logic capability; it is the .NET deployment model plus stricter,
   explicit optimization-result contracts.
2. **Z3 is the strongest substitution risk.** A .NET team may accept a native
   dependency and encode Boolean optimization around Z3 to gain SMT theories and
   institutional confidence. LogicalOptimizer wins only when its simpler
   propositional model, deployability, and packaged transformations matter more.
3. **SymPy/PyEDA are task competitors.** They are attractive for notebooks and
   two-level minimization, but not close substitutes for an AOT-safe embedded .NET
   library. PyEDA's Espresso is near-minimal rather than an optimality proof; SymPy
   documents an eight-variable default guard because exhaustive simplification is
   exponential.
4. **CaDiCaL/Kissat, ABC, CUDD, and d4 define the performance ceiling.** They should be
   integration or hand-off targets, not feature-by-feature roadmap templates.

## Reproduced head-to-head results

The controlled comparison pins one corpus and competitor versions in a Linux
container. Deterministic outcomes are more meaningful than the cross-tool timings:

| Claim supported by the run | Result | Correct interpretation |
|---|---:|---|
| Default result literal count vs. SymPy/PyEDA | no larger on 12/12 comparable functions; strictly smaller on 4 | Factoring gives smaller multi-level output on some structured formulas |
| Like-for-like two-level DNF | same literal count wherever competitors finish | Parity on this corpus, not universal optimality of every heuristic zone |
| Equivalence miter verdict | OUR, CaDiCaL, Kissat, and Z3 all report UNSAT on 17/17 | Independent solvers agree that every measured transformation preserves semantics |
| Exact model count | OUR BDD/d-DNNF, d4, and LogicNG agree on 17/17 | Strong cross-validation of counts on this corpus |

The corpus is only 17 structured formulas and tops out at roughly 24 input variables.
It validates the mechanics and several quality claims; it is too small to establish a
general performance ranking. The current report also does not compare:

- hard SAT Competition families;
- large real configuration models;
- industrial BLIF/AIG networks;
- adversarial BDD variable orders;
- large multi-output PLA workloads;
- memory ceilings and cancellation latency;
- API implementation effort for the same end-user task.

## Weighted decision scorecards

Scores are from 1 (poor fit) to 5 (excellent fit). Weighted totals are judgments, not
benchmark results. The weights are published so a reader can substitute their own.

### Scenario A: embedded Boolean reasoning in a .NET service or CLI

Weights: .NET/deployment 25%, result assurance 20%, integrated Boolean scope 15%,
operability 10%, raw solving capacity 10%, maturity/support 15%, interoperability 5%.

| Tool | .NET / deploy | Assurance | Scope | Operability | Capacity | Maturity | Interop | Weighted / 5 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| LogicalOptimizer | 5 | 5 | 5 | 4 | 3 | 2 | 4 | **4.25** |
| Z3 | 3 | 4 | 5 | 4 | 5 | 5 | 5 | **4.20** |
| LogicNG | 1 | 4 | 5 | 4 | 4 | 4 | 4 | **3.35** |
| SymPy / PyEDA | 1 | 3 | 2 | 2 | 2 | 3 | 3 | **2.20** |
| CaDiCaL / Kissat | 1 | 4 | 1 | 2 | 5 | 5 | 3 | **2.85** |

**Decision:** LogicalOptimizer narrowly leads only because deployment and packaged
result assurance are deliberately heavily weighted. If native deployment is acceptable
or theories may appear later, Z3 becomes the safer default.

### Scenario B: product configuration and business-rule analysis

Weights: formula/model API 20%, explanation and counterexamples 15%, optimization and
counting breadth 20%, solver capacity 15%, maturity/support 20%, deployment 10%.

| Tool | Model API | Explain | Breadth | Capacity | Maturity | Deploy | Weighted / 5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| LogicalOptimizer | 5 | 4 | 5 | 3 | 2 | 5 | **4.00** |
| LogicNG | 5 | 5 | 5 | 4 | 4 | 3 | **4.50** |
| Z3 | 4 | 4 | 4 | 5 | 5 | 3 | **4.25** |
| SymPy / PyEDA | 3 | 2 | 2 | 2 | 3 | 2 | **2.45** |

**Decision:** LogicNG leads for a JVM-compatible organization because it combines
domain breadth with maturity. LogicalOptimizer is compelling when .NET-native
deployment and transformation assurance are hard constraints, but it needs real
configuration case studies and larger-model evidence to close the confidence gap.

### Scenario C: specialist solver, model-counting, or EDA workload

Weights: domain performance 35%, scale evidence 20%, domain features 20%,
interoperability 10%, maturity 15%.

| Tool | Domain perf. | Scale evidence | Domain features | Interop | Maturity | Weighted / 5 |
|---|---:|---:|---:|---:|---:|---:|
| LogicalOptimizer | 2 | 2 | 3 | 4 | 2 | **2.45** |
| CaDiCaL / Kissat for SAT | 5 | 5 | 5 | 4 | 5 | **4.90** |
| d4 for #SAT | 5 | 5 | 4 | 4 | 4 | **4.55** |
| CUDD for decision diagrams | 5 | 5 | 5 | 3 | 5 | **4.80** |
| ABC for synthesis | 5 | 5 | 5 | 5 | 5 | **5.00** |

**Decision:** use the specialist. LogicalOptimizer's value here is a convenient
in-process baseline, preprocessing layer, verified small/medium engine, or portable
fallback—not leadership at industrial scale.

## Competitor-by-competitor judgment

### Z3

**Why it wins:** theories, optimization over richer models, mature bindings and
institutional trust, broad modeling runway. Its official guide describes arithmetic,
bit-vectors, arrays, strings, floating point, quantifiers, optimization, and more.

**Why LogicalOptimizer can win:** a smaller propositional API; expression minimization
as a first-class result rather than a user-built encoding; default equivalence guard;
explicit minimality/resource statuses; no native runtime component.

**Primary displacement risk:** teams may standardize on one solver to avoid a second
logic abstraction, even if Z3 is heavier than necessary.

**Response:** make “Boolean transformation with evidence” the message. Do not attempt
to chase SMT theories.

### LogicNG

**Why it wins:** closest end-to-end scope, mature configuration-oriented use cases,
commercial support, SAT/MaxSAT and knowledge-compilation depth.

**Why LogicalOptimizer can win:** .NET-native packaging, Native AOT/trimming evidence,
zero third-party runtime dependency, optimizer trace, and explicit proof/minimality
contracts.

**Primary displacement risk:** organizations that already run JVM services have little
platform reason to switch, and maturity favors LogicNG.

**Response:** run a shared, realistic configuration corpus and compare developer effort,
counterexamples, weighted/evidence queries, cancellation, and memory—not only counts.

### SymPy and PyEDA/Espresso

**Why they win:** familiar Python workflow, notebooks, algebraic composition, and—in
PyEDA—an established C Espresso implementation with multi-output minimization.

**Why LogicalOptimizer can win:** embedded .NET use, integrated multi-level output,
proof status, SAT-backed scaling zones, budgets, CLI/JSON contracts, and deployment.

**Primary displacement risk:** the buyer only needs a build-time script, so an embedded
library is unnecessary.

**Response:** own production and CI integration; do not position against exploratory
Python use. Add larger multi-output PLA evidence before claiming parity with Espresso.

### CaDiCaL and Kissat

**Why they win:** their product is the SAT engine itself. CaDiCaL emphasizes a
documented, changeable CDCL implementation and incremental API; Kissat is a bare-metal
C solver optimized around modern preprocessing/inprocessing.

**Why LogicalOptimizer can win:** no FFI or process boundary; high-level formulas,
encoders, optimizer, model counting, and a coherent .NET result model.

**Primary displacement risk:** hard instances overwhelm the embedded solver.

**Response:** keep honest size/budget thresholds and define an optional DIMACS hand-off
recipe. A pluggable external-solver boundary would improve scalability without
pretending the built-in solver is competition-grade.

### Berkeley ABC

**Why it wins:** sequential synthesis and verification, network/AIG optimization,
technology mapping, and established EDA flows. Its objectives include area, levels,
delay, and mapped hardware—not source-expression literal count.

**Why LogicalOptimizer can win:** application-rule expressions, managed embedding,
human-readable output, and result contracts. These are different jobs.

**Primary displacement risk:** calling LogicalOptimizer's AIG rewriting “logic
synthesis” can invite an invalid comparison to ABC.

**Response:** consistently say “DAG-aware expression rewriting”; reserve “synthesis”
for the hand-off/export scenario.

### CUDD and d4

**Why they win:** focused engineering and evidence for large decision-diagram or exact
counting workloads.

**Why LogicalOptimizer can win:** one package-level workflow from expression to
BDD/d-DNNF, weighted/evidence queries, budgets, and managed deployment.

**Primary displacement risk:** users infer industrial scale from the presence of a BDD
or d-DNNF implementation.

**Response:** publish node/memory curves and explicit operating envelopes. Recommend a
specialist once the envelope is exceeded.

## Positioning and defensibility

### Positioning statement

> For .NET teams that transform and analyze Boolean business rules, feature models, or
> generated conditions, LogicalOptimizer is a managed toolkit that returns optimization
> results with equivalence and proof-status evidence. Unlike general SMT solvers,
> Python minimizers, or native SAT/EDA engines, it combines the transformation workflow,
> budgets, diagnostics, and deployment contract in one .NET package family.

### Defensible advantages

| Advantage | Strength | Why it is defensible | What could erode it |
|---|---|---|---|
| Per-result soundness guard | high | Architectural contract backed by tests and independent miter checks | A competitor can add a wrapper; failures in the internal oracle would damage trust |
| Explicit proof/status model | high | Cross-cutting API and documentation discipline | Status semantics become confusing or inconsistent across engines |
| Managed/AOT deployment | high for .NET niche | Structural platform choice, verified in CI | Users accept native dependencies; competitors ship simpler native bundles |
| Integrated engine breadth | medium | Reduces glue code and representation conversion | Breadth outruns quality or maintainability |
| Reproducible claim discipline | medium | Pinned corpus, automated artifact checks, banned overclaims | Still self-produced and based on a small corpus |
| Algorithm implementation alone | low | Public algorithms can be reproduced | Specialists will remain deeper and faster |

The moat is therefore **contract integration plus trust accumulation**, not proprietary
algorithms.

## Gaps, ranked by competitive impact

| Priority | Gap | Why it matters | Evidence required to close it |
|---:|---|---|---|
| 1 | No external users or case studies | Makes maturity the deciding loss against Z3/LogicNG | 3+ consented workloads with formula sizes, operations, deployment constraints, and outcomes |
| 2 | No independent comparison reproduction | All benchmark evidence is project-produced | Third-party run with published manifest and result diff |
| 3 | Corpus too small and synthetic | Cannot support scale or domain-performance conclusions | Real configuration, rule-engine, multi-output PLA, hard SAT, and adversarial BDD suites |
| 4 | No documented operating envelope by engine | Users cannot predict when budgets or heuristics dominate | Curves for variables/clauses/nodes, wall time, peak memory, status, and cancellation latency |
| 5 | No external SAT/BDD backend seam | Forces users to replace the toolkit when specialist scale is reached | Stable adapter contract plus CaDiCaL/d4 reference integrations |
| 6 | Limited institutional support signal | Enterprise buyers price maintenance risk | Support policy, named maintainers, response SLO options, or partner support |
| 7 | Comparisons omit developer effort | A unified API is asserted but not quantified | Matched implementation studies with code size, setup, deployment artifact, and failure handling |

## Recommended roadmap

### Next 30 days: improve the evidence, not the feature count

1. Add four corpus families: one real feature model, one business-rule regression set,
   one multi-output PLA set, and one adversarial BDD-order set.
2. Record peak working set, allocation, status, and cancellation overshoot in addition
   to elapsed time.
3. Publish an “engine envelope” table: intended use, soft threshold, hard budget,
   fallback/status, and external-tool hand-off.
4. Turn the existing use-case issue into a short outreach campaign; zero incoming
   reports is a distribution problem, not yet a roadmap signal.

### Next 90 days: validate the differentiator

1. Build one matched configuration case study in LogicalOptimizer, LogicNG, and Z3.
   Compare end-to-end modeling code, counterexample workflow, repeated-query behavior,
   deployment artifact, and operational failure modes.
2. Obtain one independent reproduction of the container comparison.
3. Add an optional external solver interface while keeping the default packages free of
   third-party runtime dependencies.
4. Publish only segment-specific claims. Avoid a universal score or “better than Z3”
   language.

### Defer unless user evidence appears

- SMT theories;
- technology mapping or sequential synthesis;
- a competition-grade SAT portfolio;
- an industrial CUDD replacement;
- more exporters without a demonstrated workflow.

These would dilute the product's clearest position and create maintenance obligations
without strengthening the current reason to choose it.

## Go-to-market implications

The initial buyer is not “anyone doing logic.” It is a .NET library/platform engineer
who owns generated conditions, feature rules, entitlement logic, configuration
validity, or CI equivalence checks and has one of these constraints:

- native dependencies are difficult to approve or deploy;
- silent semantic changes are unacceptable;
- resource bounds and cancellation must be observable;
- results need a machine-readable audit trail;
- the same formulas need several operations, not just SAT.

The most persuasive demonstration is therefore:

1. optimize an old and new rule set;
2. show the smaller canonical expression;
3. show equivalence status or a concrete counterexample;
4. show minimality/resource status and diagnostic trace;
5. publish as trimmed/AOT or run as a dependency-free CLI;
6. hand the same model to counting or configuration queries.

That story is more differentiated than a solver microbenchmark and aligns with the
capabilities the project can currently defend.

## Sources and version boundaries

### Repository evidence

- [`comparison/merged.md`](comparison/merged.md) — reproduced 17-function results.
- [`COMPARISON_METHODOLOGY.md`](COMPARISON_METHODOLOGY.md) — controls, exclusions,
  timeout treatment, and reproduction.
- [`comparison/manifest.json`](comparison/manifest.json) — environment and corpus
  fingerprint.
- [`CLAIMS.md`](CLAIMS.md) — exact meanings, executable evidence, and limitations for
  verification, minimality, dependencies, AOT, and comparison claims.
- [`ADOPTION.md`](ADOPTION.md) — current external-adoption evidence (zero submitted
  use-case reports at the assessment date).

### Official competitor documentation reviewed

- Z3: [project repository](https://github.com/Z3Prover/z3),
  [guide](https://microsoft.github.io/z3guide/), and
  [optimization overview](https://microsoft.github.io/z3guide/docs/optimization/intro/).
- LogicNG: [documentation](https://logicng.org/documentation/),
  [knowledge compilation](https://logicng.org/documentation/knowledge-compilation/),
  and [MaxSAT](https://logicng.org/documentation/solvers/maxsat-solving/).
- SymPy 1.14:
  [`simplify_logic`](https://docs.sympy.org/latest/modules/logic.html).
- PyEDA:
  [two-level minimization](https://pyeda.readthedocs.io/en/latest/2llm.html) and
  [Espresso API](https://pyeda.readthedocs.io/en/latest/reference/boolalg/espresso.html).
- CaDiCaL: [official repository](https://github.com/arminbiere/cadical) and
  [API header](https://github.com/arminbiere/cadical/blob/master/src/cadical.hpp).
- Kissat: [official repository](https://github.com/arminbiere/kissat).
- Berkeley ABC: [official repository](https://github.com/berkeley-abc/abc).

The controlled run pins SymPy 1.14.0, PyEDA 0.29.0, Z3 4.16.0.0, and exact commits
for native competitors in [`../tools/comparison/Dockerfile`](../tools/comparison/Dockerfile).
Feature descriptions may evolve after the assessment date; deterministic comparison
claims apply only to the pinned versions, corpus, and manifest.
