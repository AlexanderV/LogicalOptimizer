# Валідоване порівняння LogicalOptimizer з аналогами (v2.0)

Дата перевірки: 27 липня 2026 року.

Цей документ описує стан бібліотеки після релізу **v2.0.0** і замінює попередні
порівняльні оцінки [UPDATED_LIBRARY_COMPARISON.md](UPDATED_LIBRARY_COMPARISON.md) та
[GLOBAL_LIBRARY_COMPARISON.md](GLOBAL_LIBRARY_COMPARISON.md) (обидві датовані 24.07.2026 і
описують архітектуру v1). Кожне конкретне твердження нижче або звірене з кодом (наведено
`файл:рядок`), або явно позначене як неперевірене / зовнішньо задокументоване.

---

## 1. Перевірений стан v2.0

### Реліз і платформа

- v2.0.0 випущено 2026-07-27 (коміт `8a098a6`) — перший свідомий breaking-реліз за політикою
  SemVer ([CHANGELOG.md](CHANGELOG.md), [MIGRATION-v2.md](MIGRATION-v2.md)).
- Target frameworks бібліотек: `net8.0;net10.0` (multi-target). CLI — `net10.0`, публікується
  як `dotnet tool` (`logical-optimizer`).
- **Нуль production/runtime залежностей.** Єдина посилка — `Microsoft.SourceLink.GitHub` з
  `PrivateAssets=All` (build-time only).
- **Нуль відомих вразливостей** (`dotnet list package --vulnerable --include-transitive` — чисто
  на всіх 8 проєктах).
- Тести: **880 кейсів зелені** (0 failed, 0 skipped, CI-фільтр). Line coverage фасаду 90.15%
  (branch 82.09%, method 93.53%). Джерело: [doc/TESTING.md](doc/TESTING.md), CHANGELOG.

### Архітектура v2

- N-ary AST: `NaryNode` з `IReadOnlyList<AstNode> Operands`; `AndNode`/`OrNode` — sealed
  спадкоємці (`LogicalOptimizer.Core/NaryNode.cs:10,40`, `AndNode.cs:8`, `OrNode.cs:8`).
  Розширені оператори (`XorNode/ImpNode/EqvNode/NandNode/NorNode`) лишаються бінарними
  derived-вузлами поза канонічним ядром.
- `FormulaFactory` — єдина точка побудови: construction-time flatten, канонічне сортування
  операндів, dedup, constant folding, complement folding + структурний інтернінг
  (структурно рівні дерева — один інстанс, reference equality)
  (`FormulaFactory.cs:122-214`; інтернінг через `ConcurrentDictionary`, рядок 18).
  `FormulaFactory.Parse` — публічний вхід парсингу; `Lexer/Parser/Token/TokenType` — `internal`.
- Єдиний внутрішній `RewriteEngine` (Rewrite-шар) замість 10 класів `IOptimizer`; порядок правил
  на вузол: De Morgan → absorption → consensus → redundancy → factorization (з rollback) + bounded
  expand-reduce + soundness guard, збережено 1:1 з v1 (CHANGELOG «Changed»).
- Публічна поверхня — **53 типи** у 5 пакетах (Core 20 · Sat 10 · Bdd 1 · Minimization 5 ·
  facade 17), закріплена member-level baseline-ом (`PublicApi.approved.txt`) та архітектурним
  тестом (`ArchitectureTests.PublicSurface_IsTheDocumentedSet`). Ациклічне низхідне шарування
  пакетів (Facade → Min/Sat/Bdd → Core; Min → Sat → Core; Bdd → Core) пінується архітектурним
  тестом.

### Верифікований інвентар можливостей (звірено з кодом 27.07.2026)

**SAT-ядро (`LogicalOptimizer.Sat`)** — усі claims підтверджено в `SatSolver.cs`:

- Two-watched literals, 1UIP clause learning.
- **VSIDS як бінарна купа** (`ActivityHeap`, `SatSolver.cs:689-768`) — O(log n) `PopMax`, а не
  лінійний скан.
- **LBD-based clause-DB reduction** (`ComputeLbd` 456-466, `ReduceLearntDatabase` 473-492,
  збереження glue-клауз при LBD ≤ 3).
- **Luby restarts** (`Luby` 293-310).
- **Subsumption + self-subsuming resolution** preprocessing (`PreprocessSubsumption` 506-575).
- **Incremental `Solve(assumptions)`** (161-262, стан зберігається між викликами).
- **UNSAT core** (`UnsatCore` 269, `AnalyzeFinal` 598-629).
- **DRAT proof logging** (`EnableProofLogging` 80-83, `ToDrat` 89-95).
- `CardinalityEncoder` — AtMost/AtLeast/ExactlyK (sequential counter, Sinz 2005;
  `CardinalityEncoder.cs:48-112`).
- `PseudoBooleanEncoder` — зважені суми (`CardinalityEncoder.cs:123-193`).
- `MaxSatSolver` — weighted partial MaxSAT (`MaxSatSolver.cs:47-135`).
- Tseitin **і** Plaisted–Greenbaum (`CnfEncodingStyle {Tseitin, PlaistedGreenbaum}`,
  `TseitinConverter.cs:223-234`; n-ary gate → n+1 клауз).

**BDD-ядро (`LogicalOptimizer.Bdd`, `BinaryDecisionDiagram.cs`)** — підтверджено:

- ROBDD: unique table + hash-consing + memoized `ite` (490-525).
- `BuildWithBestOrder` (358-391) і `BuildWithSiftedOrder` — Rudell-style sifting (402-448).
- `Exists`/`ForAll` (275-296), `Compose` (299-306), `Restrict` (268-272).
- Model counting `CountSatisfyingAssignments` через `BigInteger` (171-174);
  `EnumerateSatisfyingAssignments`/`FindSatisfyingAssignment` (206-265).
- Node budget (default 1e6, `MakeNode` 498-499).
- **Complement edges — ВІДСУТНІ** (вузол — простий триплет без sign-біта; `Negate` — повний
  `ite`, не O(1) flip). Це задокументований майбутній пункт (Трек D.1), не поточна можливість.

**Мінімізація (`LogicalOptimizer.Minimization`)** — підтверджено:

- `TruthTableMinimizer`: exact QM prime generation (`TruthTableMinimizer.cs:126-171`);
  branch-and-bound cover з **row/column dominance** (220-308) та **independent-set lower bound**
  (368-393, використовується в `CoverSearch` 342-345).
- `MinimizationStatus { MinimalProven, BudgetExceeded, Heuristic }` — точні імена підтверджено
  (живе у core-проєкті: `LogicalOptimizer/OptimizationOptions.cs:59-72`).
- `EspressoLiteMinimizer` — cube-list EXPAND/IRREDUNDANT/REDUCE з exact cofactor-tautology
  валідацією (`EspressoLiteMinimizer.cs:87-289`) — **internal**.
- `SatTwoLevelMinimizer` — SAT prime cover для смуги 13–24 змінних (`SatTwoLevelMinimizer.cs`,
  межа 24 = `MAX_SAT_MINIMIZATION_VARIABLES`) — **internal**.
- `MultiOutputMinimizer` — спільні куби між виходами (`TrySharedCovers` 63-111, приймається лише
  якщо дешевше за незалежне покриття) — **internal**.
- `SubcircuitLibrary` — 256-функційна precomputed-таблиця оптимальних форм для ≤3-змінних
  підсхем (`SubcircuitLibrary.cs:20-105`) — **internal**.

> Примітка: Espresso-lite, SAT-two-level, multi-output і subcircuit — **internal** класи;
> користувачу вони доступні через фасад (`BooleanExpressionOptimizer`), а не як окремі публічні
> типи. Публічні у пакеті Minimization лише 5 типів (`TruthTableMinimizer`, `CsvTruthTableParser`,
> `PartialTruthTable`, `MultiOutputTable`, `MultiOutputFunction`).

**Core + фасад** — підтверджено:

- `AndInverterGraph` — structural hashing, complemented edges, balanced n-ary folding —
  тепер **internal** (`AndInverterGraph.cs:12`), використовується для multi-level метрик і як
  база майбутнього cut-based rewriting.
- `TruthTable` (до 20 змінних), `NormalFormConverter` (CNF/DNF), `AstFormatter`
  (єдиний precedence-based рендерер; `ForceParentheses` видалено повністю).
- `Transformations.MinimizeDnfHeuristic` (Espresso-style), `ToEquisatisfiableCnf` (фасад,
  повертає публічний `TseitinCnf`).
- `FormulaAnalysis.ComputeBackbone` (SAT-based) + `EnumerateModels` (blocking-clause enumeration).
- `EquivalenceChecker` — XOR-miter + SAT, counterexamples, `CheckWithProof` з DRAT-сертифікатом.
- Експортери на `BooleanExpressionExporter`: `ToDimacs`, `ToBlif`, `ToVerilog`,
  `ToMathematicalNotation`, `ToLatex`; C#-експорт — окремий `CSharpExpressionExporter`.
  Разом 6 форматів + C#.

---

## 2. Глобальне позиціонування

> **LogicalOptimizer v2.0 — найповніший керований (managed) .NET-тулкіт булевої оптимізації:**
> parser + канонічний n-ary AST + пояснюване спрощення + доведена мала мінімізація +
> евристична велика мінімізація + два-три класи CNF-кодувань + власний CDCL-SAT (heap-VSIDS,
> LBD, Luby, subsumption, incremental, UNSAT-core, DRAT) + ROBDD із квантифікацією/sifting +
> cardinality/PB/MaxSAT + експортери + CLI — **усе в одному дистрибутиві без жодної
> native/production залежності.**

Це найсильніша позиція саме в ніші: **dependency-free + пояснюваність + широке покриття в одному
пакеті**. Стратегічна рамка ([LEADERSHIP_ROADMAP.md](LEADERSHIP_ROADMAP.md)) незмінна: «краще за
всіх у своїй ніші + достатньо в суміжних», а **не** паритет зі SMT-солверами.

Чого це **не** означає: LogicalOptimizer **не** є глобальним лідером у жодній окремій
спеціалізованій категорії. Кожен спеціалізований продукт випереджає його у своєму домені —
CaDiCaL/Kissat як raw-SAT, Espresso/ABC як EDA-мінімізатори, CUDD як BDD-движок, Z3 як SMT,
SymPy як CAS, LogicNG як зрілий JVM-фреймворк. Наша перевага — **інтеграція та відсутність
залежностей**, а не пікова потужність кожного алгоритму окремо.

---

## 3. Матриця можливостей

Легенда: `++` сильна спеціалізована підтримка · `+` штатна підтримка · `±` часткова / з
обмеженнями · `−` відсутня.

| Можливість | LogicalOptimizer v2 | LogicNG 3 | Z3 | CaDiCaL/Kissat + PySAT | Espresso | ABC | CUDD/dd | SymPy | PyEDA |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Нативний managed .NET | ++ | − | ± | − | − | − | − | − | − |
| Нуль production-залежностей | ++ | + | − | ± | − | − | − | + | − |
| Parser виразів + канонічний AST | ++ | ++ | ± | − | − | ± | ± | + | ++ |
| Пояснюване symbolic rewriting | ++ | ++ | + | − | − | − | − | ++ | + |
| Exact мала SOP/POS | ++ | + | − | − | ± | ± | − | ++ | ± |
| Велика heuristic two-level | + | + | − | − | ++ | ++ | − | − | ++ |
| Multi-output зі спільними кубами | + | ± | − | − | ++ | ++ | − | − | ++ |
| Multi-level / logic synthesis | ± | − | − | − | − | ++ | − | − | ± |
| Equivalent CNF/DNF | ++ | ++ | ± | − | − | − | ± | ++ | ++ |
| Tseitin + Plaisted–Greenbaum CNF | ++ | ++ | ++ | ++ | − | − | − | ± | ± |
| CDCL SAT-солвер | + | ++ | ++ | ++ | − | + | − | ± | + |
| Incremental SAT + assumptions | + | ++ | ++ | ++ | − | ± | − | − | ± |
| UNSAT core | + | ++ | ++ | ++ | − | ± | − | − | − |
| DRAT-докази | + | ± | ++ | ++ | − | ± | − | − | − |
| MaxSAT | + | ++ | ± | ++ | − | − | − | − | − |
| Cardinality / PB constraints | + | ++ | ++ | ++ | − | − | − | − | − |
| ROBDD | + | ++ | ± | − | − | ± | ++ | − | ++ |
| BDD reordering (sifting) | + | + | − | − | − | ± | ++ | − | ± |
| BDD quantification/compose/restrict | + | + | ± | − | − | ± | ++ | − | ± |
| BDD complement edges | − | + | − | − | − | ± | ++ | − | ± |
| Model counting | + | ++ | ± | ± | − | − | + | ± | + |
| Backbone / model enumeration | + | ++ | + | + | − | − | ± | ± | ± |
| AIG / structural hashing | ± | − | − | − | − | ++ | − | − | ± |
| Скасування / resource budgets | ++ | ++ | ++ | + | ± | ± | ± | ± | ± |
| DIMACS / BLIF / Verilog / LaTeX / C# export | ++ | ± | ±(SMT-LIB) | ±(DIMACS) | ±(PLA) | ++ | − | ± | ± |
| Готовий CLI / dotnet tool | ++ | − | ± | ± | ++ | ++ | − | − | ± |
| Доведена коректність кожного результату | ++ | ± | ± | ± | − | ± | − | ± | − |

> Оцінки конкурентів у стовпцях — за їхньою публічною документацією та кодом, **не** повторно
> заміряні на єдиному стенді; це якісна орієнтація, а не benchmark. Оцінки колонки
> LogicalOptimizer — код-верифіковані (розділ 1).

---

## 4. Аналіз за конкурентами

Для кожного: де LogicalOptimizer тепер стоїть після v2 і **конкретне залишкове відставання**.

### LogicNG 3 (зрілий JVM logic framework)

Найближчий концептуальний аналог. LogicNG 3 має immutable hash-consed formula factory, n-ary
AND/OR, канонізацію при побудові, MiniSat/Glucose/MiniCARD, incremental solving, MaxSAT,
cardinality/PB, BDD і DNNF, proofs/cores.

**Де LogicalOptimizer тепер:** v2 закрив головні архітектурні розриви, за які критикувала версія
24.07 — тепер **є** справжній n-ary `FormulaFactory` з construction-time канонізацією та
інтернінгом (пряма аналогія до formula factory LogicNG), incremental SAT з assumptions, UNSAT-core,
cardinality/PB/MaxSAT. За **широтою галочок** ми тепер зіставні, а за deployment у .NET —
недосяжні для JVM-бібліотеки.

**Залишкове відставання:** зрілість і глибина. LogicNG має роки продакшн-adoption, комерційну
підтримку, DNNF (у нас немає), багатший вибір SAT-бекендів і CNF-кодувань, глибшу документацію.
Наш SAT — один власний CDCL, а не набір промислових солверів. **Gap: зрілість екосистеми та DNNF,
а не набір фіч.**

### Z3 (SMT)

**Де LogicalOptimizer тепер:** ми свідомо **не** конкуруємо як SMT. У чисто propositional-задачах
(equivalence, SAT, backbone, model counting, cardinality/PB/MaxSAT) v2 має самодостатній стек без
native-залежності — це наша перевага для .NET-інтеграції та explainability (результат — читабельний
булевий вираз, а не solver-verdict).

**Залишкове відставання:** усі теорії (arithmetic, bit-vectors, arrays, strings, quantifiers),
tactics, промислова масштабованість на сотнях тисяч змінних, десятиліття інженерії. **Це свідомий
non-goal** (LEADERSHIP_ROADMAP §2). Для тих, кому потрібні Z3-гарантії, T7 передбачає опційний
адаптер `IEquivalenceChecker` без внесення native-залежності в core. **Gap: SMT-теорії та
solver-потужність — принципова, не тактична.**

### CaDiCaL / Kissat-class + PySAT (raw SAT потужність/перф)

**Де LogicalOptimizer тепер:** наш CDCL має сучасний набір технік — heap-VSIDS, LBD-редукцію
learnt-DB, Luby-restarts, subsumption + self-subsuming, incremental, UNSAT-core, DRAT. Це вже не
навчальний DPLL: архітектурно він у тому ж класі технік, що й сучасні inprocessing-солвери
(підтверджено кодом, розділ 1).

**Залишкове відставання:** абсолютна продуктивність. CaDiCaL/Kissat — це роки low-level
оптимізації (bounded variable elimination, vivification, chronological backtracking, кеш-дружні
структури, target phases). Наш benchmark — `PhaseTransition60Variables` 471.6 μs
([doc/BENCHMARKS.md](doc/BENCHMARKS.md)) — свідчить про коректність і адекватність на наших
масштабах, але ми **не** заміряні на SATLIB-конкурсних інстансах (Трек C — vendored корпус ще не
підключено). PySAT дає уніфікований доступ до десятка топ-солверів + готові кодування. **Gap:
пікова перф на великих індустріальних інстансах + відсутній публічний SATLIB-бенчмарк.**

### Berkeley Espresso (індустріальна two-level мінімізація)

**Де LogicalOptimizer тепер:** v2 має `EspressoLiteMinimizer` — власну cube-list реалізацію
EXPAND/IRREDUNDANT/REDUCE з exact cofactor-tautology валідацією (sound by construction),
що стискає DNF-покриття на 40+ змінних (`EspressoLite_FortyVariableCover` у BENCHMARKS). Це закриває
рядок «немає Espresso backend» із версії 24.07 — тепер у нас є heuristic-режим для великих таблиць.

**Залишкове відставання:** Espresso — це десятиліття відточених евристик (справжній UNATE-recursive
complement, expand-героїстики, makesparse, PLA-workflow на 50 входах × кількох виходах). Наш
«espresso-lite» — свідомо простіший (LEADERSHIP_ROADMAP позначає це `+`, а не `++`). **Gap: якість
покриття та масштаб на промислових PLA — принципова поступка заради нульових залежностей.**

### Berkeley ABC (logic synthesis / technology mapping)

**Де LogicalOptimizer тепер:** v2 має internal `AndInverterGraph` зі structural hashing,
complemented edges і **balanced n-ary folding** — це база (перший крок Треку D.2), а `SubcircuitLibrary`
дає локальний optimal-rewrite ≤3-змінних підсхем (концептуально близько до cut-based rewriting).

**Залишкове відставання:** ABC працює на рівні **логічних мереж і схем** — DAG-aware rewriting,
technology mapping (FPGA/ASIC), sequential synthesis, retiming, промислова formal verification. У нас
AIG використовується лише для метрик, DAG-aware rewriting ще не реалізовано (Трек D.2 — пост-v2).
**Gap: багаторівнева синтезна оптимізація та technology mapping — цілий домен, якого ми торкаємось
лише мінімально.**

### CUDD / dd (decision diagrams)

**Де LogicalOptimizer тепер:** наш ROBDD — вже не «базовий», як у критиці 24.07. v2 має
quantification (`Exists`/`ForAll`), `Compose`, `Restrict`, sifting-reordering (`BuildWithSiftedOrder`),
best-order евристику, `BigInteger` model counting, node budget. За **набором BDD-операцій** ми
наблизилися до інтегрованих BDD-бібліотек.

**Залишкове відставання:** CUDD/dd — це ADD/ZDD, **complement edges** (у нас ВІДСУТНІ — Трек D.1,
виграш ~2× пам'яті), власний memory-pool/GC, dynamic reordering під час операцій, декади
оптимізації. Наш sifting — rebuild-based (перебудовує діаграму), а не in-place swap-based. **Gap:
complement edges + масштаб/перф ядра + ADD/ZDD.**

### SymPy (symbolic math)

**Де LogicalOptimizer тепер:** у **точній малій булевій мінімізації** ми зіставні або сильніші —
доведена мінімальність до 10 змінних (exhaustively verified для всіх функцій n=3,4), бюджетований
QM до 12, з явним `MinimizationStatus`. Захисний поріг SymPy `simplify_logic` — 8 змінних без
`force=True`. Наш диференціальний корпус проти `simplify_logic` (SymPy як зовнішній оракул) — у CI
([doc/TESTING.md](doc/TESTING.md)).

**Залишкове відставання:** SymPy — це повна CAS (calculus, equations, matrices, ANF, теорія чисел)
з величезною науковою екосистемою та стабільним API. Ми — вузько булеві. **Gap: математична широта
та зрілість екосистеми, не булева мінімізація.**

### PyEDA (Python EDA)

**Де LogicalOptimizer тепер:** v2 звів розрив у EDA-workflow: є expressions, truth tables, ROBDD,
formal equivalence, власний SAT, Espresso-lite для великих таблиць, multi-output CSV зі спільними
кубами, PLA-подібні експортери (DIMACS/BLIF/Verilog). За **інтегрованістю managed-workflow** ми
тепер ширші за PyEDA у .NET-контексті.

**Залишкове відставання:** PyEDA лінкує C-extension Berkeley Espresso (справжній, не lite) і
демонструє PLA 50×5 — інший масштаб heuristic-мінімізації. Проте PyEDA — старіший maintenance
(останній значний release датований 2018), тож розрив закривається радше нашим розвитком, ніж їхнім.
**Gap: справжній Espresso-backend і промисловий PLA-масштаб.**

---

## 5. Що змінив v2.0 проти порівняння від 24.07.2026

Версії 24.07 фіксували вісім конкретних архітектурних відставань (розділ «Де бібліотека все ще
слабша» в UPDATED_LIBRARY_COMPARISON.md). Стан кожного після v2:

| Відставання станом на 24.07 (v1) | Стан у v2.0 (код-верифіковано) |
|---|---|
| «AST бінарний і частково mutable; `ForceParentheses` — mutable» | **Закрито.** N-ary `NaryNode`/`AndNode`/`OrNode`; AST повністю immutable; `ForceParentheses` видалено; рендеринг через `AstFormatter`. |
| «Немає formula factory та повного hash-consing AST» | **Закрито.** `FormulaFactory` з construction-time канонізацією + структурний інтернінг (reference equality), `FormulaFactory.cs:122-214`. |
| «SAT не incremental; немає assumptions, proof tracing, unsat core» | **Закрито.** `Solve(assumptions)`, `UnsatCore`, DRAT (`ToDrat`) — усе в `SatSolver.cs`. Додатково: heap-VSIDS, LBD, Luby, subsumption+self-subsuming. |
| «BDD backend базовий; немає reordering, quantification/composition» | **Закрито.** `Exists`/`ForAll`, `Compose`, `Restrict`, `BuildWithBestOrder`, `BuildWithSiftedOrder`. (Complement edges — досі ні, Трек D.1.) |
| «Multi-output не мінімізується спільно» | **Закрито.** `MultiOutputMinimizer.TrySharedCovers` — спільні куби між виходами. |
| «Немає cardinality, PB, MaxSAT» | **Закрито.** `CardinalityEncoder`, `PseudoBooleanEncoder`, `MaxSatSolver`. |
| «Немає Espresso backend» | **Частково закрито (`+`).** `EspressoLiteMinimizer` (EXPAND/IRREDUNDANT/REDUCE); повний Espresso — свідомий non-goal. |
| «Публічний API завеликий; немає поділу packages» | **Закрито.** 5 пакетів, звужено до 53 публічних типів, пінується двома тестами. |

Також закрито критичну поправку 24.07 щодо «provably minimal»: тепер є явний
`MinimizationStatus { MinimalProven, BudgetExceeded, Heuristic }` без тихого greedy-fallback
(README, `OptimizationOptions.cs:59-72`).

**Що v2 навмисно НЕ змінив** (незмінний behavior-контракт фасаду): зонний роутинг (≤10 guarantee /
≤12 бюджетований QM / 13–24 SAT prime cover / >24 espresso-lite), статуси, budget-и, обов'язкова
equivalence-верифікація кожного результату.

**Що лишилося відкритим після v2** (з V2_PLAN «Залишок» і Трек D):

- Тег `v2.0.0` + публікація 6 NuGet-пакетів (дія користувача; workflow готовий).
- DocFX-сайт (Трек B).
- SATLIB-корпус (Трек C) — публічного SAT-бенчмарку ще немає.
- BDD complement edges (Трек D.1).
- AIG DAG-aware rewriting у стилі ABC (Трек D.2).

---

## 6. Оцінка за категоріями

Шкала 1–5 — позиція в категорії, не абсолютна якість.

| Категорія | LogicalOptimizer v2 | Лідер категорії | Коментар |
|---|---:|---|---|
| Легке вбудовування в .NET | 5 | LogicalOptimizer | нуль native/production залежностей, multi-target net8/net10 |
| Читабельне symbolic simplification | 5 | LogicNG / SymPy / LO | тепер канонічний n-ary AST + factory усувають головну ваду v1 |
| Точна мала SOP/POS | 5 | SymPy / LogicalOptimizer | доведена ≤10, бюджетована ≤12, явний статус |
| Велика two-level мінімізація | 3 | Espresso / PyEDA | є espresso-lite (`+`), але не промисловий Espresso |
| Multi-level synthesis | 2 | ABC | лише AIG-метрики + subcircuit rewrite; DAG-rewriting попереду |
| SAT capability | 4 | Z3 / CaDiCaL | сучасний CDCL (heap-VSIDS/LBD/Luby/incremental/core/DRAT), без пікової перф |
| BDD capability | 4 | CUDD / dd | quantification+sifting+counting; бракує complement edges + перф ядра |
| CNF conversion | 5 | LogicNG / LO | Equivalent + Tseitin + Plaisted–Greenbaum |
| Cardinality/PB/MaxSAT | 4 | LogicNG / Z3 | усе in-house, але без промислового тюнінгу |
| CLI та формати | 5 | LogicalOptimizer | 6 форматів + C# + dotnet tool |
| Зрілість / ecosystem | 2 | Z3 / SymPy / LogicNG | молодий проєкт; adoption ще не підтверджена, NuGet-публікація попереду |

---

## 7. Підсумок і рекомендоване позиціонування

v2.0 якісно змінив позицію: LogicalOptimizer перейшов від «сильного прототипу з бінарним AST» до
**архітектурно зрілого managed-тулкіта**, що закриває сім з восьми відставань версії 24.07, а восьме
(Espresso) закриває свідомо частково. Жодного рядка матриці, де конкурент `++`, а ми `−`, окрім
двох принципових поступок (промисловий Espresso/ABC-synthesis і complement-edge BDD-перф) та
неминучого SMT-розриву з Z3.

Найкраще формулювання:

> **LogicalOptimizer v2.0 — найповніший dependency-free .NET-тулкіт для пояснюваного спрощення,
> доведеної малої мінімізації, масштабованого CNF, власного CDCL-SAT (incremental/core/DRAT),
> ROBDD із квантифікацією та cardinality/PB/MaxSAT — з обов'язковою верифікацією кожного результату.**

Він не є і не прагне бути заміною Z3 (SMT), CaDiCaL/Kissat (raw-SAT-перф), Espresso/ABC
(промислова EDA-синтеза) чи CUDD (BDD-ядро). Його перевага — **інтеграція всього стеку в одному
managed-дистрибутиві без native-залежностей + пояснюваність + доведена коректність**.

---

## 8. Джерела

### Внутрішні (код-верифіковано)

- Код: `LogicalOptimizer.Sat/SatSolver.cs`, `CardinalityEncoder.cs`, `MaxSatSolver.cs`,
  `TseitinConverter.cs`; `LogicalOptimizer.Bdd/BinaryDecisionDiagram.cs`;
  `LogicalOptimizer.Minimization/TruthTableMinimizer.cs`, `EspressoLiteMinimizer.cs`,
  `SatTwoLevelMinimizer.cs`, `MultiOutputMinimizer.cs`, `SubcircuitLibrary.cs`;
  `LogicalOptimizer.Core/NaryNode.cs`, `FormulaFactory.cs`, `AndInverterGraph.cs`,
  `AstFormatter.cs`; `LogicalOptimizer/EquivalenceChecker.cs`, `FormulaAnalysis.cs`,
  `BooleanExpressionExporter.cs`, `Transformations.cs`, `OptimizationOptions.cs`.
- [CHANGELOG.md](CHANGELOG.md), [MIGRATION-v2.md](MIGRATION-v2.md), [V2_PLAN.md](V2_PLAN.md),
  [LEADERSHIP_ROADMAP.md](LEADERSHIP_ROADMAP.md), [README.md](README.md),
  [doc/TESTING.md](doc/TESTING.md), [doc/BENCHMARKS.md](doc/BENCHMARKS.md).

### Зовнішні (за публічною документацією конкурентів, не заміряно локально)

- [LogicNG](https://logicng.org/) — formula factory, SAT, CNF transformations, cardinality.
- [Microsoft Z3](https://github.com/Z3Prover/z3) — SMT solver і .NET binding.
- [CaDiCaL](https://github.com/arminbiere/cadical), [Kissat](https://github.com/arminbiere/kissat),
  [PySAT](https://github.com/pysathq/pysat).
- [Berkeley Espresso](https://people.eecs.berkeley.edu/~alanmi/research/espresso/espresso_5.html),
  [Berkeley ABC](https://github.com/berkeley-abc/abc).
- [CUDD](https://github.com/SSoelvsten/cudd), [dd](https://github.com/tulip-control/dd).
- [SymPy Logic](https://docs.sympy.org/latest/modules/logic.html),
  [PyEDA](https://pyeda.readthedocs.io/en/latest/).
