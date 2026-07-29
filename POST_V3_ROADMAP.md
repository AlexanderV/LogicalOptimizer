# План розвитку LogicalOptimizer після v3.0.0

Базовий реліз: `v3.0.0`.

Поточний перевірений стан плану: branch `perf/minimizer-and-nuget-verify` (29.07.2026).

Статуси: ✅ виконано · ◐ частково виконано · 🧪 design spike завершено, production
API ще не реалізовано · ☐ не розпочато.

## 0. Мета

Посилити позицію LogicalOptimizer як інтегрованого propositional toolkit, не
порушуючи основного таргетування:

- бібліотеки: `net8.0;net10.0`;
- CLI: `net10.0`;
- pure managed implementation;
- нуль зовнішніх production/runtime dependencies;
- модульні NuGet-пакети;
- additive public API без breaking changes;
- явні budgets, cancellation і proof statuses;
- позитивний доказ еквівалентності перед прийняттям оптимізації.

Ціль — не перетворювати LogicalOptimizer на SMT-солвер чи повну EDA-систему, а
посилити його унікальність у .NET: interoperability та практичну цінність SAT,
BDD і d-DNNF engines.

## 1. Очікуваний вплив

Розвиток вимірюється **конкретними capability-твердженнями**, а не єдиним
композитним балом. Композитна експертна оцінка (`VALIDATED_LIBRARY_COMPARISON`)
використовується лише як directional-орієнтир: LogicalOptimizer — другий після
LogicNG серед integrated propositional toolkits, і головний розрив — не
функціональність, а зрілість ecosystem.

Три зміни дають найбільший приріст саме в capability-площині:

1. Native AOT/trimming як формальний контракт → self-contained managed toolkit,
   якого немає в жодного прямого конкурента;
2. стандартні DIMACS/WCNF/OPB inputs → пряма сумісність з існуючими datasets без
   конвертації;
3. розширені d-DNNF queries, зокрема projected model counting → унікальна для
   pure .NET можливість exact #SAT з проєкцією.

Кожна з них має бути підкріплена відтворюваним доказом (розд. 4), а не заявою.

## 2. Зведений статус і пріоритет

| Статус | ID | Зміна | Фактичний результат / залишок | Реліз |
|:---:|---|---|---|---|
| ◐ | P0.2 | Контрольований cross-library benchmark | OUR-side JSON/manifest/summary і runners готові; competitor columns ще `pending` | v3.1 |
| ✅ | P0.1 | Native AOT і trimming certification | 6 бібліотек + `.Formats` позначені AOT/trim-safe; smoke app і workflow готові | v3.1 |
| ✅ | P0.3 | Internal performance hardening + regression gate | мінімізатори оптимізовано; allocation baseline і blocking CI gate додано | v3.1 |
| ✅ | P1.1 | DIMACS/WCNF/OPB import | новий пакет `.Formats`, CLI, writers, budgets, fuzz/round-trip tests | v3.1 |
| ✅ | P3.1 | `LogicalOptimizer.Full` meta-package | meta-package, integration tests і post-publish verification готові | v3.1 |
| 🧪 | P1.4-spike | Projected counting design spike | SAT і BDD prototypes, 1,048,576 exhaustive checks, рекомендація зафіксована | v3.1 |
| ☐ | P1.2 | d-DNNF conditioning і batch queries | не розпочато | v3.2 |
| ☐ | P1.3 | d-DNNF marginals і sampling | не розпочато | v3.2 |
| ☐ | P1.4 | Production projected model counting | spike завершено; лишилися API-рішення та production implementation | v3.3 |
| ☐ | P2.3 | Серіалізація BDD/d-DNNF (experimental) | не розпочато | v3.3 |
| ☐ | P2.1 | Portfolio cardinality/PB encodings | не розпочато | v3.4 |
| ☐ | P2.2 | Core-guided MaxSAT | не розпочато | v3.5 |

До завершення v3.1 залишилося:

1. виконати competitor runners в одному контрольованому Linux environment;
2. злити результати в спільний comparison artifact;
3. виконати повний release gate і оновити comparison/README claims за реальними
   артефактами.

Закрито після зведеного стану вище: категоризацію exhaustive spike-тесту уніфіковано
до `Category=Exhaustive` (гейт більше не тягне ~1-хвилинний доказ); кількість пакетів
у README/docs-site узгоджено (9 опублікованих); додано CHANGELOG-запис `[Unreleased]`
для v3.1; **open API-питання projected-counting зафіксовано** в
[`doc/decisions/projected-model-counting-api.md`](doc/decisions/projected-model-counting-api.md)
(production implementation лишається роботою v3.3).

Нові capability-напрями до закриття цього залишку додавати не потрібно.

### Перевірка поточного branch

Локально на branch `perf/minimizer-and-nuget-verify`:

- Release build усієї solution: **0 warnings, 0 errors**;
- повний тест-сюїт (без фільтра): **1113/1113 passed**, 0 failed, 0 skipped;
- gate-фільтр `Category!=Performance&Category!=Exhaustive` — зелений; вичерпний ≤4-змінний
  spike-доказ
  (`ProjectedModelCountingTests.ExhaustiveAgreement_AllFourVariableFunctions`) тепер
  виключено з гейта (trait уніфіковано до `Category=Exhaustive`): під гейт-фільтром
  spike-набір виконується як **10 тестів за ~0.6 с** замість 11 за ~1 хв 28 с, а сам
  вичерпний доказ зберігається у повному/nightly-прогоні.

## 3. P0.2 — Контрольований cross-library benchmark

**Статус: ◐ частково виконано (`54dcccb`).**

Готові:

- `CrossLibraryComparisonHarness`;
- `doc/comparison/our-results.json`, `manifest.json`, `summary.md`;
- окрема methodology;
- runners і merge tooling;
- CI workflow.

Залишок: запустити зовнішні інструменти та замінити `pending` у SymPy/PyEDA,
CaDiCaL/Kissat, LogicNG/c2d/d4 columns результатами з того самого environment.

### Мета

Перетворити якісне порівняння на відтворюваний доказ, не заявляючи
publication-grade performance там, де його немає. Це передумова для будь-якого
capability-claim у розд. 1.

### Учасники

- LogicalOptimizer;
- LogicNG;
- Z3;
- SymPy;
- PyEDA;
- CaDiCaL або Kissat для SAT-only таблиці.

### Методика

Один Linux runner/container:

- одна CPU allocation policy;
- warm-up;
- однаковий timeout;
- один committed corpus;
- фіксовані random seeds;
- окремі таблиці для різних задач.

Не можна змішувати:

- default multi-level output і two-level SOP;
- equivalent CNF і equisatisfiable CNF;
- JIT cold-start і warmed steady state;
- measurements із різних машин.

### Набори результатів

1. **Symbolic optimization** — input/output literal count, AST node count,
   equivalence verdict, time, allocations.
2. **Two-level minimization** — terms, literals, proof status, timeout.
3. **SAT** — verdict, conflicts, time, proof availability.
4. **BDD/d-DNNF** — compile time, node count, exact model count,
   repeated-query time.

### Artifacts

- JSON із сирими результатами;
- Markdown summary;
- environment manifest;
- historical trend file;
- CI regression comparison без крихких hard wall-clock assertions.

### Критерії приймання

- будь-яке число в comparison-документі походить із committed artifact;
- кожна таблиця має точну команду відтворення;
- competitor timeout позначається як timeout, а не як failure;
- correctness перевіряється незалежно від performance.

## 4. P0.1 — Native AOT і trimming certification

**Статус: ✅ виконано (`8a78023`).**

Production-критерій виконано через AOT/trim metadata, `LogicalOptimizer.AotSmoke`
та `.github/workflows/aot.yml`. Цей розділ зберігається як контракт і checklist
для наступних пакетів: кожен новий publishable package повинен пройти той самий
analyzer/smoke gate.

### Мета

Зробити AOT-сумісність формальною частиною контракту, а не припущенням.

Переваги:

- self-contained deployment без установленого .NET runtime;
- швидший startup CLI та короткоживучих workers;
- менший memory footprint;
- придатність для containers, serverless і restricted JIT environments;
- сильний differentiator серед Boolean toolkits.

### Вихідна позиція

Код бібліотек і CLI — reflection-free: немає `System.Reflection`,
`Activator.CreateInstance`, `BinaryFormatter`, `JsonSerializer`,
`Expression.Compile` чи reflection-based CLI-фреймворку (парсинг аргументів —
ручний). Тому основне джерело trim/AOT-warnings відсутнє, і ризик низький.
Залишкова тертя очікується лише навколо generic-важкого LINQ у мінімізаторах і
globalization/ICU.

### Зміни

Для бібліотечних `.csproj`:

```xml
<IsAotCompatible>true</IsAotCompatible>
<IsTrimmable>true</IsTrimmable>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
<EnableAotAnalyzer>true</EnableAotAnalyzer>
```

Необхідно:

1. виправити всі actionable `IL2026`, `IL3050`, `IL3053` та пов'язані warnings;
2. не приховувати warnings без конкретного justification;
3. створити окремий Native AOT smoke application;
4. виконувати його для parser, optimizer, SAT, BDD, minimization і d-DNNF;
5. підтримувати CI publish щонайменше для `win-x64` і `linux-x64`;
6. перевіряти semantic parity між JIT та AOT outputs.

Native AOT CLI artifacts (`win-x64`, `linux-x64`, `linux-arm64`) — це
**CI-certified capability, а не частина кожного релізу**: публікувати їх лише на
явний trigger, щоб не роздувати surface релізу заради меншості користувачів.
Звичайні framework-dependent NuGet/CLI packages лишаються основним каналом
доставки.

### Критерії приймання

- AOT publish без analyzer warnings;
- усі smoke scenarios завершуються з очікуваними результатами;
- оптимізовані формули JIT і AOT byte-identical;
- SAT verdicts, model counts і proof statuses збігаються;
- README містить відтворювані publish-команди;
- звичайні framework-dependent NuGet/CLI packages лишаються доступними.

### Ризики

- trimming може виявити приховане використання reflection у transitive BCL-коді;
- Native AOT CLI binary — platform-specific і більший за framework-dependent
  executable;
- AOT artifacts не замінюють звичайний dotnet tool.

## 5. P0.3 — Internal performance hardening + regression gate

**Статус: ✅ виконано (`5a17671`, `a3ee347`).**

Hot-path оптимізації мінімізаторів уже внесені без public API changes. Committed
allocation baseline, comparison script і blocking workflow є постійним
regression gate. Подальше performance hardening — звичайна підтримка, а не
незакритий milestone v3.1.

### Мета

Тримати алгоритмічне ядро швидким і **захистити його від регресій** між
релізами. План загалом трактує SAT/BDD/DNNF/interop як зону росту, але сам
символьний оптимізатор і мінімізатори мають запас перформансу, який реалізується
без зміни public API чи поведінки (тому пункт не суперечить non-goal про
відсутність breaking rewrite).

### Scope

- зменшення hot-path алокацій у мінімізаторах:
  - Espresso-lite tautology check: спільна bitmask виключених змінних замість
    per-recursion cube cloning;
  - exact Quine–McCluskey: popcount-бакети суміжних імплікант, batch-екстракція
    essential primes, bitmask-подання covering-table dominance замість
    `HashSet<int>`;
- committed BenchmarkDotNet baseline-артефакт для ключових engines;
- CI regression comparison поверх наявного benchmark-suite, без крихких
  wall-clock assertions (та сама політика, що в P0.2);
- будь-яка зміна hot-path супроводжується оновленим benchmark-артефактом.

Всі оптимізації зберігають byte-identical output (той самий prime set і той самий
`(literals, terms)` cost order) і залишаються під тими самими budgets/cancellation
контрактами.

### Критерії приймання

- повний тест-сюїт зелений без змін очікуваних результатів (доказ незмінності
  output);
- committed baseline-артефакт існує і відтворюється однією командою;
- regression-порівняння в CI сигналить деградацію понад узгоджений threshold, але
  не червонить збірку через шум машини;
- жоден timing-залежний тест не покладається на повільність коду як на inваріант.

## 6. P1.1 — DIMACS, WCNF та OPB import

**Статус: ✅ виконано (`0aff0e7`).**

Фактичний API реалізований у `LogicalOptimizer.Formats`; пакет містить problem
types, `Parse(TextReader, ResourceBudget?, CancellationToken)`, writers і
перетворення/solve helpers. CLI wiring, architecture/API baselines,
round-trip/fuzz tests і release integration також готові.

### Мета

Зробити бібліотеку безпосередньо сумісною зі стандартними SAT, MaxSAT і
pseudo-Boolean datasets.

### Розташування

Parser-типи виносяться в **окремий пакет `LogicalOptimizer.Formats`**, а не в
`.Sat`. DIMACS/WCNF/OPB — це I/O-концерн; змішування з solver-ядром порушує
layering-дисципліну, яку enforced архітектурний тест. Пакет залежить від `.Sat`
для результуючих типів; CLI wiring — у `LogicalOptimizer.Cli`; facade
convenience APIs — лише якщо не дублюють package APIs.

### Новий API

Фактично реалізовано:

```csharp
public static class DimacsParser
{
    public static CnfProblem Parse(
        TextReader reader,
        ResourceBudget? budget = null,
        CancellationToken cancellationToken = default);
}

public static class WcnfParser
{
    public static WeightedCnfProblem Parse(
        TextReader reader,
        ResourceBudget? budget = null,
        CancellationToken cancellationToken = default);
}

public static class OpbParser
{
    public static PseudoBooleanProblem Parse(
        TextReader reader,
        ResourceBudget? budget = null,
        CancellationToken cancellationToken = default);
}
```

### Вимоги

- streaming через `TextReader`, без обов'язкового завантаження всього файлу;
- підтримка comments і стандартних headers;
- line/column у parse errors;
- захист від oversized variable IDs, clauses, literals і weights;
- cancellation checks;
- round-trip writer tests;
- deterministic normalization;
- CLI:

```text
logical-optimizer solve input.cnf
logical-optimizer maxsat input.wcnf
logical-optimizer solve-pb input.opb
logical-optimizer count input.cnf --engine dnnf
```

### Критерії приймання

- official/public corpus samples читаються без preprocessing scripts;
- parse → write → parse зберігає семантику;
- SAT/MaxSAT optimum звіряється з незалежним oracle на малих instances;
- malformed-input fuzzing не призводить до hangs або uncontrolled allocation.

## 7. P1.2 — d-DNNF conditioning і batch queries

### Мета

Перетворити d-DNNF з одноразового model counter на reusable query engine поверх
наявних `CountModels`, `WeightedModelCount` і `EnumerateModels`.

### API

Стартова форма — **immutable circuit methods**; `DnnfQuerySession` вводиться лише
коли профіль покаже реальну вигоду від кешу повторних passes (YAGNI до того).

```csharp
public DnnfCircuit Condition(
    IReadOnlyDictionary<string, bool> assignment,
    CancellationToken cancellationToken = default);

public BigInteger CountModels(
    IReadOnlyDictionary<string, bool> evidence);

public double WeightedModelCount(
    IReadOnlyDictionary<string, (double positive, double negative)> weights,
    IReadOnlyDictionary<string, bool> evidence);
```

### Вимоги

- unknown variable — явна argument error;
- conditioning не змінює original circuit;
- node count conditioned circuit не перевищує budget;
- exact counts збігаються з brute force і BDD Restrict;
- floating-point поведінка weighted-шляху документується згідно з розд. 14.

### Критерії приймання

- randomized differential tests проти BDD;
- `CountModels(evidence)` дорівнює кількості моделей із відповідним filter;
- repeated queries не компілюють formula повторно;
- cancellation працює на великих circuits.

## 8. P1.3 — Marginals і model sampling

### Мета

Додати практичні probabilistic/configuration queries поверх наявного weighted
model counting.

### API

```csharp
public double MarginalProbability(
    string variable,
    IReadOnlyDictionary<string, (double positive, double negative)> weights);

public IReadOnlyDictionary<string, bool> SampleModel(
    Random random,
    IReadOnlyDictionary<string, (double positive, double negative)>? weights = null);

public IEnumerable<IReadOnlyDictionary<string, bool>> SampleModels(
    int count,
    int seed,
    IReadOnlyDictionary<string, (double positive, double negative)>? weights = null,
    CancellationToken cancellationToken = default);
```

### Вимоги

- seeded mode повністю deterministic;
- zero-total-weight повертає явну помилку/status;
- probabilities перевіряються через exhaustive weighted enumeration;
- sampling distribution тестується статистично з широкими, стабільними bounds;
- жодних cryptographic claims;
- floating-point limitations документуються згідно з розд. 14.

### Критерії приймання

- marginal дорівнює `WMC(v=true) / WMC(total)`;
- unweighted sampling приблизно uniform на малих circuits;
- impossible evidence не повертає fabricated model.

## 9. P1.4 — Projected model counting

**Статус: ✅ production MVP реалізовано — `FormulaAnalysis.CountProjectedModels(...)`
(SAT blocking enumeration). Design spike завершено раніше (`9ce9ede`).**

Повний звіт: [`doc/spikes/projected-model-counting.md`](doc/spikes/projected-model-counting.md).
SAT blocking і BDD existential-abstraction prototypes збіглися з незалежним
oracle на всіх 1,048,576 exhaustive 4-variable перевірках, randomized trials та
edge cases.

### Мета

Обчислювати кількість різних assignments лише за вибраною множиною змінних.
**Фінальне розміщення (реалізовано):** статичний facade-метод на `FormulaAnalysis`
(поряд із `ComputeBackbone`/`EnumerateModels`), а не extension на `DnnfCircuit` зі
sketch у decision doc — MVP-engine є SAT blocking enumeration над CNF і не використовує
скомпільований d-DNNF, тож тримаємо SAT-рушій поза `Dnnf`-пакетом:

```csharp
public static ProjectedModelCountResult CountProjectedModels(
    AstNode formula,
    IReadOnlyCollection<string> projectedVariables,
    ResourceBudget? budget = null,
    CancellationToken cancellationToken = default);
```

Потенційний differentiator проти LogicNG і водночас алгоритмічно найризикованіша
можливість плану.

### Критична пастка

Не можна:

1. просто видалити literals непроєктованих змінних;
2. скласти counts OR-гілок;
3. вважати, що determinism зберігається після forgetting.

Після projection різні повні моделі можуть відповідати одному projected model;
наївне підсумовування дає overcount.

### Підхід після spike

- **MVP (v3.3):** SAT blocking enumeration як sound budgeted шлях із чесним
  `Status`;
- **exact d-DNNF projection** (projected compilation з cache за projected scope
  або existential abstraction з повторною deterministic compilation) — після
  spike;
- hybrid BDD existential-abstraction path підтверджено прототипом і лишається
  opt-in exact fallback для сприятливих projected variable sets.

### Open API-рішення — зафіксовано

Усі п'ять питань закриті в
[`doc/decisions/projected-model-counting-api.md`](doc/decisions/projected-model-counting-api.md):

1. **Scope** — `P` може містити змінні поза formula (кожна вільна множить count на 2);
   empty ⇒ `0/1`; unknown-name не є помилкою;
2. **Result shape** — `ProjectedModelCountResult { BigInteger? Count; ProjectedCountStatus
   Status }`, `Count` non-null iff `Exact`; **окремий** enum (не наявний `ComputationStatus`,
   значення якого — `Computed/TooLarge/NotRequested`);
3. **Budget** — спільний `ResourceBudget`; кожен engine мапить у власну валюту, але outcome
   завжди `BudgetExhausted`;
4. **Engine** — v3.3 лише blocking-enumeration MVP; `Auto`/explicit — коли з'явиться exact
   path (політика `Auto` як у §17 рішення 6);
5. **Enumeration** — відкладено як окремий метод, не в counting-контракті.

### Статус-контракт

Часткове число ніколи не видається за точний результат:

```csharp
public sealed class ProjectedModelCountResult
{
    public BigInteger? Count { get; }            // non-null iff Status == Exact
    public ProjectedCountStatus Status { get; }  // Exact | BudgetExhausted (окремий enum)
}
```

### Критерії приймання

- exhaustive verification для всіх functions до 4 змінних;
- randomized comparison із explicit projection/dedup enumeration;
- tests із many-to-one projection;
- empty projection повертає 0 для UNSAT і 1 для SAT;
- projection усіх variables дорівнює `CountModels()`;
- budget exhaustion ніколи не видається за точний count.

## 10. P2.3 — Серіалізація BDD і d-DNNF

> **Статус: ✅ реалізовано (experimental до v4).** `BinaryDecisionDiagram` і `DnnfCircuit`
> мають `Save(Stream)` / `static Load(Stream, ResourceBudget?, CancellationToken)` над
> compact binary-форматом (magic · version · engine byte · variable table · node table · root ·
> CRC-32). Deterministic output, explicit little-endian, forward-version rejection, budgeted +
> semantically-validated load, engine-byte cross-load як typed error, жодної unsafe object
> deserialization. Malformed input → `CircuitSerializationException`. Golden-blob drift-тести
> регенеруються через `LOGICALOPTIMIZER_REGENERATE_GOLDEN=1`.

### Мета

Дозволити сервісам компілювати circuit один раз і повторно використовувати його
після restart/deployment.

Формат оголошується **experimental до v4** — stable-контракт фіксується лише коли
з'явиться перший реальний споживач; до того forward-compatibility не гарантується.

### Формат

Compact binary format:

- magic;
- format version;
- engine type;
- variable table;
- node table;
- root handle;
- checksum;
- optional metadata.

JSON — лише diagnostic/debug format.

### API

```csharp
public void Save(Stream destination);

public static DnnfCircuit Load(
    Stream source,
    ResourceBudget? budget = null,
    CancellationToken cancellationToken = default);
```

Аналогічно для BDD.

### Вимоги

- deterministic byte output;
- explicit endianness;
- forward-version rejection із чіткою помилкою;
- size/node/depth budgets під час load;
- жодної unsafe object deserialization;
- semantic validation;
- hash/checksum не замінює semantic checks.

### Критерії приймання

- save/load зберігає model count, variables і evaluation;
- corrupted-input fuzzing;
- golden format samples;
- experimental-маркування в API/README до першого stable format version.

## 11. P2.1 — Portfolio cardinality і PB encodings

### Мета

Покращити SAT/MaxSAT performance без додавання другого solver backend.

### Передумова

Calibration corpus фіксується **окремим PR до** впровадження евристик — інакше
evaluation підганяється під результат.

### Cardinality encodings

- pairwise для малого AtMostOne;
- sequential counter;
- product encoding;
- totalizer;
- generalized totalizer для weighted constraints.

### PB encodings

- наявний encoding зберегти як стабільний default;
- binary merge;
- watchdog або generalized totalizer;
- automatic selection за кількістю literals, bound, щільністю, magnitude weights.

### API

```csharp
public enum CardinalityEncoding
{
    Auto,
    Pairwise,
    SequentialCounter,
    Product,
    Totalizer
}

public enum PseudoBooleanEncoding
{
    Auto,
    DynamicProgramming,
    BinaryMerge,
    GeneralizedTotalizer
}
```

Нові optional parameters мають defaults, що зберігають поточну поведінку. `Auto`
може змінювати вибір між minor-релізами — але лише з документованим записом у
CHANGELOG і за умови «не гірше threshold на зафіксованому corpus».

### Критерії приймання

- exhaustive semantic verification;
- clause/auxiliary-variable counts;
- benchmark matrix за shape constraints;
- `Auto` ніколи не гірший за поточний default більше ніж на узгоджений threshold
  на calibration corpus;
- encoding statistics доступні в diagnostics.

## 12. P2.2 — Core-guided MaxSAT

### Мета

Доповнити linear weighted partial MaxSAT алгоритмом, що використовує наявні
assumptions, UNSAT cores і cardinality constraints.

### Scope

- спочатку unweighted partial MSU3/OLL-lite;
- потім weighted extension;
- incumbent best model;
- lower/upper bounds;
- three-valued completion status;
- cancellation і conflict budget.

### API

Поточний `MaxSatSolver` не ламати. Додати configuration через options object або
окрему factory method:

```csharp
public enum MaxSatAlgorithm
{
    Auto,
    Linear,
    CoreGuided
}
```

### Критерії приймання

- optimum проти brute force для малих задач;
- differential comparison із Z3 Optimize;
- hard-UNSAT відрізняється від budget exhaustion;
- incumbent не видається за proven optimum;
- regression corpus включає cases, де linear і core-guided мають різний профіль.

## 13. P3.1 — `LogicalOptimizer.Full` meta-package

**Статус: ✅ виконано (`cd1b186`, розширено `.Formats` у `0aff0e7`).**

Meta-package створено, integration tests перевіряють dependency graph і
capabilities, release verification оновлено. Надалі пункт є packaging contract:
кожен новий user-facing engine/package має бути свідомо включений або явно
виключений із `.Full`.

### Мета

Дати one-install experience без зміни залежностей існуючого facade. «Один
toolkit» — маркетингова теза, яку installation-snippet не закриває, тому
восьмий пакет виправданий.

```text
LogicalOptimizer.Full
 ├─ LogicalOptimizer
 └─ LogicalOptimizer.Dnnf
```

Пакет не містить runtime code — лише агрегує managed packages.

### Переваги

- `dotnet add package LogicalOptimizer.Full`;
- основний facade не отримує нової обов'язкової залежності;
- modular users продовжують брати окремі layers.

### Критерії приймання

- package dependency graph перевіряється integration test;
- smoke project використовує optimizer, SAT, BDD і d-DNNF;
- README чітко розрізняє facade, full і individual packages;
- post-publish verification охоплює восьмий пакет у межах розширеного вікна
  індексації (див. розд. 15, gate 8).

## 14. Контракти бібліотеки

Ці контракти фіксуються явно, бо саме вони визначають придатність до
serverless/worker-сценаріїв, які мотивують P0.1.

### Потокобезпека

- compiled/immutable структури (`DnnfCircuit`, побудований BDD, оптимізований
  AST) — safe для concurrent read-only queries;
- mutable engines (`SatSolver`, BDD manager під час побудови) — не thread-safe;
  один екземпляр на потік;
- query-сесії (якщо вводяться) — або immutable, або явно позначені як
  non-thread-safe.

### Floating-point

- weighted model counting, marginals і sampling працюють у `double`;
- документується, де точність гарантується, а де можлива накопичена похибка;
- жодних cryptographic claims для sampling;
- zero-total-weight — явний error/status, не fabricated результат.

### Budgets, cancellation, status

- кожен expensive engine приймає `ResourceBudget` і `CancellationToken`;
- часткові результати ніколи не видаються за exact (typed status або exception);
- жодних прихованих heuristic fallbacks без status.

## 15. Обов'язкові gates для кожного релізу

1. `dotnet build -c Release` — 0 warnings.
2. PR-gate tests — 0 failed, 0 skipped; усі `Performance`, `Exhaustive` і spike
   exhaustive cases мають послідовну категоризацію та не потрапляють у fast gate.
3. API baseline diff reviewed explicitly.
4. Architecture layering test зелений.
5. `dotnet list package --vulnerable --include-transitive` — чисто.
6. Documentation examples executed.
7. `git diff --check` — чисто.
8. NuGet post-publish verification для всіх пакетів у межах реалістичного вікна
   індексації (нагадування: вузьке вікно червонить успішний реліз).
9. Performance regression comparison поверх committed baseline — деградація понад
   threshold блокує, шум машини — ні.
10. Для algorithm changes:
    - brute-force oracle;
    - differential oracle;
    - property/metamorphic tests;
    - resource budget і cancellation tests;
    - benchmark artifact.
11. Для serialization/parser changes:
    - malformed-input fuzzing;
    - allocation/size limits;
    - deterministic round trip.

## 16. Релізна послідовність

### v3.1 — Credibility, deployment, interoperability

Виконано: P0.1, P0.3, P1.1, P3.1 і P1.4 spike. P0.2 виконано з OUR-side,
methodology та runners.

Залишок перед релізом:

1. competitor-side benchmark runs і merged artifact;
2. рішення щодо projected-counting public contract;
3. повний build/test/AOT/perf/API/security/release gate;
4. синхронізація README, changelog, docs і comparison із фінальним складом v3.1.

Production projected counting не є блокером v3.1.

### v3.2 — Reusable knowledge compilation

1. P1.2 conditioning і batch queries;
2. P1.3 marginals і sampling.

### v3.3 — Unique model-counting capability

1. P1.4 projected model counting (MVP через blocking enumeration, далі exact);
2. P2.3 d-DNNF serialization (experimental).

### v3.4 — Encoding portfolio

1. calibration corpus (передумова);
2. P2.1 cardinality/PB encodings;
3. BDD serialization.

### v3.5 — MaxSAT depth

1. P2.2 core-guided MaxSAT;
2. WCNF benchmark corpus;
3. algorithm auto-selection.

## 17. Прийняті архітектурні рішення

Ці рішення зафіксовано; відповідні public APIs проєктуються відповідно до них.

1. **Native AOT artifacts** — CI-certified capability на явний trigger, не частина
   кожного релізу.
2. **Parser-типи** — окремий пакет `LogicalOptimizer.Formats`, не в `.Sat`.
3. **d-DNNF query API** — стартує як immutable circuit methods; `DnnfQuerySession`
   лише за доведеної вигоди кешу.
4. **Projected counting** — MVP на SAT blocking enumeration з
   `ProjectedModelCountResult{Count?, Status}`; exact d-DNNF projection після
   spike; часткове число ніколи не exact. Spike підтвердив soundness SAT і BDD
   strategies; open API-рішення перелічені в §9.
5. **Serialization format** — experimental до v4, stable лише з першим реальним
   споживачем.
6. **`Auto` encoding** — може змінювати вибір між minor-релізами з CHANGELOG-записом
   і threshold-гарантією на зафіксованому corpus.
7. **`LogicalOptimizer.Full`** — восьмий пакет, публікується після стабілізації
   вікна post-publish verification.

## 18. Свідомі non-goals

У цьому циклі не плануються:

- нижчі TFM (`netstandard`, старий .NET Framework);
- SMT theories;
- native Z3, Espresso, CUDD або ABC runtime bindings;
- другий SAT solver backend;
- ADD/ZDD;
- sequential synthesis, retiming або technology mapping;
- hard real-time preemption;
- приховані heuristic fallbacks без status;
- breaking rewrite публічного AST/API;
- service hosting framework усередині бібліотеки.

Ці напрями або змінюють таргетування, або розмивають головну перевагу:
self-contained managed .NET toolkit.
