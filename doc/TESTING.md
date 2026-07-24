# Testing Strategy — Analysis, Actuality & Checklist

**Last audited:** 2026-07-24 (full-suite audit: every test method reviewed for duplication,
tautology, circular oracles and assertion strength; ~180 garbage/duplicate tests removed,
~30 weak tests strengthened, two whole files with test-local re-implementations of
production logic deleted).
**Library:** LogicalOptimizer

---

## Part 1: Summary Matrix

Status legend: ☑ current and verified in the last audit · ◪ present with known gaps · ☐ not started.

| # | Technique | Applicability | Status | Coverage today | Effort to extend | Priority |
|---|-----------|:---:|:---:|---|:---:|:---:|
| 1 | **Property-Based (CsCheck)** | ★★★★★ | ☑ | 14 properties incl. extended-operator generators, `Techniques/PropertyBasedTests.cs` | Med | P0 |
| 2 | **Metamorphic** | ★★★★★ | ☑ | 7 relations, `Techniques/MetamorphicTests.cs` | Med | P0 |
| 3 | **Mutation (Stryker.NET)** | ★★★★☆ | ☑ | per-module runs + killer tests (Part 5); Transformations.cs 100% | Low | P1 |
| 4 | **Algebraic** | ★★★★★ | ☑ | all axioms as laws, `Techniques/AlgebraicLawTests.cs` | Low | P1 |
| 5 | **Snapshot / Approval (Verify)** | ★★★★☆ | ☑ | 7 approved snapshots, `Techniques/SnapshotTests.cs` | Low | P1 |
| 6 | **Architecture (ArchUnitNET)** | ★★★☆☆ | ☑ | 8 rules incl. pinned public-API list, `Techniques/ArchitectureTests.cs` | Low | P2 |
| 7 | **Differential** | ★★★★★ | ☑ | 7 internal-engine suites + SymPy oracle (CI) + Z3 oracle (Microsoft.Z3, runs locally) | Med | P0 |
| 8 | **Fuzzing** | ★★★★☆ | ☑ | 7 deterministic fuzzers, `Techniques/FuzzingTests.cs` | Med | P2 |
| 9 | **Characterization** | ★★★☆☆ | ☑ | 31-expression golden master, `Techniques/CharacterizationTests.cs` | Low | P3 |
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
├── Core/                  Lexer, Parser, AST node contracts, TruthTable engine
│     LexerTests · ParserTests · BinaryNodeContractTests (parameterized over
│     Xor/Nand/Nor/Eqv/Imp) · TruthTableGenerationTests · TruthTableMethodTests ·
│     TruthTableAdvancedTests
├── Optimizers/            individual rewrite rules, pattern detection
│     CommutativityOptimizerTests · DistributiveOptimizerTests · ConsensusRuleTests ·
│     RuleCompletenessTests · OptimizerSoundnessTests · ExtendedOptimizationRulesTests ·
│     EqvRulesTests · AdvancedPatternDetectorTests · EqvPatternRecognizerTests ·
│     TransformationsTests
├── Engines/
│   ├── Sat/               SatSolverTests · IncrementalSatTests · DratProofTests
│   ├── Bdd/               BinaryDecisionDiagramTests · BddOperationsTests
│   ├── Minimization/      TruthTableMinimizerTests · SatTwoLevelMinimizerTests
│   └── Encodings/         TseitinConverterTests · CnfEncodingTests (Plaisted–Greenbaum) ·
│                          CardinalityAndMaxSatTests
├── Analysis/              FormulaAnalysisTests (backbone, model enumeration)
├── Facade/                BooleanExpressionOptimizer end-to-end
│     OptimizerTests · EdgeCaseTests · OptimizerTruthTableTests · NormalFormTests ·
│     OptimizationMetricsTests · OptimizationQualityAnalyzerTests ·
│     OptimizationResultTests · EqvIntegrationTests · ConsoleTestedCasesTests
│     (historical regression corpus) · ConsoleInterfaceTests (limits) ·
│     ResourceBudgetAndCancellationTests · PerformanceValidatorTests (input validation)
├── Formats/               CsvTruthTableParserTests · MultiOutputCsvTests · ExportTests ·
│                          CSharpExpressionExporterTests · AstVisualizerTests
├── Cli/                   CommandLineProcessorTests · OutputFormatterTests
├── Techniques/            the ten cross-cutting suites (see Part 3) + snapshots
├── TestInfrastructure/    RandomExpressions · TruthTableAssert · SatTestOracles ·
│                          ExpressionGeneratorTests
└── TestData/              characterization.golden.txt
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
zone) the proven-minimal cost.

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
disprove the proof). The external corpus pits our proven-minimal DNF against SymPy's
Quine–McCluskey (`simplify_logic`): equivalence required, and SymPy must never find a
smaller cover. Runs where `python`+`sympy` exist (CI installs it; skips silently otherwise).

### Fuzzing — `Techniques/FuzzingTests.cs`

In-process, deterministic (fixed seeds): garbage-alphabet parser fuzzing (only
`ArgumentException` may escape), mutation fuzzing of valid expressions, grammar
fuzzing through the whole pipeline with a truth-table oracle, corrupted-CSV fuzzing,
random-CNF solver fuzzing against a brute-force SAT oracle (returned models
re-verified), and limit probing (nesting cap, 10 000-char cap, wide flat chains).

### Characterization — `Techniques/CharacterizationTests.cs` + `TestData/characterization.golden.txt`

The complete observable output over a fixed 31-expression corpus is pinned. Any drift
fails the test and writes `.received.txt`. Regenerate intended changes with
`LOGICALOPTIMIZER_REGENERATE_GOLDEN=1` and review the diff; a guard test re-verifies
every pinned result is still semantically equivalent, so the golden master can never
legitimize a wrong answer.

### Snapshot / approval (Verify) — `Techniques/SnapshotTests.cs`

CLI output (default + verbose), C# exporter, AST visualizer, truth-table rendering
and the debug dump are approved as `Techniques/SnapshotTests.*.verified.txt`. A change
produces `*.received.txt`; approve by replacing the verified file.

### Architecture (ArchUnitNET + reflection) — `Techniques/ArchitectureTests.cs`

The library never touches `System.Console` and never references CLI/test frameworks;
the rewrite pipeline stays internal; every `IOptimizer` lives in the Optimizers
namespace; AST nodes are sealed/abstract and immutable (`ForceParentheses` is the one
documented display-hint exception); expensive public entry points take a
`CancellationToken`; the public API surface is pinned to an explicit reviewed list —
any new public type fails the build until consciously added.

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

```powershell
cd LogicalOptimizer.Tests
dotnet stryker --project LogicalOptimizer.Sat.csproj --mutate "**/SatSolver.cs" --solution ..\LogicalOptimizer.sln
dotnet stryker --project LogicalOptimizer.Minimization.csproj --mutate "**/TruthTableMinimizer.cs" --solution ..\LogicalOptimizer.sln
```

Reading the scores honestly: a solver's mutation score is dominated by heuristic
code where ANY behavior is correct (only slower), so 100% is not the goal — the goal
is that every SURVIVOR is either killed by a targeted test or classified as
equivalent, which is the state recorded above.
