# Testing Strategy — Analysis, Actuality & Checklist

**Last audited:** 2026-07-29 (four-criteria re-audit: nine parallel read-only reviewers covered
every test file — representative, logically-correct, strong, non-duplicating — verified each
flagged finding against production code, and re-ran the ten techniques against current behavior.
~30 fixes applied. The one genuine circular oracle was in the flagship minimizer honesty test
(`MinimalSopWithStatus_ProvenClaimUnderAnyBudget_ImpliesTrueMinimum`): its "reference optimum"
was the SAME routine at a larger budget, so a branch-and-bound mutant would corrupt both runs
identically — now cross-checked by an INDEPENDENT SatTwoLevelMinimizer prime-cover (a SAT cover
beating a proven QM minimum disproves the proof). Two "status-only" minimizer tests asserted the
`proven`/`MinimalProven` flag but never the returned cover — now pinned against a brute-force
on-set / truth-table oracle. One dead no-op (`Assert.Same(circuit, circuit)`) and one AND/OR-
indistinguishable CSV assertion fixed; the golden-master guard now re-verifies CNF/DNF, not just
Optimized. ~15 weak `Contains`/`<=`/`NotEmpty`/`ThrowsAny` assertions pinned to exact values where
the output is deterministic; Metamorphic renaming now pins byte-identical output and AIG idempotent
duplication compares the canonical Root. Exact-duplicate rows removed. Gate suite 1158 → **1152**
green (−6 dups, every removed behavior still pinned by a stronger surviving test).
**Previous audit:** 2026-07-27 (v2.0 post-migration re-audit: six parallel read-only reviewers
covered every test file against the four quality criteria — representative, logically correct,
strong, non-duplicating — and doc/TESTING.md's own audit rules; ~35 fixes applied across all
folders. Two genuine "green-masking" weakenings introduced by the bulk v2 migration were caught
and fixed: `ConsensusRule_ValidConsensus_ShouldSimplify` had been softened to a `<=`-count no-op,
and `PropertyBasedTests` SAT-miter cross-check had degenerated to truth-table-vs-truth-table
(the SAT engine never ran). Duplicates removed (`OptimizerTests` rows subsumed by
`OptimizerTruthTableTests`, the consensus slack-scan, CLI validation twins); missing v2 coverage
added (flat n-ary C#/BLIF/Verilog rendering, n-ary Tseitin clause counts, canonical-order
tie-breaks, distributive `ToDnf`/`ToCnf`+budget, multi-output cube sharing that actually
exercises `TrySharedCovers`, a >12-var SAT-miter differential). Suite green (net10.0, CI filter).
**Previous audit:** 2026-07-24 (full-suite audit: ~180 garbage/duplicate tests removed,
~30 weak tests strengthened, two whole files with test-local re-implementations of production
logic deleted).
**Library:** LogicalOptimizer (v2.0 — n-ary AST, single `RewriteEngine`, `FormulaFactory`
construction-time canonicalization)

---

## Part 1: Summary Matrix

This matrix covers the **cross-cutting layer only**. It sits on top of the structured functional
suite organized by subject (Part 2) — the tests that pin what the library must *do*, and the ones
that fail first when a behavior changes. Of the 1230 discovered cases, 1085 are subject tests
(Core 134 · Optimizers 186 · Engines 338 · Facade+Analysis 187 · Formats 116 · Cli 77 ·
Documentation 47) and 109 live in `Techniques/` (the ten below plus the API-surface, claims and
package-contract guards); the rest are the test infrastructure's own tests and the
projected-model-counting spike. The techniques are cross-checks on that base, not a replacement
for it — read Part 2's audit rules first.

Status legend: ☑ current and verified in the last audit · ◪ present with known gaps · ☐ not started.

| # | Technique | Applicability | Status | Coverage today | Effort to extend | Priority |
|---|-----------|:---:|:---:|---|:---:|:---:|
| 1 | **Property-Based (CsCheck)** | ★★★★★ | ☑ | 14 properties incl. extended-operator generators, `Techniques/PropertyBasedTests.cs` | Med | P0 |
| 2 | **Metamorphic** | ★★★★★ | ☑ | 11 relations (permutation AND renaming MRs pin byte-identical canonical output; AIG idempotent-duplication compares the canonical Root), `Techniques/MetamorphicTests.cs` | Med | P0 |
| 3 | **Mutation (Stryker.NET)** | ★★★★☆ | ☑ | per-module runs + killer tests (Part 5); Transformations.cs 100% | Low | P1 |
| 4 | **Algebraic** | ★★★★★ | ☑ | all axioms as laws, `Techniques/AlgebraicLawTests.cs` | Low | P1 |
| 5 | **Snapshot / Approval (Verify)** | ★★★★☆ | ☑ | 7 approved snapshots, `Techniques/SnapshotTests.cs` | Low | P1 |
| 6 | **Architecture (ArchUnitNET)** | ★★★☆☆ | ☑ | 9 rules incl. pinned public-API list (58 types, now incl. the typed budget/size exceptions `ComputationBudgetExceededException`/`NodeBudgetExceededException`/`NormalFormTooLargeException`) + `IRewriteRule`-in-`Rewrite`-namespace, `Techniques/ArchitectureTests.cs` | Low | P2 |
| 7 | **Differential** | ★★★★★ | ☑ | internal-engine suites (incl. a >12-var SAT-miter-vs-BDD test) + SymPy oracle (CI) + Z3 oracle (Microsoft.Z3, runs locally) | Med | P0 |
| 8 | **Fuzzing** | ★★★★☆ | ☑ | deterministic fuzzers (factory-invariant walker now checks dedup/complement/sortedness), `Techniques/FuzzingTests.cs` | Med | P2 |
| 9 | **Characterization** | ★★★☆☆ | ☑ | 32-expression golden master, `Techniques/CharacterizationTests.cs` | Low | P3 |
| 10 | **Combinatorial / Pairwise** | ★★★☆☆ | ☑ | covering array over 7 option axes + full 2⁷ grid | Low | P3 |

Former gaps — all closed 2026-07-25:
- **Mutation**: per-module Stryker runs executed (see Part 5); survivors analyzed —
  three real coverage gaps got deterministic killer tests in `EspressoLiteTests`
  (cofactor conflict detection, constant-1 SOP terms, last-variable rebuild), the
  rest are equivalent mutants (budget boundaries, heuristic ordering, `>>` vs `>>>`
  on non-negative values) and are documented as such.
- **Property-based**: generators now cover the FULL operator set (Xor/Eqv/Imp/Nand/Nor)
  via `ExtendedAst`; engine-to-engine properties (TruthTable vs AIG vs BDD vs both CNF
  encodings vs FormulaFactory) replace the parser round-trip for the extended operators
  the text grammar cannot express.
- **Differential**: SymPy oracle in CI; Z3 oracle (`Z3DifferentialTests`, Microsoft.Z3
  in the test project only) runs everywhere the native library loads.
- **Mid-flight cancellation**: `Facade/MidFlightCancellationTests` cancels tokens WHILE
  QM/SAT/BDD/facade run on multi-second workloads. Writing them exposed and fixed two
  real production gaps: QM prime-pairing and the covering-table reductions observed the
  token only once per level/pass (≈48 s of uncancellable work on a dense 13-variable
  table — now checked every 256 pairing rows and every reduction round).

---

## Part 2: Test hierarchy

The suite is organized by subject under `LogicalOptimizer.Tests/`; the ten techniques
above live in `Techniques/` and cut across all subjects.

```
LogicalOptimizer.Tests/
├── Core/                  Lexer, Parser (canonical output), AST node contracts, TruthTable
│     LexerTests · ParserTests · NaryNodeContractTests (n-ary invariants: ≥2 operands,
│     defensive copy, order-sensitive equality, cached hash, Clone==this; + derived-op
│     contracts) · FormulaFactoryTests (canonicalization + interning) ·
│     TruthTableGenerationTests · TruthTableMethodTests · TruthTableAdvancedTests
├── Optimizers/            rewrite rules (RewriteEngine/IRewriteRule), pattern detection
│     CanonicalOrderingTests (construction-time sort) · DistributiveExpanderTests ·
│     ConsensusRuleTests · RuleCompletenessTests · OptimizerSoundnessTests ·
│     SoundnessGuardUnknownTests (SAT-Unknown → rollback contract) ·
│     SubcircuitRewriteTests · ExtendedOptimizationRulesTests (dormant rule library) ·
│     EqvRulesTests · AdvancedPatternDetectorTests · EqvPatternRecognizerTests ·
│     TransformationsTests
├── Engines/
│   ├── Sat/               SatSolverTests · SatSolverMutationKillerTests ·
│   │                      IncrementalSatTests · DratProofTests
│   ├── Bdd/               BinaryDecisionDiagramTests · BddOperationsTests
│   ├── Aig/               AndInverterGraphTests
│   ├── Minimization/      TruthTableMinimizerTests · SatTwoLevelMinimizerTests ·
│   │                      EspressoLiteTests
│   └── Encodings/         TseitinConverterTests (n-ary clause counts) ·
│                          CnfEncodingTests (Plaisted–Greenbaum) · CardinalityAndMaxSatTests
├── Analysis/              FormulaAnalysisTests (backbone, model enumeration)
├── Facade/                BooleanExpressionOptimizer end-to-end
│     OptimizerTests · EdgeCaseTests · OptimizerTruthTableTests · NormalFormTests ·
│     OptimizationMetricsTests (n-ary cost model) · OptimizationQualityAnalyzerTests ·
│     OptimizationResultTests · EqvIntegrationTests · ConsoleTestedCasesTests
│     (unique regression rows) · ConsoleInterfaceTests (limits) ·
│     MidFlightCancellationTests (Performance; mid-flight cancellation is timing-dependent so
│     it lives here, not the gate — the gate has ResourceBudgetAndCancellationTests' deterministic
│     pre-cancelled-token checks) ·
│     ResourceBudgetAndCancellationTests · PerformanceValidatorTests (input validation)
├── Formats/               CsvTruthTableParserTests · MultiOutputCsvTests · ExportTests
│                          (n-ary BLIF/Verilog gates) · CSharpExpressionExporterTests
│                          (flat n-ary render) · AstVisualizerTests
├── Cli/                   CommandLineProcessorTests · OutputFormatterTests
├── Techniques/            the ten cross-cutting suites (see Part 3) + CanonicalInvariantTests
│                          (v2 interning/canonical-shape/Tseitin counts) + snapshots
├── TestInfrastructure/    RandomExpressions · TruthTableAssert · SatTestOracles ·
│                          ExpressionGeneratorTests
└── TestData/              characterization.golden.txt · PublicApi.approved.txt
```

Audit rules that keep the hierarchy healthy (enforced in review):
1. **One behavior — one test.** A duplicate is deleted, keeping the stronger variant
   (exact expected string + independent oracle beats equivalence-only beats Contains).
2. **No constructor-echo tests** (asserting a constructor stored its arguments), no
   compile-time-fact tests (inheritance checks), no `Assert.NotNull(new X())`.
3. **No test-local re-implementations of production logic as oracle** (circular
   oracle). Oracles must be independent: brute-force enumeration, hand-computed
   expectations, textbook-known minimal costs, an external tool, or a different engine.
4. **No wall-clock vanity tests.** Scale tests assert correctness under a
   deterministic effort budget (conflict/node limits), not `Stopwatch < N ms`.
5. **Weak assertions are bugs**: `Assert.NotEmpty`, unfalsifiable disjuncts and
   `>= small-constant` checks get strengthened or deleted.

---

## Part 3: Techniques — how each direction works here

### Property-based (CsCheck) — `Techniques/PropertyBasedTests.cs`

Generated ASTs (depth ≤ 4, up to 6 variables, constants included) run through
universally quantified properties: optimization preserves semantics against the
truth-table oracle, re-optimization never increases literal count, print→parse→print
is a fixpoint, whitespace at token boundaries is insignificant, Tseitin stays linear
in formula size, and every term of a proven-minimal DNF is irredundant. CsCheck
shrinks failures and prints a reproducing seed (`Set seed: "..." to reproduce`).

### Metamorphic — `Techniques/MetamorphicTests.cs`

No output oracle — a semantics-preserving transformation of the *input* must produce
the documented relation between the two *outputs*: variable renaming, negation,
cofactor substitution, disjunction composition and De Morgan duality all commute with
optimization; permuting top-level OR terms preserves semantics and (inside the exact
zone) the proven-minimal cost. Where v2 construction-time canonicalization guarantees it,
the relation is pinned as byte-identical output rather than mere equivalence (term
permutation and order-preserving renaming), and the AIG idempotent-duplication MR
(`F` vs `F | F`) compares the canonical `Root`, not just the AND-node count.

### Algebraic — `Techniques/AlgebraicLawTests.cs`

Every axiom — commutativity, associativity, distributivity, De Morgan, absorption,
idempotence, complement/identity, consensus, Shannon expansion — asserted as a law
over random subexpressions F, G, H. Commuted operands must additionally produce an
*identical canonical output string*, not just an equivalent one.

### Differential — `Techniques/DifferentialEngineTests.cs`, `Techniques/SymPyDifferentialTests.cs`

Independent engines cross-check each other: Quine–McCluskey vs brute force, truth
table vs ROBDD vs SAT miter (on equivalent AND single-minterm-perturbed pairs), BDD
model counting vs brute force vs SAT enumeration, SAT backbone vs brute-force
backbone, SAT cover minimizer vs QM (a SAT cover beating a *proven* minimum would
disprove the proof). The d-DNNF knowledge compiler
(`Engines/Dnnf/KnowledgeCompilationTests.cs`) is gated the same way: its exact `#SAT`
count must equal the ROBDD's `CountSatisfyingAssignments` on a large random + structured
corpus (both exact oracles), match brute-force truth-table counting on small formulas, and
its model enumeration must agree with `FormulaAnalysis.EnumerateModels`. The external
corpus pits our proven-minimal DNF against SymPy's
Quine–McCluskey (`simplify_logic`): equivalence required, and SymPy must never find a
smaller cover. Runs where `python`+`sympy` exist (CI installs it; skips silently otherwise).

### Fuzzing — `Techniques/FuzzingTests.cs`

In-process, deterministic (fixed seeds): garbage-alphabet parser fuzzing (only
`ArgumentException` may escape), mutation fuzzing of valid expressions, grammar
fuzzing through the whole pipeline with a truth-table oracle, corrupted-CSV fuzzing,
random-CNF solver fuzzing against a brute-force SAT oracle (returned models
re-verified), and limit probing (nesting cap, 10 000-char cap, wide flat chains).

### Characterization — `Techniques/CharacterizationTests.cs` + `TestData/characterization.golden.txt`

The complete observable output over a fixed 32-expression corpus is pinned. Any drift
fails the test and writes `.received.txt`. Regenerate intended changes with
`LOGICALOPTIMIZER_REGENERATE_GOLDEN=1` and review the diff; a guard test re-verifies
every pinned result — Optimized AND CNF AND DNF — is still semantically equivalent, so the
golden master can never legitimize a wrong answer (CNF/DNF are checked in the truth-table
zone; above it the CNF may be an equisatisfiable Tseitin encoding).

### Snapshot / approval (Verify) — `Techniques/SnapshotTests.cs`

CLI output (default + verbose), C# exporter, AST visualizer, truth-table rendering
and the debug dump are approved as `Techniques/SnapshotTests.*.verified.txt`. A change
produces `*.received.txt`; approve by replacing the verified file.

### Architecture (ArchUnitNET + reflection) — `Techniques/ArchitectureTests.cs`

The library never touches `System.Console` and never references CLI/test frameworks;
the rewrite pipeline stays internal; every rewrite rule (`IRewriteRule`) lives in the
`LogicalOptimizer.Rewrite` namespace; AST nodes are sealed/abstract and **fully immutable**
(no exceptions — the v1 `ForceParentheses` display-hint setter was removed in v2, rendering
now flows through the precedence-based `AstFormatter`); expensive public entry points take a
`CancellationToken`; the public API surface is pinned to an explicit reviewed list of 58 types
across the six assemblies (Core 23 · Sat 10 · Bdd 1 · Dnnf 2 · Minimization 5 · facade 17) — any new
public type fails the build until consciously added, and any removal (the v2 narrowing:
`Lexer`/`Parser`/`Token`/`AndInverterGraph` and the SAT/BDD low-level internals) is a
deliberate major-version decision.

### Combinatorial / pairwise — `Techniques/PairwiseOptionsTests.cs`

A greedy covering array over the 7 binary axes of `OptimizationOptions` guarantees
every pair of settings is exercised (self-checked by a coverage test); each artifact's
presence must match exactly what was requested. The full 2⁷ grid proves the optimized
expression itself is invariant across all option combinations.

### Mutation (Stryker.NET) — `LogicalOptimizer.Tests/stryker-config.json`

```powershell
dotnet tool restore
cd LogicalOptimizer.Tests
dotnet stryker                                    # full run (long)
dotnet stryker --mutate "**/Transformations.cs"   # scoped run
```

Standard mutation level, string mutations ignored, thresholds 80/60 with `break: 0`
(reporting gate). HTML report in `StrykerOutput/`. Last scoped run: Transformations.cs
100% mutation score.

---

## Part 4: Audit log

**2026-07-29 — four-criteria full-suite re-audit (nine parallel read-only reviewers; ten
techniques re-run against current behavior).** Every test file was re-graded representative /
logically-correct / strong / non-duplicating; each flagged finding was verified against production
before any change; the ten cross-cutting techniques were re-validated (the critical failure mode —
a degenerated cross-engine miter — was checked and is NOT present: the differential/property SAT
paths deliberately call `EquivalenceChecker.CheckWithSat`/`BinaryDecisionDiagram.AreEquivalent`
past the truth-table threshold, so the second engine really runs).

Genuine logical defects fixed (green-masking / circular oracle / dead assert):
- **Circular oracle (minimizer honesty).** `TruthTableMinimizerTests.MinimalSopWithStatus_
  ProvenClaimUnderAnyBudget_ImpliesTrueMinimum` compared each budgeted run to a "reference optimum"
  produced by the SAME routine at a larger budget — a branch-and-bound mutant (bad lower bound /
  best-update) corrupts both identically and the test still passes. Now the reference is cross-checked
  by an INDEPENDENT engine (`SatTwoLevelMinimizer`, a BOOM-style SAT prime-cover): a SAT cover with
  fewer literals than the "proven" QM optimum disproves the proof.
- **Status-only paints-green.** `MinimalSopWithStatus_RandomFunctions_AlwaysProvenInGuaranteeZone`
  and `OptimizeExpression_AllThreeVariableFunctions_MinimalProven` asserted only the `proven` /
  `MinimalProven` flag and never checked the returned cover — a mutant hard-coding the flag survived.
  Added a brute-force on-set oracle (evaluate the result over all 2ⁿ assignments == the requested
  on-set) and a truth-table equivalence check respectively.
- **Dead assertion.** `DnnfConditioningTests` had `Assert.Same(circuit, circuit)` (can never fail);
  replaced with `Assert.NotSame(source, conditioned)` — `Condition()` must return a fresh circuit.
- **AND/OR-indistinguishable.** `ExportTests.TruthTableToCsv` asserted only the two rows AND shares
  with OR (`0,0,0` / `1,1,1`); the OR table passed the "AND" test. Now pins all four data rows.
  `MultiOutputCsvTests` full-adder pinned only literal counts (a proxy a wrong 12-literal function
  passes) → now asserts equivalence to independent parity / majority reference forms.
- **Golden-master guard gap.** `CharacterizationTests` froze Optimized+CNF+DNF but the equivalence
  guard checked only Optimized; a wrong pinned CNF/DNF could be legitimized. Guard now re-verifies
  CNF and DNF too (truth-table zone; above it the CNF may be an equisatisfiable Tseitin encoding).

Weak assertions strengthened (exact where the output is deterministic and knowable):
- `AdvancedPatternDetectorTests` XOR/IMP detections: `Contains("XOR")`/`Contains("→")` →
  exact renderings (`"(a XOR b) | c"`, `"(x → w) | (z → y)"`, …) so a swapped/dropped operand fails.
- `EqvIntegrationTests`, `OptimizerTruthTableTests` (factorization + complex), `ConsensusRuleTests`:
  equivalence-only / `Contains` → exact optimized strings (an equivalence-only check accepts a
  worse-but-equivalent rewrite; the removed `EqvPattern` "either m↔n or n↔m" hedge was a false
  disjunct — the rendering is deterministic).
- `ConsoleInterfaceTests` `NotEmpty(Variables)` → exact `["a"]` + exact CNF/DNF; `IncrementalSatTests`
  `Assert.Superset` → exact `{-1,-2}` unsat core; `TseitinConverterTests` `<= 3*aux+1` → exact 10;
  `EspressoLiteTests` `< original` → exact 52 literals; `AigRewriteLibraryTests` `<= 3` → `== 3`.
- Over-broad / vanity: `CircuitSerializationTests` `ThrowsAny<Exception>` → typed
  `CircuitSerializationException`; `BddSiftingTests` `Stopwatch < 5 s` deleted (the deterministic
  `NodeCount <= staticBest` bound is the real check); `NaryNodeContractTests` vacuous
  `Assert.NotNull(symbol)` fillers → meaningful per-node diagnostics (they were load-bearing only
  for the xUnit1026 unused-parameter analyzer under `TreatWarningsAsErrors`).
- **Metamorphic** strengthened past mere equivalence where v2 canonicalization guarantees more:
  `Renaming_CommutesWithOptimization` now pins BYTE-IDENTICAL output (the constant `"renamed_"`
  prefix preserves canonical variable order); `AigStructure_IsInvariantUnderIdempotentDuplication`
  now compares the canonical `Root` literal, not just the AND-node count (equal counts ≠ equal graphs).
- **ProjectedModelCounting** `ConflictBudget_NeverPassesPartialAsExact` asserted nothing on the
  `Exact` branch (green-passed whenever the seed solved trivially); both branches are now
  falsifiable — an `Exact` result is checked against the brute-force projected oracle.

Duplicates removed (kept the stronger home): `OptimizerTests` absorption rows (subsumed by
`OptimizerTruthTableTests.Optimizer_AbsorptionLaws`); two `EdgeCaseTests` contradiction/tautology
rows and one `OptimizerTruthTableTests` complex row (subsumed by the Tautologies… theory /
`OptimizerTests.SmartCommutativity`); one duplicate `AdvancedPatternDetectorTests` StandardXor row.

Reviewer suggestions REJECTED on verification (not defects):
- `Spikes/ProjectedModelCountingTests` is NOT a duplicate of the facade suite — it exercises a
  DIFFERENT production API (`ProjectedModelCounting.BddExistentialAbstraction`), cross-checked
  exhaustively against brute force and the SAT-blocking count; kept.
- `RuleCompletenessTests`' `<= maxLiterals` is not slack: the budgets are calibrated so the
  non-filler prefix must collapse (e.g. `!a | a & b` → 2 literals under a 14-literal cap), and
  exact-string pins on 13-variable canonical output would be brittle; kept as-is.
- `ArchitectureTests` public-type list vs `ApiSurfaceTests` member-level baseline: kept as a
  deliberate belt-and-suspenders (type-level architectural pin + member-level snapshot).

Net: gate suite 1158 → **1152 cases** (−6 exact-duplicate rows; every removed behavior remains
pinned by a stronger surviving test). All added assertions are independent-oracle (SAT prime-cover,
brute-force on-set/projected, truth-table) or exact-string/exact-count. Suite green (net10.0, CI filter).

**2026-07-28 — contract-honesty change-set audit (tests for the review-driven fixes
re-graded against the four criteria; all ten techniques re-run against the new behavior).**
After the run of contract-honesty fixes (SAT-`Unknown` → rollback, QM budget → `BudgetExceeded`,
unbounded ≤10 guarantee cover, scalable `IsEquivalent`, cooperative global timeout,
`IsOptimal` ⇔ `MinimalProven`, memory/convergence telemetry, typed budget/size exceptions,
`MinimalPosWithStatus` + `CnfMinimizationStatus`), every new/changed test was graded
representative / logically-correct / strong / non-duplicating, and each technique was checked
for a coverage gap on the new functionality:

- **Differential (gap closed):** the whole point of the scalable `IsEquivalent` fix — that it
  no longer throws past the 20-variable truth-table limit — had **no** test. Added three to
  `OptimizationResultTests`: a 25-variable equivalent pair proven via the SAT miter (not a
  truth table), its non-equivalent sibling refuted, and `CheckEquivalence()`'s three-valued
  verdict (proof / counterexample). The SAT engine is the independent oracle.
- **Mutation (new killers):** `SoundnessGuardUnknownTests` kills the guard's `== true` → `!= false`
  mutant (zero-budget `Unknown` must roll back); `OptimizationQualityAnalyzerTests`'
  `AnalyzeOptimization_HighScoreButUnproven_IsNotOptimal` kills `IsOptimal = MinimalProven`
  → `score >= 85`; the typed-exception tests kill "catch base type" mutants; the
  `CnfMinimizationStatus` pair kills "reuse SOP status for CNF".
- **Architecture:** public-surface list repinned 55 → 58 types (the three typed
  budget/size exceptions); the five BDD/d-DNNF/distributive throw-site tests now assert the
  **specific** `NodeBudgetExceededException` / `NormalFormTooLargeException`, not the base
  `InvalidOperationException` (strengthened, and they double as the exact-type contract).
- **Characterization:** golden master regenerated for one honest status change — a dense
  12-variable function now reports `BudgetExceeded` where it previously masqueraded as
  `Heuristic`; every pinned string re-verified equivalent.
- **Property-based / Metamorphic / Algebraic / Fuzzing / Snapshot / Combinatorial:** no new
  relation required. Convergence/allocation telemetry is deliberately kept **out** of
  `OptimizationMetrics.ToString()` so the `DebugInfo`/CLI Verify snapshots stay deterministic;
  it is covered by direct assertions instead (see below).

Fixes applied to the change-set's own tests (held to the same bar as production):
- **Duplicate removed:** `OptimizationMetricsTests.OptimizeExpression_WithoutMetrics_LeavesTelemetryDefault`
  deleted — `PairwiseOptionsTests` already pins `IncludeMetrics || Metrics == null` across the
  full 2⁷ option grid (strictly stronger), and `ConsoleInterfaceTests` covers the single case
  (audit rule 1).
- **Tautological assert strengthened:** `SoundnessGuardUnknownTests` dropped a trivially-true
  `TruthTable.AreEquivalent(input, rolledBack)` (the rollback *is* the input) in favour of the
  precise `Assert.Same(input, optimized)` — reference-equality proves the rewrite was discarded,
  not merely that an equivalent survived (audit rule 5).
- **Direct telemetry tests added:** convergence trace `OptimizationSteps.Count == Iterations + 1`
  with `iter N:` format + node counts, and `AllocatedBytes > 0`, in `OptimizationMetricsTests`.

- **Mutation (technique 3) actually executed on the changed logic**, not just asserted: a
  scoped Stryker run on `RewriteEngine.cs` (75.3%) and `OptimizationQualityAnalyzer.cs` (31.8%)
  confirmed the two mutants that matter are **killed** — the soundness guard's `== true`
  (RewriteEngine.cs:114) and `IsOptimal == MinimalProven` (line 44). The low analyzer score is
  the documented heuristic-constant equivalent class (see Part 5). The run surfaced a real gap
  in the *new* report-rendering code (the `Proven minimal` / convergence / memory lines were
  never asserted as printed); closed by `GenerateQualityReport_WithMetrics_RendersProvenMinimalAndTelemetry`.

Net: 1032 → **1035 cases** (−1 duplicate, +3 scalable-equivalence gap tests, +1 report-render
mutation killer). The added tests are all independent-oracle (SAT miter) or exact-property
(`Assert.Same`, exact status enums, exact report lines); CI-like suite 1035/1035, exhaustive
sweeps 7/7.

**2026-07-27 — v2.0 post-migration re-audit (all ten techniques re-run against changed
functionality).** After the binary→n-ary AST migration, the single-`RewriteEngine` rewrite,
`FormulaFactory` construction-time canonicalization and the API narrowing, six parallel
read-only reviewers re-graded every test file against the four criteria (representative /
logically correct / strong / non-duplicating). Each of the ten techniques was re-validated
against the new behavior and its inventory refreshed:

- **Property-based:** SAT-miter cross-check fixed — it had silently degenerated to
  truth-table-vs-truth-table (generator ≤6 vars never reaches the SAT path); now calls
  `EquivalenceChecker.CheckWithSat` so the miter/Tseitin/SAT stack is the real second engine.
- **Metamorphic:** the two term-permutation MRs now pin **byte-identical** optimized output
  (v2 canonical construction guarantees it) instead of mere equivalence.
- **Algebraic:** idempotence/absorption "collapse" laws + the expression-level complement law
  now assert canonical-string identity / exact `"0"`/`"1"` (were soundness-only, which the
  optimizer satisfies whether or not the rule fires).
- **Differential:** the three-engine equivalence tests now genuinely run the SAT engine
  (`CheckWithSat`); added a >12-variable SAT-miter-vs-BDD test (proof + refutation) so the
  scale path the class exists for has an independent oracle.
- **Fuzzing:** the `FormulaFactory` invariant walker now checks operand dedup, complement-pair
  absence and canonical sortedness (matched to its docstring), not just nested-op/constant.
- **Characterization / Snapshot:** golden master (32 entries) and the 7 Verify snapshots
  regenerated for the v2 canonical strings; equivalence guard re-verified every pinned result.
- **Architecture:** public-surface list repinned to the v2 53-type set; `IRewriteRule`-in-
  `Rewrite`-namespace rule replaces the old `IOptimizer` rule; `ForceParentheses` immutability
  exception removed.
- **Combinatorial / Mutation:** pairwise option grid unchanged (still valid); mutation config
  unchanged — Part 5 numbers predate v2, re-run pending.

Fixes applied (~35 across all folders):
- **Green-masking weakenings fixed:** `ConsensusRule_ValidConsensus_ShouldSimplify` (was a
  `terms.Length <= n` no-op → now pins exact `a & b | c & !a` + 2 terms); the two circular
  SAT-miter cross-checks above.
- **Duplicates removed:** `OptimizerTests` rows subsumed by `OptimizerTruthTableTests`; the
  consensus `AllOptimizationRules_ShouldNotCreateContradictoryTerms` slack-scan (subsumed by the
  exhaustive `OptimizerSoundnessTests`); `ConsoleInterfaceTests`/`ConsoleTestedCasesTests`
  validation/case twins.
- **Wall-clock vanity removed:** the `Stopwatch < 5000 ms` assert in `AdvancedPatternDetectorTests`
  (rule 4) → now asserts all 50 XOR patterns are detected.
- **Missing v2 coverage added:** flat n-ary rendering (`(a && b && c)` C#, 3-input BLIF/Verilog
  gates, single flat AND in the visualizer); n-ary Tseitin clause/aux counts pinned locally;
  canonical-order tie-breaks (Not-vs-Not, equal-complexity composites); `DistributiveExpander`
  `ToDnf`/`ToCnf` + budget-exceeded throw; multi-output minimization redesigned to actually
  exercise `TrySharedCovers` (3 shared cubes strictly beating 4 independent, with an independent
  cube-count oracle); strict folding reduction in `FormulaFactoryTests` (raw un-canonical input,
  not an already-folded tree).
  - *Note:* a gate-visible mid-flight cancellation test was attempted but removed — reliably
    exercising *mid-flight* (not entry-point) cancellation needs a tuned multi-second workload
    that is timing/input-dependent (it flaked in CI both too-fast and too-slow), so mid-flight
    cancellation stays in the Performance suite; the gate keeps the deterministic
    pre-cancelled-token checks in `ResourceBudgetAndCancellationTests`. The attempt also
    surfaced a real product gap — a dense 13-variable `MinimalSop` ran ~41 s without honoring a
    mid-flight token because the branch-and-bound cover search checked only its step limit; this
    was fixed (the cover search, covering-table reductions and greedy fallback now observe the
    token), with a regression test in `MidFlightCancellationTests`.
- **Weak asserts strengthened:** deterministic quality scores pinned exactly (score 85, ratio
  1/9); `↔`/`XOR` detections pinned to exact operands; hand-computed truth-table oracle anchors
  added to the De Morgan laws; constructor-echo `Assert.Same(.Left/.Right)` removed.

Net: 891 → **880 cases** (fewer, stronger — deleted rows were exact-duplicate theory cases;
added cases are all independent-oracle or exact-string). Facade line coverage 90.15%
(branch 82.09%, method 93.53%), gate ≥80%.

**2026-07-24 — full-suite audit and cleanup.** Five parallel reviewers covered all
~700 test methods; five cleanup passes executed the verdicts:

- Deleted outright: `PerformanceTests.cs`, `AstAdvancedFormsPerformanceTests.cs`
  (wall-clock vanity, `Assert.True(true)`, GC pseudo-tests), `AstAdvancedFormsTests.cs`,
  `AdvancedLogicalFormsTests.cs` (test-local re-implementations of XOR/IMP detection —
  circular oracle: production detection could be deleted with all tests staying green).
- Node-template dedup: 60 constructor/Operator/inheritance/clone-echo tests across
  ExtendedOperators/EqvNode/ImpNode files replaced by one parameterized
  `Core/BinaryNodeContractTests.cs` (31 cases over 5 node types).
- Truth-table dedup: laws consolidated to one 11-row theory; tautology/contradiction
  trio kept once; ~28 duplicates removed.
- Strengthened: 9 CSV parser tests (Contains → semantic equivalence — parser proved
  correct), consensus-term removal now asserted structurally, factorization output
  pinned, `SimplifyWithBackbone` contract asserted structurally, `ToAst` round-trip
  verified semantically, default budgets pinned to exact documented values,
  CLI CSV auto-detection actually asserted, invalid-expression path pins the specific
  exception, IMP-detection theories assert the arrow.
- Production observations filed (no code changed): `Advanced` output can carry doubled
  parentheses (`((a XOR b))`); pattern recognition in the facade caps at 5 variables
  while the CLI's separate path has no cap; `OutputFormatter.DisplayTruthTableOnly`
  logs then rethrows; `IsEqvPattern` logic exists in two production classes.

Net: 1043 → 800 executed test cases with zero behavioral coverage lost — every
deleted behavior remains pinned by a stronger surviving test (the drop also reflects
theory-row dedup counted per-case, offset by 14 new tests for Plaisted–Greenbaum
CNF, BDD quantification/composition/ordering, and the SymPy differential corpus).

**2026-07-25 — new-engine coverage + debt closure.** Every technique extended to the
new engines (FormulaFactory, Espresso-lite, AIG, BDD quantification/sifting, PG CNF):
extended-operator CsCheck generators, quantifier laws as canonical-node identities,
four-representation differential, BDD op-chain fuzzing, encoding pairwise grid,
heuristic-zone characterization entries, mid-flight cancellation (which found and
fixed two real token-granularity gaps in QM). Production fixes from the audit
backlog: double-parentheses printing, CLI truth-table error handling (ExitCode
instead of rethrow), pattern-recognition cap unified with the CLI path (5 → 100).

---

## Part 5: Mutation-testing results (per module)

| Module | Score | Verdict on survivors |
|---|---:|---|
| Transformations.cs | 100% | — |
| EspressoLiteMinimizer.cs | 72.5% | 3 real gaps → killer tests in `EspressoLiteTests` (cofactor conflict `\|\|→&&`, constant-1 SOP term, last-variable rebuild bound); the rest are budget-boundary and heuristic-ordering equivalents |
| SatSolver.cs | 52.5% | 121 survivors triaged: real gaps → `SatSolverMutationKillerTests` (ctor/AddClause validation, growth-gated subsumption soundness vs brute force, unsat-core sufficiency contract, zero-budget Unknown); the dominant survivor class is verdict-neutral search heuristics (VSIDS activity/heap ordering, restart schedule, learnt-DB reduction triggers, phase saving) — these change only the search path and are unkillable by correctness assertions by design |
| TruthTableMinimizer.cs | 82.6% | 32 survivors triaged: real gaps → killer tests in `TruthTableMinimizerTests` (MinimalPos don't-care handling; the PROOF-HONESTY sweep asserting that a proven claim under ANY step budget equals the reference optimum — covering the LimitHit/best-update/lower-bound mutant class); the rest are tie-break guards, dominance-gate boundaries and greedy-fallback ordering — cover-equivalent by construction |
| RewriteEngine.cs | 75.3% | 2026-07-28 scoped run. The **soundness-guard mutant that matters is killed**: `.AreEquivalent == true` (RewriteEngine.cs:114) → `!= true` dies to `SoundnessGuardUnknownTests` (a budget-`Unknown` must roll back). Survivors are the bounded **expand-reduce** gate (`MaxExpandReduceInputNodes`/`MaxExpandedNodes` boundary `<`/`>`/`<=`, the reduce-loop iteration bound, and the `strictly-cheaper?reduced:node` guard) — heuristic size cut-offs where either branch stays sound, plus one `SoundnessRollback` `countAsApplied:false→true` metrics-only mutant (a rollback is not an applied rule; not worth a brittle exact-`AppliedRules` pin) |
| OptimizationQualityAnalyzer.cs | 31.8% | 2026-07-28 scoped run. The **honesty mutant that matters is killed**: `IsOptimal = MinimizationStatus == MinimalProven` (line 44) → `!=` dies to `AnalyzeOptimization_HighScoreButUnproven_IsNotOptimal` + `…ShouldIndicateOptimal`. The low score is by design and matches the SatSolver class: `CalculateOptimalityScore` is an explicitly **heuristic** rating (thresholds `0.5/0.7/0.9`, bonuses `+30/+20/+10/+5`) with no ground-truth to pin — every such boundary/constant mutant is equivalent, and `IsOptimal` no longer depends on the score. Real rendering gaps in the new report section were then killed by `GenerateQualityReport_WithMetrics_RendersProvenMinimalAndTelemetry` (asserts the report prints the `Proven minimal (two-level)`, `Convergence trace` and `Allocated memory` lines); the residual `>=0` boundary mutants on those guards are true equivalents (the counters are always ≥1 in a metrics run) |

```powershell
cd LogicalOptimizer.Tests
dotnet stryker --project LogicalOptimizer.Sat.csproj --mutate "**/SatSolver.cs" --solution ..\LogicalOptimizer.sln
dotnet stryker --project LogicalOptimizer.Minimization.csproj --mutate "**/TruthTableMinimizer.cs" --solution ..\LogicalOptimizer.sln
```

Reading the scores honestly: a solver's mutation score is dominated by heuristic
code where ANY behavior is correct (only slower), so 100% is not the goal — the goal
is that every SURVIVOR is either killed by a targeted test or classified as
equivalent, which is the state recorded above.
