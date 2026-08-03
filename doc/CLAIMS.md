# Claims: what each word means, and what backs it

Every claim LogicalOptimizer makes in public — README, package descriptions, the documentation
site, comparison pages — uses the vocabulary defined here. Each entry states the **approved
wording**, the **precise meaning**, the **evidence** (an executable test, a CI check, or a
versioned artifact you can open), and the **limits** — what the claim deliberately does *not*
assert.

This file is not decoration. `ClaimsConsistencyTests` fails the build if a public-facing document
uses a banned unqualified phrasing or an absolute superlative, and if any evidence reference below
stops resolving to a real file or a real test method. So a claim cannot outlive the thing that
backs it.

**If you want to add a claim:** add it here first, with its evidence, then use it. If there is no
executable test, CI check or versioned artifact to point at, say so in the Limits column instead of
finding softer wording.

---

## `verified`

> **Approved wording:** "every optimization is verified equivalent to the input", "verified
> Boolean reasoning", "equivalence-verified".

**Meaning.** Before `OptimizeExpression` returns, the expression it is about to return is checked
against the **parsed input** by an oracle independent of whichever engine produced it: an exhaustive
truth-table comparison up to 12 variables, a CDCL SAT miter (`EquivalenceChecker`) beyond that. If
the check does not *prove* equivalence — whether it refutes the candidate or the SAT budget runs out
— the pipeline **rolls back to the input**, drops the minimality claim to `Heuristic`, and re-derives
the normal forms from the input, rather than returning an unverified result.

There are two guards, and the claim rests on the second:

| Guard | Covers |
|---|---|
| `RewriteEngine`'s guard | the rewrite phase only — its output against its own input |
| **The final guard** in `BooleanExpressionOptimizer` | the **returned** expression against the parsed input, whichever engine produced it: exact Quine–McCluskey, the SAT prime cover, the subcircuit library, AIG rewriting, or the rewriter |

The second matters because every path except the rewriter *imports a candidate built by a different
engine*, and the winner of the cost comparison would otherwise never be compared to the input at
all. `OptimizationResult.IsEquivalent()` re-runs the same check on demand; it is a consumer-facing
report of a property already established, not the first time it is checked.

**Evidence.**

| What | Where |
|---|---|
| The final guard runs in every zone (exact QM, SAT prime cover, rewrite) and proves the returned result | [`FinalSoundnessGuardTests.EveryZone_RunsTheFinalGuard_AndProvesTheReturnedResult`](../LogicalOptimizer.Tests/Optimizers/FinalSoundnessGuardTests.cs) |
| A non-equivalent candidate is refused — including the dropped-minterm shape of the historical consensus bug | [`FinalSoundnessGuardTests.NonEquivalentCandidate_IsRefused`](../LogicalOptimizer.Tests/Optimizers/FinalSoundnessGuardTests.cs) |
| When equivalence cannot be *proven*, the input is returned and no minimality is claimed | [`FinalSoundnessGuardTests.WhenEquivalenceCannotBeProven_TheInputIsReturnedInsteadOfAnUnverifiedResult`](../LogicalOptimizer.Tests/Optimizers/FinalSoundnessGuardTests.cs) |
| The two verification routes (reused ON-set, full truth table) are the same predicate over every ordered pair of 3-variable functions | [`FinalSoundnessGuardTests.OnSetReuse_AgreesWithTheFullTruthTable_OnEveryThreeVariableFunctionPair`](../LogicalOptimizer.Tests/Optimizers/FinalSoundnessGuardTests.cs) |
| All 254 non-constant 3-variable functions preserve semantics, and the rewrite guard never fires | [`OptimizerSoundnessTests.Optimize_AllThreeVariableFunctions_PreserveSemantics`](../LogicalOptimizer.Tests/Optimizers/OptimizerSoundnessTests.cs) |
| All 65534 non-constant 4-variable functions, same two assertions. Re-run as a **gate before every publish** (`ReleaseEvidence`) and recorded in the release evidence bundle | [`OptimizerSoundnessTests.Optimize_AllFourVariableFunctions_PreserveSemantics`](../LogicalOptimizer.Tests/Optimizers/OptimizerSoundnessTests.cs) |
| The pinned golden corpus stays equivalent for `Optimized`, `CNF` *and* `DNF` — so the golden master cannot legitimize a wrong pinned result | [`CharacterizationTests.GoldenCorpus_EveryPinnedResultIsStillEquivalent`](../LogicalOptimizer.Tests/Techniques/CharacterizationTests.cs) |
| Independent external oracle: Z3 agrees on equivalence | [`Z3DifferentialTests`](../LogicalOptimizer.Tests/Techniques/Z3DifferentialTests.cs) |
| Property-based and metamorphic sweeps over generated expressions | [`PropertyBasedTests`](../LogicalOptimizer.Tests/Techniques/PropertyBasedTests.cs), [`MetamorphicTests`](../LogicalOptimizer.Tests/Techniques/MetamorphicTests.cs) |

**Limits.**

- This is a **per-result check by an in-process oracle**, not a machine-checked proof of the
  library's own correctness. It catches an unsound result on the input it was given; it is not a
  formal verification of the optimizer.
- Above 12 variables the oracle is a **budgeted** SAT miter. An exhausted budget is not treated as
  success: the input comes back untouched. Tightening `ResourceBudget.SoundnessGuardConflictLimit`
  far enough will therefore trade optimization away for the guarantee — deliberately, in that order.
- There has been **no external formal audit** of the code.
- `verified` says nothing about performance. See `benchmark result` below.
- The CLI report's `equivalent` field is documented as a boolean, but the optimize path is not
  expected to emit `false`: the guard runs before the result is returned, so `false` would indicate
  a bug worth reporting. See [`schema/README.md`](../schema/README.md).
- "the input" here means **the expression that was parsed and optimized**. When the CLI is given a
  CSV truth table it first *derives* an expression from the table; the guard then verifies the
  result against that derived expression — reported as `analyzedExpression` — and not against the
  table itself. The derivation is ordinary sum-of-products construction, not something this claim
  covers.

---

## `minimal` / `MinimalProven`

> **Approved wording:** "provably minimal", "`MinimalProven`", "optimality is reported explicitly
> when proven". Never bare "minimal" without the status or the cost model nearby.

**Meaning.** Minimality is claimed **only** under a stated cost model: the minimum two-level cover,
ranked by total literal count first, then term count. `OptimizationResult.MinimizationStatus` is
one of three values and is never silently downgraded:

- `MinimalProven` — the exact minimum-cover search completed;
- `BudgetExceeded` — the exact search ran but hit its work budget, so the result is sound but not
  proven optimal;
- `Heuristic` — outside the exact range; rule-based simplification only.

The returned multi-level expression never has more literals than that cover.

**Evidence.**

| What | Where |
|---|---|
| Every non-constant 3-variable function reports `MinimalProven` **and** is still the input function | [`TruthTableMinimizerTests.OptimizeExpression_AllThreeVariableFunctions_MinimalProven`](../LogicalOptimizer.Tests/Engines/Minimization/TruthTableMinimizerTests.cs) |
| All 65534 non-constant 4-variable functions, same two assertions. Re-run as a **gate before every publish** (`ReleaseEvidence`) and recorded in the release evidence bundle | [`TruthTableMinimizerTests.OptimizeExpression_AllFourVariableFunctions_MinimalProven`](../LogicalOptimizer.Tests/Engines/Minimization/TruthTableMinimizerTests.cs) |
| A `proven` claim under *any* budget really is the true minimum, against a brute-force oracle | [`TruthTableMinimizerTests.MinimalSopWithStatus_ProvenClaimUnderAnyBudget_ImpliesTrueMinimum`](../LogicalOptimizer.Tests/Engines/Minimization/TruthTableMinimizerTests.cs) |
| A tiny step limit downgrades the status instead of over-claiming | [`TruthTableMinimizerTests.MinimalSopWithStatus_TinyStepLimit_ReportsUnproven`](../LogicalOptimizer.Tests/Engines/Minimization/TruthTableMinimizerTests.cs) |
| Beyond the exact range the status becomes `Heuristic`, not a silent proven | [`TruthTableMinimizerTests.OptimizeExpression_BeyondExactRange_ReportsHeuristic`](../LogicalOptimizer.Tests/Engines/Minimization/TruthTableMinimizerTests.cs) |
| Independent exact minimizer (SymPy Quine–McCluskey) agrees on literal count where we report proven | [`SymPyDifferentialTests`](../LogicalOptimizer.Tests/Techniques/SymPyDifferentialTests.cs) |

**Limits.**

- The cost model is **literals, then terms, on the two-level cover** — *not* minimal gate count,
  circuit depth, delay, or area. It is not a logic-synthesis quality claim.
- The `MinimalProven` guarantee zone is **≤10 variables**. At 11–12 the exact search runs under a
  budget and may legitimately end as `BudgetExceeded`. Above that the result is `Heuristic`.
- Exhaustive verification covers 3 and 4 variables. For 5–10 variables the guarantee rests on the
  algorithm plus randomized and property-based tests, not on an exhaustive sweep.
- The 4-variable sweep is too slow for a per-push gate, so it runs before **publishing**, not on
  every commit: a commit on a branch has not necessarily been through it. The full `Exhaustive`
  category runs nightly ([`exhaustive.yml`](../.github/workflows/exhaustive.yml)).
- `CnfMinimizationStatus` is reported separately from the top-level status; a proven SOP does not
  imply a proven POS.

---

## `dependency-free` / `no third-party runtime dependency`

> **Approved wording:** "zero third-party runtime dependencies", "no third-party runtime
> dependency", "pure managed". The short form "dependency-free" is acceptable in a headline
> **only** where the precise form appears nearby; the phrasings "zero production dependencies" and
> "zero runtime dependencies" are **banned**, because a LogicalOptimizer package does reference
> other LogicalOptimizer packages.

**Meaning.** No package that ships references any package outside the `LogicalOptimizer.*` family.
Since v4.0 the library is ONE package carrying seven assemblies whose internal dependency graph is
acyclic and downward-only; the deprecated forwarding shells reference only `LogicalOptimizer`.
That family-internal referencing is the design working as intended, not a hidden dependency.

**Evidence.**

| What | Where |
|---|---|
| Every dependency in every packed `.nupkg` is a LogicalOptimizer package — audited by opening the nuspec, on every pull request **and** as a gate before `nuget push` | check `no-third-party-dependencies` in [`tools/verify_package_contract.ps1`](../tools/verify_package_contract.ps1) |
| Package layering is acyclic and points downward | [`ArchitectureTests.PackageLayering_IsAcyclicAndPointsDownward`](../LogicalOptimizer.Tests/Techniques/ArchitectureTests.cs) |
| Library code does not reach into test frameworks or the CLI | [`ArchitectureTests.Library_DoesNotDependOnTestFrameworksOrCli`](../LogicalOptimizer.Tests/Techniques/ArchitectureTests.cs) |
| The consolidated package really carries all seven assemblies, and every forwarding shell points at it | checks `bundled-assemblies-complete` and `forwards-to-consolidated-package` in [`tools/verify_package_contract.ps1`](../tools/verify_package_contract.ps1) |

**Limits.**

- Applies to **shipped** packages. The test and benchmark projects use third-party packages
  (Z3 as a differential oracle, CsCheck, JsonSchema.Net, xUnit, BenchmarkDotNet) and none of them
  ship.
- The audit reads declared nuspec dependencies. It is not a supply-chain scan of the .NET base
  class library, which every managed package uses.

---

## `Native AOT support` / `trimming`

> **Approved wording:** "Native AOT and trimming verified in CI", "Native-AOT-safe". Not
> "AOT-compatible everywhere".

**Meaning.** The library packages compile and **run correctly** as Native AOT binaries, and the
trim/AOT analyzers are treated as errors so a change that would break AOT fails the build rather
than degrading at runtime.

**Evidence.**

| What | Where |
|---|---|
| `LogicalOptimizer.AotSmoke` is published with `PublishAot` for `linux-x64` and `win-x64`, and the resulting **native binary is executed**; it asserts every engine and exits non-zero on any mismatch | [`.github/workflows/aot.yml`](../.github/workflows/aot.yml) |
| `IL2026` / `IL3050` / `IL3053` are fatal through publish (`-warnaserror`), so an AOT-hostile change cannot merge quietly | [`.github/workflows/aot.yml`](../.github/workflows/aot.yml), [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) |
| A Native AOT binary built against the **packaged NuGet bytes** — not an in-repo project reference — produces the expected optimized expression, equivalence proof and `MinimalProven` status. The release gate runs it on the local pre-publish artifacts (the exact bytes then pushed); the same script runs against the published nuget.org package post-release, and its report's `source` field names which it was | `-IncludeAot` in [`tools/smoke_install.ps1`](../tools/smoke_install.ps1), run pre-publish by [`.github/workflows/release.yml`](../.github/workflows/release.yml) |

**Limits.**

- Verified for **`linux-x64` and `win-x64`**. Other runtime identifiers (macOS, arm64) are expected
  to work but are not exercised in CI — treat them as unverified.
- The claim covers the **library** packages. The CLI is distributed as a normal .NET tool, not as a
  Native AOT executable.
- The experimental binary BDD/d-DNNF `Save`/`Load` surface is labelled experimental and is not part
  of any stability claim.

---

## `benchmark result` / comparison numbers

> **Approved wording:** always with the version, corpus and environment attached, or a link to the
> pinned artifact that carries them. Never "faster than X" without them.

**Meaning.** A published number is a measurement of a specific version, on a specific committed
corpus, in a specific environment, against pinned competitor versions.

**Evidence.**

| What | Where |
|---|---|
| Environment and corpus metadata for the comparison run: .NET version, OS, architecture, processor count, CPU policy, corpus path with SHA-256 and function count | [`doc/comparison/manifest.json`](comparison/manifest.json) |
| Methodology: what is measured, what is excluded, how timeouts are treated | [`doc/COMPARISON_METHODOLOGY.md`](COMPARISON_METHODOLOGY.md) |
| Competitor side runs in one version-pinned container (SymPy, PyEDA, CaDiCaL, Kissat, Z3, d4, LogicNG) | [`tools/comparison/Dockerfile`](../tools/comparison/Dockerfile) |
| Raw and merged results, kept as artifacts rather than prose | [`doc/comparison/`](comparison/) |
| Our own timing baseline with runtime/SDK/OS/architecture recorded | [`doc/BENCHMARKS.md`](BENCHMARKS.md), [`doc/perf-baseline.json`](perf-baseline.json) |
| A run is checked, not trusted: same corpus by SHA-256, every row `equivalent`, every miter `unsat`, both counters agreeing, enough competitor columns actually populated (an all-`pending` report fails), and non-timing fields identical to the committed run | [`tools/verify_comparison_reproduction.ps1`](../tools/verify_comparison_reproduction.ps1) |
| The documented reproduction sequence is rehearsed from a clean checkout, so an outside reproducer does not meet a broken script | job `reproduce-from-scratch` in [`.github/workflows/comparison.yml`](../.github/workflows/comparison.yml) |

**Limits.**

- **Wall-clock timings are machine-dependent and indicative only; they are never asserted as a
  gate.** Literal counts are machine-independent, which is why size comparisons carry the weight.
- A timeout is reported as its own status, not as a loss or as a missing row.
- **No independent third-party reproduction exists yet.** All published numbers come from this
  project's own runners. The documented sequence is rehearsed on a clean checkout and mechanically
  verified, which removes the "the scripts were broken" failure mode — but a rehearsal on our own
  runner is not independent reproduction. This is the one piece of evidence the project cannot
  produce for itself: it stays open until someone outside the project runs
  [`doc/COMPARISON_METHODOLOGY.md`](COMPARISON_METHODOLOGY.md) and reports the result.
- Comparisons cover the propositional scope both sides implement. They are not a general
  "better than Z3/LogicNG" statement, and no absolute superlative is used anywhere in public
  material.

---

## `no silent fallbacks`

> **Approved wording:** "no silent fallbacks", "reported explicitly".

**Meaning.** When the library cannot deliver the strongest form of a result, it says so in the
result object instead of quietly returning something weaker: `MinimizationStatus` for minimality,
`ComputationStatus` (`Computed` / `TooLarge` / `NotRequested`) for each normal form, and — with
`--trace` / `IncludeTrace` — the engine chosen, the budgets in force, and every candidate adopted
or rejected with the reason.

**Evidence.**

| What | Where |
|---|---|
| Statuses are surfaced in the machine-readable report and pinned by golden examples, including a `BudgetExceeded` and a `TooLarge` case | [`schema/examples/budget-exceeded.json`](../schema/examples/budget-exceeded.json), [`schema/examples/form-too-large.json`](../schema/examples/form-too-large.json) |
| The trace records engine selection, budgets, candidates, adoption and fallback | [`OptimizationTraceTests`](../LogicalOptimizer.Tests/Facade/OptimizationTraceTests.cs) |
| Every documented status combination is exercised across option pairs | [`PairwiseOptionsTests`](../LogicalOptimizer.Tests/Techniques/PairwiseOptionsTests.cs) |
| The documented per-engine operating envelope (docs-site `budgets-and-zones.md`) cites its hard limits from code: every `constant = value` pair in the table is asserted equal to the enforcing constant, budget default, or default parameter | [`EngineEnvelopeConsistencyTests`](../LogicalOptimizer.Tests/Documentation/EngineEnvelopeConsistencyTests.cs) |

**Limits.**

- Trace wording, ordering and `data` keys are **diagnostic, not contract** — they may change in any
  release. Only the trace's shape and the `category` domain are stable.

---

## Banned phrasing

`ClaimsConsistencyTests` rejects these in public-facing documents:

| Banned | Use instead | Why |
|---|---|---|
| "zero production dependencies", "zero runtime dependencies" | "zero third-party runtime dependencies" | LogicalOptimizer packages reference each other |
| "most complete", "best-in-niche", "fastest", "safest", "most convenient", "unmatched", "industry-leading", "world-class" | a concrete, checkable differentiator plus a link to the pinned comparison | an absolute competitive claim needs external comparative evidence, which does not exist yet |

If a sentence genuinely needs a banned phrase — quoting someone, or stating a limitation such as
"this is not the fastest solver" — append `<!-- claim-ok: reason -->` on that line. The escape is
deliberately visible so it shows up in review.

**Scope of the check.** The public surface: [`README.md`](../README.md), the package READMEs (the consolidated library, the CLI tool, and the forwarding shells),
`<Description>` in every packable `.csproj`, [`SUPPORT.md`](../SUPPORT.md),
[`SECURITY.md`](../SECURITY.md), [`schema/README.md`](../schema/README.md), and the documentation
site (`docs-site/index.md`, `docs-site/articles/`). Historical analysis documents
(`*_COMPARISON*.md`, `*ROADMAP*.md`, `*PLAN*.md`, `CHANGELOG.md`) are out of scope: they record what
was written at a point in time, and rewriting them would falsify the record.
