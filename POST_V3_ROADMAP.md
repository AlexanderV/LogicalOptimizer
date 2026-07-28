# План розвитку LogicalOptimizer після v3.0.0

Базовий реліз: `v3.0.0`.

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

## 2. Зведений пріоритет

| ID | Зміна | Вплив | Обсяг | Ризик | Реліз |
|---|---|---:|---:|---:|---|
| P0.2 | Контрольований cross-library benchmark | висока довіра | M | низький | v3.1 |
| P0.1 | Native AOT і trimming certification | високий | S–M | низький | v3.1 |
| P0.3 | Internal performance hardening + regression gate | середній–високий | S–M | низький | v3.1 |
| P1.1 | DIMACS/WCNF/OPB import (пакет `.Formats`) | високий | M | середній | v3.1 |
| P3.1 | `LogicalOptimizer.Full` meta-package | середній UX | S | низький | v3.1 |
| P1.4-spike | Projected counting — design spike з прототипами | — | S | низький | v3.1 |
| P1.2 | d-DNNF conditioning і batch queries | високий | M | середній | v3.2 |
| P1.3 | d-DNNF marginals і sampling | високий | M | середній | v3.2 |
| P1.4 | Projected model counting | дуже високий | L–XL | високий | v3.3 |
| P2.3 | Серіалізація BDD/d-DNNF (experimental) | середній | M–L | середній | v3.3 |
| P2.1 | Portfolio cardinality/PB encodings | середній–високий | L | середній | v3.4 |
| P2.2 | Core-guided MaxSAT | середній–високий | L–XL | високий | v3.5 |

Порядок у v3.1 навмисний: benchmark (P0.2) — **перший**, бо всі claim'и про вплив
спираються на його артефакти; далі AOT (P0.1), який має найкраще співвідношення
цінність/ризик; далі внутрішнє перф-загартування з regression-гейтом (P0.3);
потім interoperability (P1.1) і UX (P3.1). Дешевий spike найризикованішої
можливості (P1.4) стартує вже у v3.1, щоб до v3.3 алгоритм був доведений.

## 3. P0.2 — Контрольований cross-library benchmark

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
5. додати CI publish щонайменше для `win-x64` і `linux-x64`;
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

Орієнтовно (остаточні назви типів — окремий API review):

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

### Мета

Обчислювати кількість різних assignments лише за вибраною множиною змінних:

```csharp
public ProjectedModelCountResult CountProjectedModels(
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

### Підхід

- **v3.1 spike:** прототипи двох-трьох стратегій, вибір алгоритму та status
  contract до фіксації public API;
- **MVP (v3.3):** SAT blocking enumeration як sound budgeted шлях із чесним
  `Status` — віддається першим;
- **exact d-DNNF projection** (projected compilation з cache за projected scope
  або existential abstraction з повторною deterministic compilation) — після
  spike;
- hybrid BDD path для сприятливих projected variable sets — опційно.

### Статус-контракт

Часткове число ніколи не видається за точний результат:

```csharp
public sealed class ProjectedModelCountResult
{
    public BigInteger? Count { get; }
    public ComputationStatus Status { get; }
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
2. PR-gate tests — 0 failed, 0 skipped.
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

1. P0.2 cross-library benchmark (першим — база для claim'ів);
2. P0.1 Native AOT/trimming certification;
3. P0.3 internal performance hardening + regression gate;
4. P1.1 DIMACS/WCNF/OPB import (`.Formats` пакет);
5. P3.1 `LogicalOptimizer.Full`;
6. P1.4 spike (прототипи projected counting).

Найбільший короткостроковий приріст із низьким ризиком для алгоритмічного ядра.

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
   spike; часткове число ніколи не exact.
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
