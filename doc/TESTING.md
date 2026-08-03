# Testing Strategy — Analysis, Actuality & Checklist

**Last audited:** 2026-07-30 (four-criteria re-audit of the branch change-set plus the
logical-expression core; the ten techniques re-run against current behavior and their inventories
refreshed). The headline finding was a **representativeness** one: the extended-operator rule
library (`ExtendedOptimizationRules` — XOR/NAND/NOR/EQV rules and the NAND/NOR functional-
completeness bases) carried 46 cases that asserted only the SHAPE the implementation happens to
build, over 2-4 hand-picked operand pairs per rule and with no oracle at all — so a rule that
fires on the WRONG operand kind was invisible. Demonstrated, not assumed: widening
`NandRules.ZeroAbsorption` to match any constant (making `NAND(1, a)` return `1` instead of `!a`)
leaves all 52 pre-existing cases green and is caught only by the new sweep. Both files now sit on a
truth-table sweep over a 64-pair operand grid, with a per-rule firing counter so a narrowed guard
cannot leave a rule silently unchecked. Weak assertions pinned
where the output is deterministic (advanced-form rendering, EQV operand pairs, the visualizer's
nested tree, the CLI's `error.code` and no-args message); three of those pins were wrong on first
run and **exposed real contract facts** the old assertions had hidden (`ConvertToAdvancedForms`
returns *null* for null input, not `""`; EQV operand order follows the first conjunct). Vacuity
floors added to the three tests whose real assertion sits behind a `proven`/status filter.
Exact-duplicate rows removed. Six test classes that were hiding in files named after a different
class were given their own file under the folder for their subject, and the layout rule is now a
test rather than a convention. Gate suite 1261 → **1254** green (−11 duplicate/vanity rows, +2
oracle-backed sweeps, +2 layout guards).
**Previous audit:** 2026-07-29 (four-criteria re-audit: nine parallel read-only reviewers covered
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

## Part 0: Running the suite

The canonical commands, fastest first. `tools/test.ps1` is the single entry point; the
raw `dotnet test` equivalents are listed for environments without PowerShell.

| Command | What runs | Expected time |
|---|---|---|
| `./tools/test.ps1` | the fast gate — the CI filter `Category!=Performance&Category!=Exhaustive` | tens of seconds |
| `./tools/test.ps1 -Performance` | `Category=Performance`, collections sequential | minutes |
| `./tools/test.ps1 -Exhaustive` | `Category=Exhaustive`, collections sequential | ~20-40 min |
| `./tools/test.ps1 -Full` | gate, then Performance, then Exhaustive — three separate runs | ~30-50 min |

Shells: on Windows the script runs as-is in **either** Windows PowerShell 5.1 or
PowerShell 7 (`pwsh`) — installing PowerShell 7 is NOT required (verified:
`powershell -File tools\test.ps1 -NoBuild`, 1376/1376). On Linux/macOS run it via
`pwsh tools/test.ps1`.

Rules the script encodes (follow them when running `dotnet test` by hand):

- **The fast gate is the default loop.** A bare unfiltered `dotnet test` is a trap: it
  starts the exhaustive whole-function-space sweeps IN PARALLEL with everything else,
  which is CPU-thrash that looks exactly like a hang (during the 2026-07-31 audit, five
  4-variable sweeps ran concurrently and the run had no visible progress for minutes).
- **Expensive categories run sequentially**, via
  `dotnet test ... -- xUnit.ParallelizeTestCollections=false`. Same total CPU work,
  readable timeline.
- **Long tests announce themselves.** `xunit.runner.json` sets
  `longRunningTestSeconds: 60`; the script's expensive runs add
  `xUnit.DiagnosticMessages=true` and console verbosity `normal`, so the runner
  periodically names every test that has been executing longer than a minute — a
  working sweep is distinguishable from a wedged one. (Diagnostics stay off in the
  fast gate, where they are only noise.)
- **If you kill a test run, check for a leftover `testhost`.** An interrupted run can
  leave a `testhost` process holding the output DLLs, which makes the NEXT build fail
  with a file-lock error that has nothing to do with the code. End the process
  (`Get-Process testhost | Stop-Process`) and rebuild.

CI mapping: `ci.yml` runs the fast gate (with `--blame-hang`, 20 min, and always-uploaded
.trx/sequence artifacts); `exhaustive.yml` runs the whole Exhaustive category nightly,
sequential; `release.yml` runs the gate plus the `ReleaseEvidence` subset, sequential, as
a publish gate.

---

## Part 1: Summary Matrix

This matrix covers the **cross-cutting layer only**. It sits on top of the structured functional
suite organized by subject (Part 2) — the tests that pin what the library must *do*, and the ones
that fail first when a behavior changes. The gate suite is **1255** cases (the 1254 the audit
below ended on, plus one documentation guard added after it — see `Documentation/`):

- **1120** subject tests — Core 165 · Optimizers 196 · Engines 306 · Facade 180 · Analysis 10 ·
  Formats 116 · Cli 92 · Documentation 55;
- **101** in `Techniques/` — the ten below plus the API-surface, claims, package-contract and
  suite-layout guards;
- **24** in the two projected-model-counting suites (the facade one and the `Spikes/` one share a
  class name, so they are counted together);
- **10** the test infrastructure's own tests.

Performance- and Exhaustive-category cases are excluded from that count and run in their own CI
jobs (`Category=Exhaustive` also gates publishing — see the `ReleaseEvidence` category). The
techniques are cross-checks on the subject base, not a replacement for it — read Part 2's audit
rules first.

Status legend: ☑ current and verified in the last audit · ◪ present with known gaps · ☐ not started.

| # | Technique | Applicability | Status | Coverage today | Effort to extend | Priority |
|---|-----------|:---:|:---:|---|:---:|:---:|
| 1 | **Property-Based (CsCheck)** | ★★★★★ | ☑ | 14 properties incl. extended-operator generators; the SAT cross-check verified to still call `CheckWithSat` (not degenerated to truth-table-vs-truth-table), and the proven-minimal irredundancy property now carries a vacuity floor. `Techniques/PropertyBasedTests.cs` | Med | P0 |
| 2 | **Metamorphic** | ★★★★★ | ☑ | 11 relations (permutation AND renaming MRs pin byte-identical canonical output; AIG idempotent-duplication compares the canonical Root), `Techniques/MetamorphicTests.cs` | Med | P0 |
| 3 | **Mutation (Stryker.NET)** | ★★★★☆ | ☑ | per-module runs + killer tests (Part 5); Transformations.cs 100%. 2026-07-30: a hand-planted mutant in `ExtendedOperators.cs` proved the rule library's shape-only tests could not kill an operand-kind mutant; the new truth-table sweep does (Part 5, last row) | Low | P1 |
| 4 | **Algebraic** | ★★★★★ | ☑ | 13 laws — every axiom, with commuted operands additionally pinned to an identical canonical string, `Techniques/AlgebraicLawTests.cs` | Low | P1 |
| 5 | **Snapshot / Approval (Verify)** | ★★★★☆ | ☑ | 8 approved snapshots (CLI default/verbose/**json**, C# exporter, AST visualizer, truth table, debug dump, `OptimizationResult.ToString`), `Techniques/SnapshotTests.cs` | Low | P1 |
| 6 | **Architecture (ArchUnitNET)** | ★★★☆☆ | ☑ | 9 rules incl. the pinned public-API list — now **80 types across seven assemblies** (Core 27 · Sat 14 · Bdd 1 · Dnnf 2 · Formats 9 · Minimization 5 · facade 22) — plus `IRewriteRule`-in-`Rewrite`-namespace, `Techniques/ArchitectureTests.cs` | Low | P2 |
| 7 | **Differential** | ★★★★★ | ☑ | internal-engine suites (10 tests incl. a 16-var SAT-miter-vs-BDD proof+refutation) + SymPy oracle (CI) + Z3 oracle (4 tests, Microsoft.Z3, runs where the native library loads). The proof cross-checks carry vacuity floors, and the EXTERNAL oracles no longer skip silently — `LOGICALOPTIMIZER_REQUIRE_SYMPY` / `_Z3` turn an absent oracle into a failure where it is expected (`ci.yml` sets the SymPy one iff `pip install` succeeded) | Med | P0 |
| 8 | **Fuzzing** | ★★★★☆ | ☑ | 10 deterministic fuzzers; factory-invariant walker checks flattening, dedup, complement pairs, constant-freedom AND sortedness/interning (`Assert.Same` on rebuild), `Techniques/FuzzingTests.cs` | Med | P2 |
| 9 | **Characterization** | ★★★☆☆ | ☑ | 32-expression golden master + an equivalence guard over Optimized AND CNF AND DNF, `Techniques/CharacterizationTests.cs` | Low | P3 |
| 10 | **Combinatorial / Pairwise** | ★★★☆☆ | ☑ | covering array over 7 option axes (self-checked for pair coverage) + full 2⁷ grid | Low | P3 |

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
LogicalOptimizer.Tests/                                                  (gate cases per folder)
├── Core/                  Lexer, Parser (canonical output), AST node contracts, TruthTable   165
│     LexerTests · ParserTests · NaryNodeContractTests (n-ary invariants: ≥2 operands,
│     defensive copy, order-sensitive equality, cached hash, Clone==this; + derived-op
│     contracts) · FormulaFactoryTests (canonicalization + interning) ·
│     FormulaParseDiagnosticsTests (ParseErrorCode / position contract) ·
│     TruthTableGenerationTests · TruthTableMethodTests · TruthTableAdvancedTests ·
│     TruthTableComparisonTests
├── Optimizers/            rewrite rules (RewriteEngine/IRewriteRule), pattern detection      196
│     CanonicalOrderingTests (construction-time sort) · DistributiveExpanderTests ·
│     ConsensusRuleTests · RuleCompletenessTests (13+ vars, past the QM gate) ·
│     OptimizerSoundnessTests · SoundnessGuardUnknownTests (SAT-Unknown → rollback) ·
│     FinalSoundnessGuardTests (the post-rewrite guard: every zone's returned result is
│     proven against the parsed INPUT, incl. the sat-miter zone and the rollback contract) ·
│     SubcircuitRewriteTests · ExtendedOptimizationRulesTests + EqvRulesTests (the
│     XOR/NAND/NOR/EQV rule library and the NAND/NOR functional-completeness bases —
│     `internal`, currently with no production consumer; per-rule shape pins PLUS a
│     truth-table sweep over the whole operand grid, since shape alone re-states the code) ·
│     AdvancedPatternDetectorTests · EqvPatternRecognizerTests · TransformationsTests
├── Engines/                                                                                  306
│   ├── Sat/               SatSolverTests · SatSolverMutationKillerTests ·
│   │                      IncrementalSatTests · DratProofTests ·
│   │                      MaxSatSolverTests · CoreGuidedMaxSatTests ·
│   │                      CoreGuidedMaxSatExhaustiveTests ·
│   │                      SatCorpusRegressionTests (Performance)
│   ├── Bdd/               BinaryDecisionDiagramTests · BddOperationsTests · BddSiftingTests
│   ├── Aig/               AndInverterGraphTests · AigRewriteTests · AigRewriteFacadeTests ·
│   │                      AigRewriteLibraryTests · AigMinLibraryTests ·
│   │                      AigCutEnumerationTests · AigReferenceCountingTests ·
│   │                      NpnCanonicalizationTests
│   ├── Dnnf/              KnowledgeCompilationTests (exact #SAT vs ROBDD) ·
│   │                      DnnfConditioningTests · DnnfSamplingTests
│   ├── Minimization/      TruthTableMinimizerTests · SatTwoLevelMinimizerTests ·
│   │                      MultiOutputSharingTests · EspressoLiteTests
│   ├── Encodings/         TseitinConverterTests (n-ary clause counts) ·
│   │                      CnfEncodingTests (Plaisted–Greenbaum) ·
│   │                      CardinalityEncoderTests · PseudoBooleanEncoderTests ·
│   │                      EncodingPortfolioTests
│   └── Serialization/     CircuitSerializationTests (typed CircuitSerializationException)
├── Analysis/              FormulaAnalysisTests (backbone, model enumeration)                   10
├── Facade/                BooleanExpressionOptimizer end-to-end                               172
│     OptimizerTests · EdgeCaseTests · OptimizerTruthTableTests · NormalFormTests ·
│     AlgebraicNormalFormTests (ANF/Reed–Muller) · OptimizationMetricsTests (n-ary cost
│     model + convergence/allocation telemetry) · OptimizationQualityAnalyzerTests ·
│     OptimizationResultTests (incl. the scalable >20-var IsEquivalent SAT path) ·
│     EquivalenceCheckerTests · EquivalenceCheckerImplementationsTests ·
│     OptimizationTraceTests · ProjectedModelCountingTests · EqvIntegrationTests ·
│     ConsoleTestedCasesTests (unique regression rows) · ConsoleInterfaceTests (limits) ·
│     MidFlightCancellationTests (Performance; mid-flight cancellation is timing-dependent so
│     it lives here, not the gate — the gate has ResourceBudgetAndCancellationTests'
│     deterministic pre-cancelled-token checks) ·
│     ResourceBudgetAndCancellationTests · PerformanceValidatorTests (input validation)
├── Formats/               CsvTruthTableParserTests · MultiOutputCsvTests · ExportTests        116
│                          (n-ary BLIF/Verilog gates) · CSharpExpressionExporterTests
│                          (flat n-ary render) · AstVisualizerTests · DimacsFormatTests ·
│                          WcnfFormatTests · OpbFormatTests · FormatFuzzingTests
├── Cli/                   CommandLineProcessorTests · OutputFormatterTests ·                  92
│                          JsonReportWriterTests · CliReportSchemaTests (writer vs the
│                          published schema/cli-report-v1.schema.json) ·
│                          CliJsonInputContractTests (drives Program.Main: one JSON document
│                          on stdout, progress on stderr, documented exit codes, CSV seam) ·
│                          PublishedCliSchema + ConsoleCollection (shared fixtures — the
│                          collection serializes every suite that redirects Console)
├── Documentation/         DocExamplesTests (every runnable doc recipe asserts its own          55
│                          output; the flag set AND the standard-format verb set are
│                          compared against what the docs list) · DocumentedCliOutputTests
│                          (each documented CLI transcript compared line-for-line against
│                          the real formatter)
├── Spikes/                ProjectedModelCounting spike + its tests (a DIFFERENT production
│                          API from the facade suite: BddExistentialAbstraction)
├── Techniques/            the ten cross-cutting suites (see Part 3)                           101
│                          + CanonicalInvariantTests (v2 interning/canonical-shape/Tseitin
│                          counts) + ApiSurfaceTests (member-level baseline) +
│                          ClaimsConsistencyTests (doc/CLAIMS.md vocabulary) +
│                          MetaPackageTests (package contract) +
│                          TestSuiteLayoutTests (this hierarchy, enforced — see below) +
│                          Z3DifferentialTests / SymPyDifferentialTests + snapshots
├── TestInfrastructure/    RandomExpressions · TruthTableAssert · SatTestOracles ·              10
│                          DimacsCnf · SatCorpusGenerator · ExpressionGeneratorTests
└── TestData/              characterization.golden.txt · PublicApi.approved.txt ·
                           Serialization/
```

**The hierarchy is enforced, not just described.** `Techniques/TestSuiteLayoutTests.cs` requires that
every class declaring an xUnit test method lives in a file named after it, and that no test class sits
in the project root. That is the whole rule — deliberately NOT a rule about which folder anything
belongs in, since folder placement is a judgement call and encoding taste in a test makes it brittle.
It exists because the tree above silently stopped being true: `EquivalenceCheckerTests` lived inside
`Engines/Sat/SatSolverTests.cs`, so it appeared in no folder tally and no version of this tree, and
was found only by accident. A test that cannot be located by subject is one somebody duplicates later.
Helper and fixture types (`SnapshotSetup`, `ConsoleCollection`, the `TestInfrastructure` oracles) are
exempt by construction — they declare no test methods. A wholesale re-layout was deliberately NOT
done: no criterion in the four (representative / logically correct / strong / non-duplicating) is a
property of file placement, and namespaces stay flat (`LogicalOptimizer.Tests`) because per-folder
namespaces would add a `using` to every file for no test-quality gain.

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
6. **A shape assertion is not an oracle.** Asserting that a rewrite returned
   `Not(And(l, r))` re-states the implementation's own formula: it survives a rule that
   returns the wrong constant, the wrong operand or the wrong polarity, because the
   expectation was read off the code. Pair every structural pin on a *semantic*
   transformation with an independent evaluation (truth table over the operands is
   usually enough), or the test only proves the code equals itself.
7. **A filtered assertion needs a floor.** When the assertion that matters sits behind
   `if (!proven) continue`, a status guard or a size cap, count how often it actually ran
   and assert a floor. Otherwise the day the filter stops matching, the test reports green
   while checking nothing — the failure mode is indistinguishable from success.

---

## Part 3: Techniques — how each direction works here

### Property-based (CsCheck) — `Techniques/PropertyBasedTests.cs`

Generated ASTs (depth ≤ 4, up to 6 variables, constants included) run through
universally quantified properties: optimization preserves semantics against the
truth-table oracle, re-optimization never increases literal count, print→parse→print
is a fixpoint, whitespace at token boundaries is insignificant, Tseitin stays linear
in formula size, and every term of a proven-minimal DNF is irredundant. That last one
(`Property_ProvenMinimalDnfIsIrredundant`) is deliberately named for what it proves:
irredundancy is *necessary* for minimality, not sufficient — the sufficient direction is
cross-checked against an independent minimizer in `DifferentialEngineTests`. It sits behind
four filters, so it counts how many terms actually reached the assertion and fails if the
property went (near-)vacuous. CsCheck shrinks failures and prints a reproducing seed
(`Set seed: "..." to reproduce`).

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

The extended-operator axioms (XOR/NAND/NOR/EQV neutral, absorbing, idempotent and complement
laws, plus the NAND-only and NOR-only functional-completeness bases) are held to the same bar in
`Optimizers/ExtendedOptimizationRulesTests.cs`: each rule is applied across an operand grid and
every rewrite it performs must agree with its input on all four assignments over {a, b}. The
per-rule structural pins stay as the *behavior* contract (which node comes back, and that the rule
declines to fire otherwise), but they are no longer the only evidence — a shape read off the
implementation cannot detect a wrong constant or a flipped polarity (Part 2, audit rule 6).

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

Eight approved files under `Techniques/SnapshotTests.*.verified.txt`: CLI output (default,
verbose and the `--format=json` report), the C# exporter, the AST visualizer, truth-table
rendering, the debug dump and `OptimizationResult.ToString()`. A change produces
`*.received.txt`; approve by replacing the verified file. Note the deliberate boundary —
convergence and allocation telemetry is kept OUT of `OptimizationMetrics.ToString()` so these
snapshots stay deterministic; it is covered by direct assertions in `OptimizationMetricsTests`.

### Architecture (ArchUnitNET + reflection) — `Techniques/ArchitectureTests.cs`

The library never touches `System.Console` and never references CLI/test frameworks;
the rewrite pipeline stays internal; every rewrite rule (`IRewriteRule`) lives in the
`LogicalOptimizer.Rewrite` namespace; AST nodes are sealed/abstract and **fully immutable**
(no exceptions — the v1 `ForceParentheses` display-hint setter was removed in v2, rendering
now flows through the precedence-based `AstFormatter`); expensive public entry points take a
`CancellationToken`; the public API surface is pinned to an explicit reviewed list of 80 types
across the seven assemblies (Core 27 · Sat 14 · Bdd 1 · Dnnf 2 · Formats 9 · Minimization 5 ·
facade 22) — any new public type fails the build until consciously added, and any removal
(the v2 narrowing:
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

**2026-07-30 — four-criteria re-audit of the branch change-set and the logical-expression core;
ten techniques re-run and their inventories refreshed.** Every test added or changed on
`perf/minimizer-and-nuget-verify` was graded representative / logically-correct / strong /
non-duplicating, the expression core (Core, Optimizers, the facade laws) was re-graded, and the
whole suite was swept mechanically for the anti-patterns rules 1–7 name.

Representativeness — the extended-operator rule library:
- `ExtendedOptimizationRulesTests` (35 cases) + `EqvRulesTests` (11) exercised
  `ExtendedOptimizationRules` — an `internal` class with **no production consumer** — and every
  assertion was a SHAPE assertion read off the implementation (`Assert.IsType<NotNode>` then
  `Assert.Same(a, notNode.Operand)`), over 2-4 hand-picked operand pairs per rule. Those pins are
  tight on the pairs they cover; the gap is what they never cover. Verified with a planted mutant
  rather than argued: widening `NandRules.ZeroAbsorption`'s guard from
  `ConstantNode { Value: false }` to `ConstantNode` — so `NAND(1, a)` returns `1` where the answer
  is `!a` — left **all 52 pre-existing cases green**, because no test ever fed a `1` to that rule.
- The six `FunctionalCompleteness` cases were the sharpest case of the same thing: they asserted
  that `ThroughNand.Or(a, b)` builds `NAND(NAND(a,a), NAND(b,b))` without ever checking that the
  result computes `a | b` — the one thing a functionally complete basis is *for*.
- Fixed by adding two oracle-backed tests rather than deleting the file: a sweep applying all 17
  rules across an 8×8 operand grid (constants, both polarities of two variables, a composite and its
  negation) and asserting **pointwise truth-table equality between input and rewrite**, and a
  semantic check of both NAND and NOR bases against the basic operators they claim to reproduce.
  The sweep counts firings per rule and fails if any rule never matched, so a narrowed guard cannot
  quietly leave a rule unchecked (rules 6 and 7, both added to Part 2 from this audit). The planted
  mutant above now dies with a readable message naming the operand pair and the assignment.
- The dead-code observation is filed, not acted on: the library is `internal` and unreferenced, so
  removing it is a production decision, not a test-suite one.

Weak assertions pinned — and three of the pins were wrong on first run, which is the point:
- `AdvancedPatternDetectorTests.ConvertToAdvancedForms_VariousPatterns` `Contains("XOR")`/`Contains("→")`
  → exact renderings (`"a XOR b"`, `"a → b"`).
- `ConvertToAdvancedForms_NullInput` asserted `string.IsNullOrEmpty` — an unfalsifiable disjunct.
  Pinning it revealed the method returns **null**, not `""`, for null input (`""` in → `""` out).
  The test is renamed to state that, so a caller that concatenates the result can rely on it.
- The two `EqvPattern_WithCommutativeAnd_*` tests asserted only `Assert.IsType<EqvNode>` and
  accepted an EQV over any pair at all. Pinning the operands revealed the detector's order follows
  the **first conjunct of the first AND** (`(b & a) | …` → `b ↔ a`) — correct, since ↔ is
  symmetric, and now pinned rather than assumed.
- `AstVisualizerTests.VisualizeTree_DeepNesting` `lines.Length >= 6` + six `Contains` checks →
  the exact seven-line tree, so a flat or mis-indented render fails (the indentation is the only
  thing the test exists to check).
- `CliJsonInputContractTests` `Assert.NotEmpty(error.code)` → the exact `processing_error` code
  (a consumer branches on it; "non-empty" accepted any code at all);
  `CommandLineProcessorTests` `Assert.NotEmpty(ErrorMessage)` → the exact no-args message.

Vacuity floors (rule 7) added where the load-bearing assertion sits behind a filter:
- `PropertyBasedTests.Property_ProvenMinimalDnfIsIrredundant` — four filters (variable count,
  `MinimalProven`, DNF availability, multi-term cover) stand between the generator and the
  assertion; ~27-40 terms actually reach it, so a floor of 15 now fails a collapse toward zero.
  Also **renamed** from `…IsNeverBeatenByAnyEquivalentCover`: it asserts irredundancy, which is
  necessary but not sufficient for minimality, and the docstring now says which test covers the
  sufficient direction.
- `DifferentialEngineTests.SatCoverMinimizer_AgreesWithQuineMcCluskey` and
  `…EspressoAndSubcircuitRewrites_AgreeWithExactMinimumOnCost` — both gate their cost comparison
  (the part that cross-checks the *proof*) on `proven`; floors of 10 and 15 comparisons added.

Duplicates removed (kept the stronger home): `ConsoleTestedCasesTests`' `(a | b) & (a | c)` and
`a & (b | c) & d` rows (verbatim copies at equal strength of `OptimizerTruthTableTests.
Optimizer_Factorization` and `OptimizerTests.Optimizer_SmartCommutativity` — the 2026-07-29 pass
deduped these out of `OptimizerTruthTableTests` but missed the console corpus);
`TruthTableAdvancedTests.TruthTable_BasicLaws`' distributivity dual row (the same pair is checked
per-row against a C# oracle by `TruthTable_FactorizationEquivalence` in the same file);
`TruthTableMethodTests.TruthTable_GetResultsString_ShouldReturnCorrectFormat` (a strict subset of
`TruthTableGenerationTests.TruthTable_TwoVariables_AND`) and `…EquivalenceReflexivity` (reflexivity
against the SAME instance, already pinned across two instances plus symmetry); six rename-only
theory rows in the two EQV suites (renaming invariance is pinned globally by the metamorphic MR).

**The techniques were checked for EXECUTION, not just for a green result** — "passed" and "actually
ran" are different claims, and for a self-skipping suite they come apart:
- **SymPy oracle (technique 7) was a green no-op.** `MinimalDnf_AgreesWithSymPyQuineMcCluskey`
  opened with `if (!SympyAvailable()) return;`, so on a machine without sympy it reported **Passed**
  in 105 ms having asserted nothing — indistinguishable in the results from a run where SymPy
  agreed on all 30 cases. The same shape guarded the four Z3 tests. This is audit rule 7 one level
  up: the failure mode is not a weak assertion but a whole technique quietly leaving the suite.
  Fixed with `TestInfrastructure/ExternalOracle.cs`: an absent oracle still skips locally, but
  `LOGICALOPTIMIZER_REQUIRE_SYMPY` / `_Z3` turn the skip into a failure naming the oracle and the
  fix. `ci.yml` now sets the SymPy flag **iff `pip install sympy` succeeded**, so a sympy that stops
  importing goes red instead of vanishing, while a registry outage stays non-fatal (the install step
  is `continue-on-error` and visibly failed in the log).
  Its corpus floors were also moved AHEAD of the availability gate — they need no python, so a
  corpus that stopped producing comparable cases is now reported even where the oracle cannot run —
  and `Assert.NotEmpty(cases)` became real floors (≥25 of 30 with a computed DNF, ≥20 `MinimalProven`,
  since the cost comparison that cross-checks the *proof* only fires on those).
- **Z3 oracle verified to genuinely run here:** 0.45-1 s per test, not the 0 ms of an early return.
  It stays un-required in CI because the ubuntu-latest native library does not match these bindings.
- **SymPy could not be executed on this machine** (the local pip index is a private registry that
  rejects the download), so the SymPy half of technique 7 is unverified in this audit and rests on
  CI. That is exactly why the strict flag was added rather than left as a comment.
- **Performance category run** (29/29 green): the five `MidFlightCancellationTests` were checked for
  degeneration into entry-point cancellation and are genuine — 242 ms-2 s of real work before the
  token fires. **Exhaustive category run** (13/13 green, 21 min). No stray `*.received.txt` outside
  `bin/` and `TestData/PublicApi.received.txt` (gitignored), so no snapshot or golden-master drift
  is sitting unapproved.

Hierarchy — a surgical pass, not a re-layout. Six test classes were hiding in files named after a
different class, which is how `EquivalenceCheckerTests` (a **facade** type) came to live in
`Engines/Sat/SatSolverTests.cs` and appear in no folder tally, no Part 2 tree and no coverage
discussion until a Performance run surfaced it by accident. MaxSat coverage was split across three
folders, one of them `Techniques/` — which by this document's own definition holds cross-cutting
suites, not subjects. Each was given its own file under the folder for its subject:
`EquivalenceCheckerTests` and `EquivalenceCheckerImplementationsTests` → `Facade/`;
`MaxSatSolverTests`, `CoreGuidedMaxSatTests`, `CoreGuidedMaxSatExhaustiveTests` → `Engines/Sat/`;
`PseudoBooleanEncoderTests` split out and `CardinalityAndMaxSatTests.cs` renamed to
`CardinalityEncoderTests.cs`; `TruthTableComparisonTests` → `Core/`; `MultiOutputSharingTests` →
`Engines/Minimization/`. Then the rule was made a test rather than a convention
(`TestSuiteLayoutTests`, +2 cases), because an unenforced layout convention decays exactly the way
this tree did — negative-tested by renaming a file and confirming the failure names the class.
A full re-layout (per-folder namespaces, ~105 files) was rejected: it changes no assertion, and
"ideal hierarchy" has no oracle.

Techniques re-validated against current behavior (Part 1 inventories refreshed): the critical
failure mode — a degenerated cross-engine miter — was re-checked and is NOT present
(`PropertyBasedTests` and `DifferentialEngineTests` both call `EquivalenceChecker.CheckWithSat`
explicitly, so the second engine really runs on ≤6-var inputs). Stale numbers corrected: Verify
snapshots 7 → **8** (the branch added `CliOutput_Json`), the pinned public surface 58 types across
six assemblies → **80 across seven** (Core 27 · Sat 14 · Bdd 1 · Dnnf 2 · Formats 9 ·
Minimization 5 · facade 22). Part 2's hierarchy was two dozen files out of date and now lists the
Aig/Dnnf/Serialization engine suites, the Formats parsers, the CLI schema/contract suites, the
`Documentation/` folder and `Spikes/`, with per-folder gate case counts.

Net: gate suite 1261 → **1254 cases** (−11 duplicate/vanity rows, +2 oracle-backed sweeps, +2
layout guards); every removed behavior is still pinned by a stronger surviving test. Runs on this
audit: gate suite 1254/1254 green, `Category=Exhaustive` 13/13 green (21 min — the 3- and 4-variable soundness and
`MinimalProven` sweeps, the 65 536-function AIG library check and the core-guided MaxSat sweep),
`Category=Performance` 29/29 green. Not done and worth doing: a scoped Stryker score for
`ExtendedOperators.cs` (see Part 5) and a local SymPy run (blocked by the pip index here).

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
| ExtendedOperators.cs | n/a (targeted) | 2026-07-30. No scoped Stryker score yet — a run was started and abandoned as not worth the wall-clock on an `internal`, unreferenced library. Instead the gap was demonstrated directly: the operand-kind mutant `NandRules.ZeroAbsorption` `ConstantNode { Value: false }` → `ConstantNode` (so `NAND(1, a)` = `1`, not `!a`) **survived all 52 shape-only cases** and is killed by `EveryRule_WhenItFires_PreservesSemantics`. The rules are 3-6 lines each with no branching beyond the guard, so the mutant classes are exactly: wrong constant returned (killed by the existing exact-string pins), wrong operand returned (killed by the existing `Assert.Same` pins), and wrong operand KIND matched (killed only by the new sweep) |
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
