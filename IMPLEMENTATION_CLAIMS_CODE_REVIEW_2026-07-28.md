# Аудит поточного стану LogicalOptimizer

Дата перевірки: 2026-07-28  
Перевірений стан: commit `1a9fb53` + поточні незакомічені зміни

## Висновок

Поточна production-реалізація загалом відповідає заявленим можливостям.
Перевірені складні компоненти є реальними алгоритмічними реалізаціями, а не
спрощеними заглушками під сильними назвами.

Критичних або високопріоритетних розривів між кодом і описом не знайдено.

Єдина актуальна невідповідність має документаційний характер:
`OptimizationResult.CnfMinimizationStatus` реалізовано й протестовано, але
властивість ще не описана у двох основних статтях про result/status contracts.

## Результат тестування

Виконано:

```powershell
dotnet test LogicalOptimizer.sln -c Release --no-restore --nologo `
  --filter "Category!=Performance&Category!=Exhaustive"
```

Результат:

```text
Passed: 1032
Failed: 0
Skipped: 0
Total: 1032
Duration: 20 s
```

README також вказує 1032 CI cases.

`git diff --check` не виявив whitespace errors. Наявні повідомлення стосуються
лише майбутньої нормалізації LF → CRLF.

## Поточні гарантії реалізації

### Equivalence verification

Rewrite result приймається лише за позитивно доведеної еквівалентності:

```csharp
EquivalenceChecker.CheckWithSat(...).AreEquivalent == true
```

Поведінка:

- `true` — rewrite приймається;
- `false` — rollback до input;
- `null` (`Unknown`) — rollback до input.

Для малих формул використовується truth-table comparison, для більших —
SAT miter.

Окремі tests перевіряють:

- rollback при SAT `Unknown`;
- збереження rewrite після успішного proof.

### Exact two-level minimization

Реалізація містить:

- Quine–McCluskey prime implicant generation;
- essential-prime extraction;
- row dominance;
- column dominance;
- branch-and-bound minimum-cover search;
- lower-bound pruning;
- greedy sound fallback при budget exhaustion;
- явний `ProvenMinimal`.

Cost model:

1. total literal count;
2. term/clause count.

Для guarantee zone до 10 variables:

- prime generation не має pair-comparison budget;
- cover search використовує effectively unbounded limit;
- returned `MinimalProven` означає завершений exact search.

Для 11–12 variables budget exhaustion відображається як
`MinimizationStatus.BudgetExceeded`.

### Окремі SOP і POS statuses

`OptimizationResult.MinimizationStatus` описує SOP/two-level provenance для
optimized/DNF path.

`OptimizationResult.CnfMinimizationStatus` окремо описує equivalent-CNF/POS:

- `MinimalProven` — POS minimum-cover search завершено;
- `BudgetExceeded` — proof search вичерпав budget;
- `Heuristic` — exact POS не створювався, використано Tseitin, CNF не
  requested або artifact не належить до exact zone.

Facade використовує `MinimalPosWithStatus` і не втрачає POS proof result.

### SAT solver

`SatSolver` реалізує:

- two-watched literals;
- implication trail і decision levels;
- 1UIP conflict analysis;
- learnt clauses;
- heap-based variable activity;
- Luby restarts;
- LBD metadata;
- learnt-clause database reduction;
- bounded subsumption preprocessing;
- incremental solving under assumptions;
- unsat cores;
- optional DRAT proof logging.

DRAT additions перевіряються незалежним test-side RUP checker, включно з
random UNSAT instances та equivalence miters.

### SAT-based minimization

Mid-range path:

- шукає uncovered ON assignments через SAT;
- стискає cube через unsat core;
- виконує greedy literal dropping;
- блокує покриті області;
- видаляє redundant cubes;
- повертає heuristic status;
- приймає candidate лише після позитивного SAT-miter proof.

Budget exhaustion не видається за успішну мінімізацію.

### BDD

BDD engine має:

- reduced ordered representation;
- hash-consing;
- complemented edges;
- exact model counting;
- model enumeration;
- existential/universal quantification;
- restriction;
- functional composition;
- variable-order heuristics;
- adjacent-level swaps;
- in-place-style sifting;
- node budget.

Tests перевіряють semantic preservation, canonicity, model counts і size
behavior після reorder.

### d-DNNF

d-DNNF compiler реалізує:

- full biconditional Tseitin CNF;
- unit propagation;
- connected-component decomposition;
- decision branching;
- component caching;
- hash-consing;
- explicit free-variable choices;
- smooth scopes;
- node budget.

Circuit підтримує:

- exact `BigInteger` model counting;
- weighted model counting;
- projected model enumeration.

Counts перевіряються проти BDD і brute-force oracles.

### AIG rewriting

AIG path містить:

- structural hashing;
- complemented edges;
- cut enumeration;
- NPN canonicalization;
- precomputed rewrite library;
- local structural cost comparison;
- independent equivalence verification перед прийняттям candidate.

Candidate приймається лише якщо він:

1. менший за поточний;
2. позитивно доведено еквівалентний.

### Typed budget failures

Expected resource failures відділено від invariant failures:

- `ComputationBudgetExceededException`;
- `NodeBudgetExceededException`;
- `NormalFormTooLargeException`.

Fallback paths ловлять конкретні типи. Звичайний
`InvalidOperationException` не використовується як універсальний спосіб
приховати внутрішню помилку.

### Equivalence self-check API

`OptimizationResult.IsEquivalent()` працює через scalable
`EquivalenceChecker`, а не завжди через truth table.

Для caller, якому потрібна тристанна семантика, є:

```csharp
OptimizationResult.CheckEquivalence()
```

Він дозволяє відрізнити:

- equivalent;
- not equivalent із counterexample;
- `Unknown` через budget exhaustion.

### Timeout і cancellation

Facade створює linked token із:

- caller cancellation token;
- 10-second timeout.

Token передається у важкі cancellable engines, а для синхронних bounded phases
перевіряється на phase boundaries.

README чесно називає це cooperative deadline: фаза переривається, коли
наступного разу перевіряє token. Це не описується як preemptive hard kill.

### Metrics

`OptimizationMetrics` містить:

- original/optimized node counts;
- iteration count;
- applied-rule counts;
- elapsed rewrite time;
- convergence trace;
- calling-thread allocated bytes.

Convergence trace має формат:

```text
iter 0: N nodes
iter 1: M nodes
...
```

Allocation delta фіксується після побудови output artifacts, optional truth
tables і debug dump.

Tests перевіряють:

- `OptimizationSteps.Count == Iterations + 1`;
- наявність node counts;
- `AllocatedBytes > 0`;
- відсутність metrics object, якщо його не запитано.

`AllocatedBytes` є allocation traffic поточного thread, а не peak або retained
memory; README використовує саме це формулювання.

### FormulaFactory contract

Документація коректно розрізняє:

- `FormulaFactory` — єдиний canonical construction path;
- public `AndNode`/`OrNode` constructors — low-level raw AST path без
  canonicalization.

Factory гарантує:

- flattening;
- stable operand sorting;
- deduplication;
- constant/complement folding;
- interning.

## Актуальна невідповідність

### `CnfMinimizationStatus` відсутній у двох основних documentation articles

Пріоритет: **P2, лише документація**.

Властивість реалізована у production API, присутня у public API approval,
changelog, introduction example і tests.

Але вона не згадується у:

- `docs-site/articles/contracts-and-statuses.md`;
- переліку полів `OptimizationResult` у
  `docs-site/articles/optimizer-and-options.md`.

Через це основна contract article описує лише
`OptimizationResult.MinimizationStatus`, хоча поточний API навмисно розділяє
SOP і POS provenance.

Потрібно задокументувати:

- `MinimizationStatus` — SOP/optimized/DNF provenance;
- `CnfMinimizationStatus` — equivalent-CNF/POS provenance;
- для `CnfMode.Tseitin` значення `Heuristic` означає, що two-level POS
  minimality не застосовна.

## Поточна оцінка

| Область | Стан |
|---|---|
| Optimization soundness | Відповідає заявленому |
| SAT `Unknown` handling | Консервативний |
| Exact SOP status | Явний і доказово чесний |
| Exact POS status | Явний і окремий |
| SAT solver | Повноцінна CDCL реалізація |
| DRAT | Є proof logging та незалежна RUP-перевірка |
| BDD | Повноцінний ROBDD engine |
| d-DNNF | Реальний compiler із component caching |
| AIG rewriting | Реальний cut/NPN rewrite path |
| Budget failures | Типізовані |
| Timeout | Cooperative і так задокументований |
| Metrics | Реалізовані й протестовані |
| FormulaFactory contract | Документація відповідає API |
| CI-подібні tests | 1032/1032 |
| Залишкові production issues | Не знайдено |
| Залишкові documentation issues | 1 P2 |

## Фінальний висновок

У поточному стані код не виглядає спрощеною реалізацією під надпотужним
описом. Основні заявлені engines і guarantees підтверджуються production-кодом
та тестами.

Єдина знайдена невідповідність — неповне документування нового
`CnfMinimizationStatus`. Вона не впливає на soundness, optimality provenance
або runtime behavior.

Під час цього аудиту production-код не змінювався. Оновлено лише цей файл.
