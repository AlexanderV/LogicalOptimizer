# План посилення позиції LogicalOptimizer без розмивання таргетування

## 1. Висновок

LogicalOptimizer уже має сильну технічну основу:

- власні SAT, BDD і d-DNNF engines;
- обов'язкову перевірку еквівалентності;
- явні статуси мінімальності та обчислень;
- модульні NuGet-пакети;
- zero production dependencies;
- підтримку Native AOT і trimming;
- імпорт, експорт і CLI;
- відтворювані тести та benchmarks.

Основний резерв для посилення конкурентної позиції зараз лежить не в додаванні
ще одного solver або формату, а в трьох напрямках:

1. зрозуміліше позиціонування;
2. швидший onboarding;
3. сильніші зовнішні докази довіри та практичної цінності.

Технічні можливості продукту вже випереджають те, наскільки легко потенційний
користувач може зрозуміти його користь.

## 2. Цільове позиціонування

### Рекомендований короткий опис

> **Verified Boolean reasoning toolkit for .NET**  
> Optimize, compare, count, and solve Boolean formulas with zero runtime
> dependencies.

Розширений варіант:

> LogicalOptimizer is a dependency-free .NET toolkit for verified Boolean
> optimization, equivalence checking, SAT solving, model counting, and knowledge
> compilation. Every optimization result is checked for equivalence, while
> minimality and resource-limit outcomes are reported explicitly.

### Ключова обіцянка продукту

LogicalOptimizer — це найбезпечніший і найзручніший спосіб виконувати
перевірювані Boolean-операції всередині managed .NET application без Z3,
native runtime або прихованих fallback-механізмів.

### Основні сценарії

- оптимізація feature та configuration expressions;
- перевірка еквівалентності двох версій умови;
- отримання counterexample для нееквівалентних формул;
- перевірка satisfiability та constraints;
- точний і weighted model counting;
- мінімізація згенерованих C#, Verilog або Boolean expressions;
- embedded і Native AOT deployment без native dependencies.

Ці сценарії не змінюють межі продукту. Вони лише пояснюють практичну цінність
наявного propositional toolkit.

## 3. P0 — Перепакування публічної подачі

### P0.1 Скоротити перший екран README

Перші один-два екрани README мають містити:

1. коротку value proposition;
2. три основні differentiators;
3. команду встановлення;
4. один короткий C# приклад;
5. таблицю вибору між LogicalOptimizer та альтернативами.

Рекомендовані differentiators:

- **Verified results** — кожна оптимізація перевіряється на еквівалентність;
- **Explicit proof status** — мінімальність і fallback ніколи не приховуються;
- **Pure managed .NET** — zero production dependencies, AOT і trimming support.

Детальний feature catalog варто залишити нижче або перенести до документації.

### P0.2 Показувати результат як доказовий звіт

Замість демонстрації лише перетвореного виразу:

```text
Original:   (a & b) | (a & c)
Optimized:  a & (b | c)
Equivalent: proven
Minimality: proven
Cost:       6 -> 4 literals
```

Такий формат одразу показує головну відмінність від простих expression
simplifiers: бібліотека не лише генерує менший вираз, а й повідомляє, що саме
було доведено.

### P0.3 Додати коротку таблицю конкурентного вибору

| Потреба | Рекомендований вибір |
|---|---|
| Managed .NET без native dependencies | **LogicalOptimizer** |
| Verified Boolean expression optimization | **LogicalOptimizer** |
| Equivalence checking із counterexample | **LogicalOptimizer** |
| Повний SMT та арифметичні теорії | Z3 |
| Competition-scale raw SAT throughput | Kissat або CaDiCaL |
| Industrial logic synthesis | Berkeley ABC |
| Зрілий JVM propositional ecosystem | LogicNG |

Таблиця має пояснювати межі продукту, а не створювати необґрунтовану заяву про
абсолютну перевагу над усіма інструментами.

## 4. P0 — Покращення NuGet discoverability

### P0.4 Окремий README для кожного пакета

Не варто використовувати однаковий великий README для всіх дев'яти пакетів.
Кожен пакет має отримати коротку спеціалізовану сторінку:

| Пакет | Основне повідомлення |
|---|---|
| `LogicalOptimizer` | Verified Boolean optimization facade |
| `LogicalOptimizer.Full` | Увесь toolkit одним встановленням |
| `LogicalOptimizer.Core` | Canonical Boolean AST, parser і truth tables |
| `LogicalOptimizer.Sat` | Pure managed CDCL SAT, MaxSAT і encodings |
| `LogicalOptimizer.Bdd` | ROBDD, quantification і model counting |
| `LogicalOptimizer.Dnnf` | Knowledge compilation і repeated counting queries |
| `LogicalOptimizer.Minimization` | Exact, heuristic і multi-output minimization |
| `LogicalOptimizer.Formats` | DIMACS, WCNF і OPB interoperability |
| `LogicalOptimizer.Cli` | Boolean optimization і analysis з командного рядка |

Кожна сторінка повинна містити:

- одну команду встановлення;
- один мінімальний приклад;
- очікуваний результат;
- посилання на повну документацію;
- чітке пояснення, коли слід обрати саме цей пакет.

### P0.5 Покращити package metadata

Для кожного пакета потрібно перевірити:

- `Description`;
- `PackageTags`;
- `PackageProjectUrl`;
- repository metadata;
- release notes;
- README;
- license;
- symbol package;
- SourceLink.

Теги мають включати не лише назви алгоритмів, а й терміни, за якими користувач
шукає розв'язання задачі:

- `boolean-expression`;
- `expression-simplification`;
- `equivalence-checking`;
- `configuration`;
- `feature-model`;
- `model-counting`;
- `native-aot`;
- `pure-managed`.

## 5. P1 — Scenario-driven onboarding

### P1.1 Додати runnable recipes

Рекомендований мінімальний набір:

1. **Feature configuration validation**  
   Перевірити, чи існує припустима конфігурація, та отримати одну модель.

2. **Business-rule regression check**  
   Перевірити еквівалентність старої й нової версії умови та отримати
   counterexample у разі зміни поведінки.

3. **Count valid configurations**  
   Обчислити кількість припустимих комбінацій через BDD або d-DNNF.

4. **Optimize generated conditions**  
   Скоротити згенерований Boolean expression і підтвердити його еквівалентність.

5. **CI verification**  
   Виконати перевірку через CLI та зберегти machine-readable artifact.

Recipes мають бути тонкими прикладами поверх наявного API. Не потрібно додавати
domain-specific rules engine або новий abstraction layer.

### P1.2 Створити окремий `samples` solution

Вимоги:

- кожен sample компілюється в CI;
- кожен sample короткий і самодостатній;
- приклади використовують публічні NuGet-style APIs;
- результати перевіряються автоматично;
- README кожного sample пояснює практичну задачу, а не лише класи API.

## 6. P1 — Production usability

### P1.3 Machine-readable CLI

Додати або стандартизувати:

- `--format json`;
- стабільну JSON schema;
- documented exit codes;
- окремі поля для equivalence, minimality, status, cost і diagnostics;
- коректну поведінку stdout/stderr;
- можливість використання результату як CI artifact.

### P1.4 Structured parsing diagnostics

Корисні доповнення:

- `TryParse`;
- позиція помилки;
- довжина проблемного token;
- expected tokens;
- source snippet;
- machine-readable diagnostic code.

Це покращує інтеграцію бібліотеки в IDE, API, configuration UI і build tools,
не змінюючи предметну область продукту.

### P1.5 Diagnostic trace

Користувачеві має бути доступна відповідь на запитання:

- який engine було обрано;
- чому було обрано саме його;
- який budget використано;
- який proof або verification path спрацював;
- чому виконання завершилося fallback/status результатом;
- який кандидат був відхилений і чому.

Trace має бути opt-in, структурованим і придатним для production diagnostics.

### P1.6 Batch і reuse API

Після підтвердження попиту варто розглянути:

- compiled evaluator для багаторазового виконання формули;
- batch evaluation;
- reuse SAT state для повторних запитів;
- reuse BDD/d-DNNF circuit;
- documented caching boundaries;
- thread-safety contracts.

Це підсилює наявні use cases без додавання нової категорії продукту.

## 7. P1 — Довіра та adoption

### P1.7 Додати trust signals

У верхній частині README доречні:

- CI status;
- NuGet version;
- NuGet downloads;
- license;
- documentation;
- Native AOT validation;
- test coverage, якщо метрика стабільна й чесно відтворюється.

### P1.8 Формалізувати підтримку

Додати:

- `SECURITY.md`;
- issue templates;
- feature request template;
- bug-report reproduction template;
- support/versioning policy;
- compatibility policy;
- GitHub Discussions або інший визначений канал для use-case feedback.

### P1.9 Посилити supply-chain довіру

Перевірити доцільність:

- deterministic builds;
- package validation у CI;
- SBOM;
- build provenance/attestations;
- signed release artifacts;
- автоматичної перевірки опублікованих NuGet-пакетів;
- reproducible installation smoke tests.

## 8. P2 — Публічні конкурентні докази

### P2.1 Створити коротку comparison page

Сторінка повинна відповідати на три питання:

1. де LogicalOptimizer має обґрунтовану перевагу;
2. де він конкурентний;
3. де варто обрати інший інструмент.

Необхідно чітко розділяти:

- feature comparison;
- output-quality comparison;
- performance benchmark;
- platform/deployment comparison;
- maturity/adoption.

### P2.2 Публікувати benchmarks як release artifacts

Для кожного значного релізу:

- pinned competitor versions;
- однакове середовище;
- опис corpus;
- raw results;
- merged report;
- hardware і runtime metadata;
- timeouts як окремий статус;
- correctness окремо від performance.

### P2.3 Додати практичні case studies

Навіть невеликі приклади корисніші за загальні заяви:

- скільки виразів оброблено;
- який був розмір до і після;
- який verification status отримано;
- скільки часу й пам'яті використано;
- чому було обрано LogicalOptimizer замість альтернативи.

## 9. Що не варто робити

Для збереження таргетування не рекомендується:

- перетворювати бібліотеку на загальний business rules engine;
- додавати власний DSL для workflow або policy management;
- намагатися наздогнати повний SMT stack Z3;
- позиціонувати SAT engine як competition-grade без відповідних доказів;
- входити в technology mapping, retiming або повний EDA flow;
- додавати UI/SaaS як основну частину продукту;
- створювати domain-specific abstractions без підтвердженого зовнішнього попиту;
- збільшувати dependency graph заради optional integrations.

Якщо потрібна інтеграція з ASP.NET, DI, serialization framework або іншим
ecosystem component, її краще реалізовувати як окремий optional package.

## 10. Рекомендована послідовність

| Черга | Робота | Очікуваний ефект | Зусилля |
|---:|---|---|---|
| 1 | Новий hero і скорочений README | Високий | Низькі |
| 2 | Package-specific NuGet README та metadata | Високий | Низькі |
| 3 | П'ять runnable recipes | Високий | Середні |
| 4 | JSON report і stable exit codes | Високий | Середні |
| 5 | Scenario comparison page | Середній/високий | Низькі |
| 6 | Trust і adoption assets | Середній/високий | Низькі |
| 7 | Structured parsing diagnostics | Середній | Середні |
| 8 | Batch/reuse API | Залежить від adoption data | Середні/високі |
| 9 | Нові алгоритмічні engines | Лише після підтвердженого попиту | Високі |

## 11. Метрики успіху

Оцінювати результат варто не лише за кількістю реалізованих features:

- conversion із перегляду repository в перехід до документації;
- NuGet downloads за пакетами;
- кількість зовнішніх repositories, що залежать від пакетів;
- час до першого успішного прикладу;
- кількість issue/discussion із реальними use cases;
- частка користувачів facade проти modular packages;
- використання CLI JSON mode;
- кількість відтворених external benchmarks;
- кількість зовнішніх contributors;
- retention між релізами.

## 12. Підсумкове рішення

Найкращий наступний крок — не розширювати LogicalOptimizer у нові предметні
області, а зробити очевидною вже наявну перевагу:

> **LogicalOptimizer — verified, dependency-free Boolean reasoning toolkit for
> .NET applications.**

Це позиціонування:

- відповідає поточній архітектурі;
- підкреслює реальні differentiators;
- не створює необґрунтованих claims;
- не розмиває propositional targeting;
- відділяє продукт від простих expression simplifiers;
- пояснює, чому .NET-розробник має обрати його замість native або
  general-purpose alternatives.
