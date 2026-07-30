# План посилення позиції LogicalOptimizer без розмивання таргетування

## 1. Поточний висновок

LogicalOptimizer уже має не лише сильну технічну основу, а й значну частину
необхідної публічної упаковки:

- власні SAT, BDD і d-DNNF engines;
- обов'язкову перевірку еквівалентності результатів оптимізації;
- явні статуси мінімальності, budget exhaustion і fallback;
- модульні NuGet-пакети без сторонніх production dependencies;
- підтримку Native AOT і trimming;
- імпорт, експорт і CLI;
- відтворювані тести, benchmarks і comparison artifacts;
- scenario-driven документацію та runnable samples;
- machine-readable CLI і production diagnostics.

Основний резерв для подальшого посилення позиції тепер лежить не в первинному
перепакуванні README і не в додаванні ще одного solver, а в чотирьох напрямках:

1. перевірити, що реалізована подача коректно доходить до опублікованих артефактів;
2. отримати зовнішні докази adoption і практичної цінності;
3. зробити метрики вимірюваними, а не декларативними;
4. розвивати нові API лише після підтвердження попиту.

## 2. Цільове позиціонування

### Короткий опис

> **Verified Boolean reasoning toolkit for .NET**  
> Optimize, compare, count, and solve Boolean formulas with zero third-party
> runtime dependencies.

### Розширений опис

> LogicalOptimizer is a dependency-free .NET toolkit for verified Boolean
> optimization, equivalence checking, SAT solving, model counting, and knowledge
> compilation. Every optimization result is checked for equivalence, while
> minimality and resource-limit outcomes are reported explicitly.

Термін `dependency-free` у коротких маркетингових матеріалах означає відсутність
сторонніх runtime dependencies. Модульні пакети LogicalOptimizer можуть залежати
один від одного.

### Ключова обіцянка продукту

LogicalOptimizer надає перевірюваний, pure-managed спосіб виконувати Boolean
reasoning усередині .NET applications без Z3, native runtime або прихованих
fallback-механізмів.

Це сильна, але перевірювана обіцянка. Не слід використовувати абсолютні твердження
на кшталт «найбезпечніший», «найзручніший» або «найшвидший» без зовнішнього
порівняльного доказу.

### Основні сценарії

- оптимізація feature та configuration expressions;
- перевірка еквівалентності двох версій умови;
- отримання counterexample для нееквівалентних формул;
- перевірка satisfiability та constraints;
- точний і weighted model counting;
- мінімізація згенерованих C#, Verilog або Boolean expressions;
- embedded і Native AOT deployment без native dependencies.

Ці сценарії пояснюють практичну цінність наявного propositional toolkit і не
перетворюють його на загальний rules engine, SMT stack або EDA platform.

## 3. Completed — реалізована публічна подача

### 3.1 README і hero

Головний README уже містить:

- коротку value proposition;
- три основні differentiators;
- команди встановлення;
- короткий C# приклад;
- proof-style report;
- таблицю вибору між LogicalOptimizer та альтернативами;
- посилання на scenario comparison, case studies і diagnostic trace.

Канонічний приклад результату:

```text
Original:    (a & b) | (a & c)
Optimized:   a & (b | c)
Equivalent:  proven
Minimality:  proven
Cost:        4 -> 3 literals
```

Такий формат показує головну відмінність від простих expression simplifiers:
бібліотека не лише генерує менший вираз, а й повідомляє, що саме було доведено.

### 3.2 NuGet discoverability

Реалізовано:

- окремий README для кожного з дев'яти пакетів;
- package-specific descriptions і tags;
- `PackageProjectUrl` і repository metadata;
- license metadata;
- symbol packages;
- SourceLink-compatible repository metadata;
- окреме пояснення призначення facade, full bundle і modular packages.

Цей пункт завершений і на рівні source repository, і на рівні артефактів: фактичний вміст
кожного `.nupkg` тепер перевіряється автоматично — див. V1.

### 3.3 Scenario-driven onboarding

Створено окремий `samples` solution із п'ятьма runnable recipes:

1. feature configuration validation;
2. business-rule regression check;
3. count valid configurations;
4. optimize generated conditions;
5. CI verification.

Samples використовують публічні NuGet-style APIs, компілюються в CI та автоматично
перевіряють очікувані результати.

### 3.4 Production usability

Реалізовано:

- `--format=json` і `--json`;
- machine-readable CLI report;
- documented exit behavior;
- розділення stdout і stderr;
- structured parsing diagnostics через `TryParse`;
- позицію та довжину помилки, expected tokens, source snippet і diagnostic code;
- opt-in diagnostic trace;
- incremental SAT solving under assumptions;
- документовані budgets і result statuses.

Не слід об'єднувати ці завершені можливості з майбутнім batch/compiled API.

### 3.5 Trust і competitive evidence

Наявні:

- CI, Native AOT, NuGet, downloads, license і documentation badges;
- `SECURITY.md`, support guidance та issue templates;
- deterministic CI build settings;
- package validation і NuGet installation verification;
- release provenance/attestations;
- comparison methodology, pinned artifacts і raw/merged results;
- scenario comparison page;
- benchmarks і case studies.

Це означає, що задача змістилася від «створити trust signals» до «перевіряти їх
актуальність і домогтися зовнішнього відтворення».

## 4. Validate — найближча перевірка

### V1. Перевірити package contract до публікації та опубліковані NuGet packages після публікації — ✅ зроблено

Реалізовано [`tools/verify_package_contract.ps1`](tools/verify_package_contract.ps1): відкриває
кожен щойно спакований `.nupkg` як zip і виконує понад 160 перевірок контракту для дев'яти
пакетів:

- README всередині кожного `.nupkg` — оголошений, реально присутній, змістовний і згадує саме
  цей пакет;
- `Description` (змістовний і **унікальний** серед пакетів), `PackageTags`, project і
  repository URLs;
- license expression як SPDX (`Apache-2.0`), а не license file/URL;
- `.snupkg` із `.pdb` на кожен TFM + repository url/commit, потрібні SourceLink;
- відсутність будь-якої сторонньої runtime dependency;
- контрактні target frameworks у `lib/` (для CLI — `tools/`, `DotnetToolSettings.xml`,
  `packageType=DotnetTool`; для meta-пакета — транзитивне покриття всіх library-пакетів);
- встановлення facade, full bundle, CLI і **кожного** modular package окремим проєктом —
  `tools/smoke_install.ps1` перевіряє, що очікувані assemblies завантажуються з публічними
  типами;
- Native AOT smoke test **із пакета** — `smoke_install.ps1 -IncludeAot` збирає консьюмерську
  програму з `PublishAot=true` і запускає нативний бінарник.

Критерій завершення виконано на двох окремих рівнях:

- pre-publish audit пише machine-readable звіт (разом із переліком того, чого він **не**
  доводить), запускається на кожному PR і **гейтить `nuget push`**, перевіряючи саме локальні
  артефакти, які workflow збирається опублікувати;
- після push `verify_nuget.ps1` підтверджує появу всіх дев'яти пакетів у nuget.org index, а
  `smoke_install.ps1` встановлює опубліковану версію, перевіряє її публічні APIs і виконує Native
  AOT smoke test.

Звіти обох фаз входять до release evidence bundle (N3). Ретроспективний повний contract audit
раніше опублікованої версії потребує ручного завантаження `.nupkg` і `.snupkg` у теку та запуску
`verify_package_contract.ps1` з `-ArtifactsPath`.

### V2. Перевірити consistency публічних claims — ✅ зроблено

Створено єдиний глосарій [`doc/CLAIMS.md`](doc/CLAIMS.md). Для кожного терміна він фіксує
**дозволене формулювання**, точне значення, **доказ** (executable test, CI check або versioned
artifact) і **межі** — що claim свідомо *не* стверджує:

- `verified` — per-result перевірка незалежним oracle (truth table ≤12 змінних, SAT miter далі) з
  rollback до input при відмові. Межі: це не формальна верифікація самої бібліотеки і не
  зовнішній аудит;
- `minimal` — лише під заявленою cost model (літерали, потім терми, на two-level cover) і лише з
  явним status. Межі: не gate count, не depth, не delay; guarantee zone ≤10 змінних;
- `dependency-free` — жоден опублікований пакет не посилається на щось поза `LogicalOptimizer.*`.
  Межі: пакети посилаються один на одного, а test/benchmark проєкти використовують сторонні
  пакети, які не публікуються;
- `Native AOT support` — реально виконуваний нативний бінарник на `linux-x64` і `win-x64`, плюс
  AOT з опублікованого пакета. Межі: інші RID не перевіряються, CLI не публікується як AOT;
- `benchmark result` — завжди з версією, corpus і середовищем. Межі: wall-clock ніколи не
  асертиться, незалежного зовнішнього відтворення поки немає (V3);
- `no silent fallbacks` — статуси й trace.

Критерій завершення виконано **механічно**, а не дисциплінарно:
[`ClaimsConsistencyTests`](LogicalOptimizer.Tests/Techniques/ClaimsConsistencyTests.cs) валить
build, якщо публічний документ використовує заборонене формулювання або якщо доказ у глосарії
перестав існувати (перевіряється і файл, і наявність названого тест-методу в ньому). Claim не може
переживти те, що його підтверджує. Для легітимного випадку (цитата, формулювання обмеження) є явний
escape `<!-- claim-ok: reason -->`, видимий у review.

Виправлені розходження, знайдені аудитом:

- **абсолютні claims** «the most complete managed .NET Boolean-optimization toolkit» і
  «best-in-niche» прибрано з `docs-site/index.md` і `articles/introduction.md` — вони прямо
  суперечили §2 і §7 цього плану. Замінено на конкретний перелік можливостей із посиланням на
  pinned comparison і на `Choosing a tool`;
- **`dependency-free` без уточнення** — формулювання «zero production dependencies» / «zero
  runtime dependencies» замінено на «no third-party runtime dependency» у README, SECURITY.md,
  facade README, `docs-site/index.md`, `introduction.md` і `choosing-a-tool.md`. Обидві заборонені
  форми тепер гейтяться тестом (дві з них знайшов саме тест, а не ручний grep);
- **надто широке твердження про мінімальність** — README заявляв, що `MinimalProven` перевірено
  «для кожної 3- і 4-змінної функції», хоча exhaustive-тест мінімальності існував лише для 3
  змінних (4-змінний перевіряв збереження семантики, не мінімальність). Додано
  `TruthTableMinimizerTests.OptimizeExpression_AllFourVariableFunctions_MinimalProven` — усі 65534
  неконстантні функції дають `MinimalProven` і залишаються еквівалентними (1 хв 29 с, категорія
  `Exhaustive`). Твердження зроблено правдивим, а не ослаблено;
- **застарілий count** «~1180 gate tests» у `choosing-a-tool.md` приведено до фактичного.

### V3. Зовнішнє відтворення comparison results — ◐ підготовку завершено й перевірено; чекає на зовнішній runner

Усі технічні передумови виконані **і фактично прогнані**, а не лише задокументовані:

- pinned competitor versions — [`tools/comparison/Dockerfile`](tools/comparison/Dockerfile);
- однакове середовище — той самий контейнер;
- documented corpus і exclusions — [`doc/COMPARISON_METHODOLOGY.md`](doc/COMPARISON_METHODOLOGY.md),
  корпус із зафіксованим checksum;
- raw results — `doc/comparison/*_out.md` + `our-results.json`, кожне число з committed артефакту;
- hardware і runtime metadata — `manifest.json`;
- timeouts як окремий status — `timeout` ніколи не подається як failure;
- correctness окремо від performance — equivalence verdict рахується власним
  `EquivalenceChecker`, і harness падає, якщо будь-який рядок не `equivalent`, незалежно від
  таймінгів;
- послідовність із трьох команд для повного reproduction.

**Знайдена й закрита дірка.** Кожен competitor adapter self-skip'ається, коли інструмента немає
(це правильно — жодне число не фабрикується), але через це контейнер **виходив з кодом 0 навіть
якщо всі колонки лишилися `pending`**. Тобто «відтворилося» було неперевірюваним твердженням:
зовнішній читач не відрізнив би «інструменти не збіглися» від «образ зламаний». Додано
[`tools/verify_comparison_reproduction.ps1`](tools/verify_comparison_reproduction.ps1), який
перевіряє прогін замість того, щоб йому вірити: корпус саме той (за SHA-256, а не за іменем файлу),
середовище записане, **кожен** рядок `equivalent`, кожен miter `unsat`, обидва незалежні counter'и
збігаються на всіх model counts, і достатньо колонок реально заповнені. Таймінги не асертяться
ніколи. З `-CompareWith` він також перевіряє заявлений у методології детермінізм: усі не-таймінгові
поля мусять бути ідентичні committed прогону.

Додано job `reproduce-from-scratch` у [`comparison.yml`](.github/workflows/comparison.yml): чистий
checkout → дві документовані команди дослівно → верифікація. Це репетиція, а не виконання критерію:
якщо документована послідовність зіпсується, це виявить CI, а не та людина, чиє підтвердження V3 і
потребує.

**Фактичний прогін на чистому checkout (`git archive HEAD`, Docker, локально):** 10/10 перевірок
пройдено — усі **5/5** конкурентських колонок заповнені (SymPy/PyEDA, CaDiCaL/Kissat, Z3, d4,
LogicNG), 17 рядків `equivalent`, 17 miter'ів `unsat`, model counts збігаються, і всі
не-таймінгові поля **байт-у-байт** ідентичні committed результатам. Негативний напрямок перевірено
окремо: підсунутий all-`pending` звіт валиться з exit 1.

Критерій завершення (незалежний користувач або чужий runner) **не виконано і не може бути виконаний
зсередини репозиторію** — це єдине, що лишилося, і воно вимагає іншої людини. Тепер від неї
потрібні три команди й один звіт, без правки скриптів.

## 5. Next — пріоритетні наступні роботи

### N1. Compatibility і lifecycle policy — ✅ зроблено

Формалізовано в [SUPPORT.md](SUPPORT.md#versioning-and-compatibility-policy):

- підтримувані .NET target frameworks (drop TFM = breaking change);
- semantic versioning policy;
- що вважається breaking change для public API, CLI surface, JSON report і result statuses;
  окремо зафіксовано, що *якість* результату не є compatibility contract;
- строк підтримки попередньої major version — 12 місяців, зі scope виправлень
  (security + correctness) і `release/<major>.x` branch;
- deprecation process у чотири кроки: announce → `[Obsolete]` як warning (видно в pinned public
  API baseline) → незмінна поведінка до кінця major → видалення лише в наступному major;
- стабільність exit codes і JSON schema, з посиланням на [`schema/README.md`](schema/README.md).

### N2. Versioned CLI JSON schema — ✅ зроблено

- `schemaVersion` у кожному report (вже було);
- опубліковано JSON Schema Draft 2020-12
  [`schema/cli-report-v1.schema.json`](schema/cli-report-v1.schema.json), доступну з docs-сайту
  за тим самим `$id`, який вона декларує;
- golden compatibility tests —
  [`CliReportSchemaTests`](LogicalOptimizer.Tests/Cli/CliReportSchemaTests.cs): committed
  приклади валідуються схемою, свіжий вихід writer'а валідується **і** порівнюється з
  прикладами, enum'и схеми звіряються з CLR enum'ами через reflection, і окремий тест доводить,
  що схема **закрита** (невідоме поле відхиляється);
- additive та breaking changes документовано в [`schema/README.md`](schema/README.md);
- приклади success, budget exhaustion, `TooLarge` form, `advanced`, `--trace`, structured parse
  error і processing error — у [`schema/examples/`](schema/examples).

Закрита схема вимагає точного формулювання compatibility contract. Нове optional field або enum
member у межах `schemaVersion: 1` може бути additive для tolerant parser, який ігнорує невідомі
значення, але старий strict validator із попередньою копією v1 schema його відхилить. Тому такі
зміни не слід безумовно називати backward-compatible для всіх consumers. Перед наступною зміною
схеми потрібно зафіксувати одну з двох моделей:

1. залишити v1 schema закритою і випускати нову schema version для кожної зміни, яку повинні
   приймати strict validators;
2. дозволити additive поля в межах v1, але явно гарантувати сумісність лише tolerant parsers,
   опублікувати оновлену v1 schema та позначати несумісність зі старою копією schema як відоме
   обмеження.

До прийняття цього рішення поточний v1 документ і його golden examples зафіксовані, а додавання
нових полів або enum members не повинно виконуватися як звичайна minor/patch зміна.

Відхилення від початкового плану: golden-приклада для **non-equivalence** немає навмисно.
`equivalent: false` описано схемою, але optimize-шлях його не породжує — внутрішній equivalence
guard спрацьовує до повернення результату, тому `false` означав би баг бібліотеки. Перевірка
еквівалентності двох незалежних виразів із counterexample — це library API
(`EquivalenceChecker`), а не цей CLI report; це зафіксовано в `schema/README.md`.

### N3. Release evidence bundle — ✅ зроблено

[`tools/build_evidence_bundle.ps1`](tools/build_evidence_bundle.ps1) збирає в одну теку:

- package verification report;
- AOT smoke result (з опублікованого пакета);
- test summary, розпарсений із release `.trx`;
- benchmark manifest і raw results (опційний вхід `-BenchmarkManifest`);
- checksums;
- provenance verification instructions — `verifying-provenance.md` із командами для самостійної
  перевірки attestation, checksums, package contract, install/AOT і CLI JSON schema;
- claim changes відносно попереднього release — секція CHANGELOG цієї версії.

`INDEX.md` каже, що кожен файл доводить і чого **не** доводить; `manifest.json` дублює це
machine-readable із SHA-256 кожного файлу. Відсутні входи позначаються `absent`, а `-RequireAll`
валить release workflow і не дозволяє сформувати або опублікувати неповний evidence bundle.
Оскільки bundle збирається після `nuget push`, ця помилка не відкликає вже опубліковані NuGet
packages і не є pre-publish gate. Bundle прикріплюється до GitHub release тега (якщо той існує)
і завжди вантажиться як run-артефакт.

### N4. Adoption feedback loop — ✅ зроблено

Канал: окрема issue form
[`use_case_report.yml`](.github/ISSUE_TEMPLATE/use_case_report.yml) з label `use-case`, що
запитує саме перелічені поля:

- тип формул і типовий/найбільший розмір;
- частота операцій (від «раз на deploy» до «per request на hot path») — це те, що вирішує долю
  D1;
- потрібний proof/status — checkbox-список, який саме guarantee читає код, а не який звучить
  добре;
- deployment constraints;
- причина вибору **або відмови** від LogicalOptimizer, із назвою альтернативи, що перемогла;
- відсутня можливість окремо від documentation gap — бо це різні виправлення;
- окреме питання про дозвіл на цитування, що живить N5.

Правила збору — [`doc/ADOPTION.md`](doc/ADOPTION.md): жодної telemetry (бібліотека не робить
мережевих викликів, і це властивість, від якої залежить security scope), агрегація **вручну і
публічно** — числа відтворюються будь-ким із label `use-case`, нічого приховано не збирається.
Задокументовано, як кожне поле впливає на конкретне рішення, і що CHANGELOG-запис для розблокованої
роботи називатиме use cases, які її мотивували — так само, як claims посилаються на тести.

Очікуваний ефект досягається лише коли звіти почнуть надходити: наразі таблиця агрегації показує
нулі, і це зафіксовано явно, а не замовчано.

### N5. Зовнішні case studies

Поточні case studies демонструють можливості repository. Наступний рівень доказу —
хоча б один case study із зовнішнього проєкту:

- скільки виразів оброблено;
- який був розмір до і після;
- який verification status отримано;
- скільки часу й пам'яті використано;
- які integration constraints були важливими;
- чому було обрано LogicalOptimizer замість альтернативи.

## 6. Demand-driven — лише після підтвердження попиту

### D1. Compiled evaluator і batch API

Розглядати після появи повторюваного сценарію з вимірюваною проблемою:

- compiled evaluator для багаторазового виконання формули;
- batch evaluation;
- reusable BDD/d-DNNF query object;
- явні caching boundaries;
- thread-safety contracts;
- allocation і throughput benchmarks.

Incremental SAT under assumptions уже існує і не є майбутньою роботою.

### D2. Нові algorithmic engines

Додавати новий engine лише якщо:

- є зовнішній use case, який не закривають наявні SAT/BDD/d-DNNF/minimization paths;
- відома очікувана перевага;
- визначено correctness oracle;
- існує corpus для regression і benchmark;
- maintenance cost виправданий adoption data.

### D3. Optional ecosystem integrations

Інтеграції з ASP.NET, DI, serialization frameworks або іншими ecosystem
components реалізовувати лише як окремі optional packages і тільки за наявності
підтвердженого попиту. Core dependency graph не повинен зростати через такі
інтеграції.

## 7. Що не варто робити

Для збереження таргетування не рекомендується:

- перетворювати бібліотеку на загальний business rules engine;
- додавати власний DSL для workflow або policy management;
- намагатися наздогнати повний SMT stack Z3;
- позиціонувати SAT engine як competition-grade без відповідних доказів;
- входити в technology mapping, retiming або повний EDA flow;
- додавати UI/SaaS як основну частину продукту;
- створювати domain-specific abstractions без підтвердженого зовнішнього попиту;
- збільшувати dependency graph заради optional integrations;
- використовувати абсолютні competitive claims без versioned evidence.

## 8. Актуальна послідовність

| Черга | Робота | Стан | Очікуваний ефект | Зусилля |
|---:|---|---|---|---|
| 1 | Pre-publish package contract і post-publish NuGet verification | ✅ зроблено (V1) | Високий | Низькі |
| 2 | Claims consistency audit | ✅ зроблено (V2) | Високий | Низькі |
| 3 | Compatibility і lifecycle policy | ✅ зроблено (N1) | Високий | Низькі |
| 4 | Versioned CLI JSON Schema | ✅ зроблено (N2) | Високий | Середні |
| 5 | Release evidence bundle | ✅ зроблено (N3) | Середній/високий | Середні |
| 6 | External comparison reproduction | ◐ підготовка перевірена прогоном (V3); чекає на зовнішній runner | Середній/високий | Середні |
| 7 | Adoption feedback loop | ✅ канал створено (N4); чекає на звіти | Високий для roadmap | Низькі |
| 8 | External case study | ▫️ залежить від adoption (N5) | Високий для довіри | Залежить від adoption |
| 9 | Batch/compiled API | ▫️ лише після підтвердженого попиту | — | Середні/високі |
| 10 | Нові algorithmic engines | ▫️ лише після підтвердженого попиту | — | Високі |

Черги 1–5 і 7 закриті кодом, CI та документацією. **Усе, що можна закрити всередині репозиторію,
закрито.** Те, що лишилося, потребує не коду, а зовнішніх людей і часу:

- **черга 6 (V3)** — підготовку не лише завершено, а й **прогнано**: чистий checkout, документовані
  команди, 10/10 перевірок, 5/5 конкурентських колонок, детермінізм байт-у-байт. Дірку
  «self-skip виглядає як успіх» закрито окремим верифікатором. Бракує лише самого факту: щоб хтось
  незалежний прогнав ті самі три команди. Це не задача розробки;
- **черга 8 (N5)** — потребує зовнішнього проєкту, який погодиться навести цифри; канал і питання
  про дозвіл на цитування вже є в use-case формі;
- **черги 9–10 (D1, D2)** — свідомо заблоковані до появи звітів через N4. Побудувати їх «наперед»
  означало б зафіксувати неправильну форму API назавжди під compatibility policy.

Одне рішення все ж чекає на maintainer'а, а не на зовнішніх людей: модель сумісності закритої JSON
schema (два варіанти описані в N2). Його потрібно зафіксувати **до** наступної зміни схеми.

## 9. Вимірювані метрики успіху

Перед використанням кожної метрики потрібно зафіксувати baseline, джерело даних,
період і ціль.

| Метрика | Джерело | Початковий період | Приклад цілі |
|---|---|---|---|
| Успішність package verification | Release workflow | Кожен release | 100% packages pass |
| Time to first successful sample | Moderated clean-machine test | 5 нових користувачів | Median ≤ 10 хвилин |
| Documentation task success | Короткий usability test | Щоквартально | ≥ 80% без допомоги |
| External benchmark reproduction | Issue/discussion confirmation; локальна репетиція — job `reproduce-from-scratch` + [`comparison-reproduction-report.json`](tools/verify_comparison_reproduction.ps1) | За major release | ≥ 1 незалежний run |
| Зовнішні repositories із package reference | GitHub/NuGet-compatible search | Щоквартально | Позитивний trend |
| Issues із реальними use cases | [`use-case` label](https://github.com/AlexanderV/LogicalOptimizer/issues?q=label%3Ause-case), агрегація в [`doc/ADOPTION.md`](doc/ADOPTION.md) | Щоквартально | ≥ 3 actionable cases |
| Consistency публічних claims | [`ClaimsConsistencyTests`](LogicalOptimizer.Tests/Techniques/ClaimsConsistencyTests.cs) | Кожен CI run | 0 заборонених формулювань, 0 непідтверджених claims |
| CLI JSON contract regressions | Compatibility test suite | Кожен CI run | 0 unintended breaks |
| External contributors | Repository history | За 6 місяців | Позитивний trend |

NuGet downloads слід трактувати лише як reach signal, а не як доказ active usage
або retention. Без telemetry retention між releases напряму не вимірюється, тому
його не варто декларувати як доступну метрику.

## 10. Підсумкове рішення

Публічне перепакування LogicalOptimizer переважно завершене. Найкращий наступний
крок — не повторювати виконані P0/P1 роботи й не розширювати продукт у нові
предметні області, а перетворити наявні можливості на versioned, independently
verifiable product contract:

> **LogicalOptimizer — a verified, pure-managed Boolean reasoning toolkit for
> .NET with zero third-party runtime dependencies.**

Це позиціонування:

- відповідає поточній архітектурі;
- підкреслює реальні differentiators;
- уточнює значення dependency-free;
- не створює абсолютних competitive claims;
- не розмиває propositional targeting;
- відділяє продукт від простих expression simplifiers;
- задає перевірюваний напрям подальшого розвитку.
