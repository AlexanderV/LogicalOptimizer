# TODO: наступний цикл розвитку LogicalOptimizer

> **Наступний цикл (v2.0):** детальний план — [V2_PLAN.md](V2_PLAN.md) (27.07.2026).

> **Статус (24.07.2026, цикл виконано):** P0 ✅ · P1.1 ✅ · P1.2 ✅ · P1.3 ✅ · P1.4 ✅ · P1.5 ✅ ·
> P1.6 ✅ · P1.7/P1.8 → v2.0 (свідомо, див. нижче) · P2.1–P2.4 ✅ (P2.5 частково: перф-смок у
> тестах + BenchmarkDotNet-сюїт) · P3.1 ✅ · P3.2 ✅ (EXPAND через unsat-core + IRREDUNDANT
> через SAT) · P3.3 ✅ (PLA-style cube sharing) · P4.1 ✅ · P4.2 ✅ · P4.3 ✖ (відкладено) ·
> P4.4 ✅ · P4.5 ✅ (цикл «10 напрямків тестування») · P4.6 ✅. Деталі — позначки в пунктах.
>
> **Цикл посилення тестування (24.07.2026) ✅:** 10 систематичних напрямків — property-based
> (CsCheck), metamorphic, algebraic, differential, fuzzing, characterization (golden master),
> snapshot/approval (Verify), architecture (ArchUnitNET), combinatorial/pairwise,
> mutation (Stryker.NET). Опис — [doc/TESTING.md](doc/TESTING.md).
>
> **Цикл аудиту тестів + P3-імпрувментів (24.07.2026) ✅:** повний аудит ~700 тестів
> (5 паралельних ревізій): видалено ~180 дублів/сміття/циркулярних оракулів (зокрема
> AstAdvancedFormsTests і AdvancedLogicalFormsTests, що тестували власні копії
> продакшн-логіки), ~30 слабких тестів посилено, суїта розкладена в ієрархію
> Core/Optimizers/Engines/Analysis/Facade/Formats/Cli/Techniques (800 тестів, зелено).
> З roadmap GLOBAL_LIBRARY_COMPARISON додатково реалізовано: Plaisted–Greenbaum CNF
> (P3.5, −46% клауз), BDD Exists/ForAll/Restrict/Compose + BuildWithBestOrder (P3.3/P3.4),
> SymPy differential corpus у CI (P0.2). Матриця актуальності — TESTING.md Part 1;
> лог аудиту — Part 4.
>
> **Цикл «весь залишок» (24.07.2026) ✅:** P0.4 Z3-oracle (Microsoft.Z3 4.12.2 у тестах:
> еквівалентність оптимізатора, вердикти SAT, контрприклади, еквісатисфіабельність
> Tseitin/PG — все звіряється з Z3) · P2.1 Espresso-lite (`Transformations.MinimizeDnfHeuristic`:
> EXPAND/IRREDUNDANT/REDUCE на cube-lists з точним cofactor-tautology, працює на 40–60+
> змінних, інтегровано у DNF-шлях фасаду) · P2.3 SubcircuitLibrary (256 доведено
> мінімальних 3-вхідних функцій, локальний рерайт у фасаді) · P2.4 AndInverterGraph
> (структурний hashing, AST↔AIG з OR-відновленням, Cleanup-пас) · P3.3 sifting
> (`BuildWithSiftedOrder`, б'є статичні евристики на adversarial-порядках) ·
> P4.1 розділення пакетів: **LogicalOptimizer.Core / .Sat / .Bdd / .Minimization + фасад
> LogicalOptimizer + CLI** (шаруватість запінена `PackageLayering_IsAcyclicAndPointsDownward`) ·
> P4.4 `ApiSurfaceTests` (PublicApiGenerator, member-level бейзлайн усіх 5 збірок,
> SemVer-політика в README) · P1.x **фундамент v2.0**: `FormulaFactory` (n-ary And/Or із
> flatten, unique operands, constant/complement folding, hash-consing/інтернінг).
>
> **Що лишилось на мажорний реліз v2.0 (breaking):** внутрішнє n-ary представлення
> AndNode/OrNode, видалення `ForceParentheses` (display hint → окремий formatter),
> єдиний канонічний rewrite-traversal замість 11 IOptimizer-класів. FormulaFactory —
> готовий будівельний блок; злам API тепер захищений ApiSurfaceTests і має йти одним
> усвідомленим релізом v2.0.0.

Джерела: [UPDATED_LIBRARY_COMPARISON.md](UPDATED_LIBRARY_COMPARISON.md) (розбір розривів і критична
поправка щодо «provably minimal»), аналіз аналогів (LogicNG 3, ABC, CaDiCaL 2.0, CUDD/dd, PySAT),
попередній [LEADERSHIP_ROADMAP.md](LEADERSHIP_ROADMAP.md) (T1–T9 виконано).

Пріоритети: **P0** — чесність заявлених гарантій (блокер), **P1** — розриви проти LogicNG,
**P2** — промислова потужність солвера (розрив проти Z3), **P3** — масштабування двохрівневої
мінімізації (розрив проти PyEDA/Espresso), **P4** — екосистема й дистрибуція.

---

## P0. Фікс гарантії «provably minimal ≤10 змінних» (КРИТИЧНО) ✅ ВИКОНАНО
Реалізовано: `MinimizationStatus { MinimalProven, BudgetExceeded, Heuristic }` у результаті;
row/column dominance-редукції + independent-set lower bound у B&B; guarantee-зона отримала
окремий високий ліміт (2M кроків) — жодного тихого greedy; README переформульовано, cost model
задокументована. Верифікація: всі 254 функції n=3 через фасад — `MinimalProven`; 120 випадкових
функцій n≤8 — доведено; циклічне гексагональне ядро Σm(0,1,2,5,6,7) — доведений мінімум 6
літералів; штучно малий ліміт — чесний непідтверджений статус.

Проблема (див. [UPDATED_LIBRARY_COMPARISON.md:185](UPDATED_LIBRARY_COMPARISON.md)): у guarantee-зоні
prime-імпліканти генеруються безлімітно, але minimum-cover branch-and-bound завжди обмежений
`BranchAndBoundStepLimit = 200_000` і після перевищення мовчки повертає недоведений або greedy
результат. Коректність зберігається, доведена мінімальність — ні.

- [ ] **P0.1 Статус доказу в результаті**: `MinimizationStatus { MinimalProven, Heuristic,
      BudgetExceeded }` в `OptimizationResult` (+ метрика). Жодних тихих fallback-ів: перевищення
      бюджету завжди видиме споживачу.
- [ ] **P0.2 Посилити точний пошук покриття**, щоб у зоні ≤10 статус `BudgetExceeded` став
      практично недосяжним: класичні редукції таблиці покриття перед B&B (essential уже є; додати
      row dominance і column dominance ітеративно) + lower-bound pruning у B&B (незалежна множина
      непокритих мінтермів як нижня межа). Це стандартна зв'язка, після якої циклічні ядра n≤10
      розв'язуються точно за мілісекунди.
- [ ] **P0.3 Контракт guarantee-зони**: для ≤10 змінних greedy-completion заборонений — або
      доведений мінімум, або явний `BudgetExceeded` (після P0.2 — виняткова екзотика). Для 11–12
      залишити бюджетований режим зі статусом `Heuristic`/`BudgetExceeded`.
- [ ] **P0.4 README**: переформулювати до «Exact minimization is attempted up to 12 variables;
      optimality is reported explicitly when proven» + задокументувати cost model (два рівні:
      cover — literals → terms; фінальний multi-level вибір — literals → AST nodes; це НЕ gate
      count / depth / delay).
- [ ] **P0.5 Тести**: відомі функції з циклічним ядром (cyclic covering benchmark) → статус
      `MinimalProven`; штучно малий step limit → статус `BudgetExceeded`, не greedy мовчки;
      вичерпний прогін n=4 підтверджує `MinimalProven` на всіх 65 534 функціях.

## P1. Розриви проти LogicNG (виконано, крім v2-блоку)
Реалізовано: **P1.1** incremental SAT (`Solve(assumptions)`, збереження learnt між викликами,
`UnsatCore`, клаузи після Solve з реплеєм пропагації); **P1.2** DRAT-лог (`EnableProofLogging`,
`ToDrat`, делеції; незалежний RUP-чекер у тестах; `EquivalenceChecker.CheckWithProof` повертає
верифіковний доказ еквівалентності); **P1.3** `CardinalityEncoder` (sequential counter:
AtMost/AtLeast/ExactlyK, вичерпно верифіковані); **P1.4** `PseudoBooleanEncoder` (DP/BDD-енкодинг
зважених сум, вичерпно верифікований); **P1.5** `MaxSatSolver` (weighted partial, лінійний пошук
з PB-обмеженням, оптимум звірений із brute force); **P1.6** `FormulaAnalysis` (backbone звірений
із перебором, projected model enumeration через blocking clauses, BDD-ітератор моделей,
`SimplifyWithBackbone`). **P1.7/P1.8** — v2.0: тепер, коли бібліотека пакується на NuGet,
злам публічного API (n-ary AST, formula factory, звуження surface) належить мажорному релізу.

- [ ] **P1.1 Incremental SAT**: `Solve(assumptions)` у стилі MiniSat (assumption-рівні),
      збереження learnt-клауз між викликами, unsat core по assumptions. Розблоковує backbone за
      n інкрементальних викликів, MUS і дешеві повторні перевірки еквівалентності (зараз солвер
      будується з нуля на кожен запит).
- [ ] **P1.2 DRAT proof tracing**: лог learnt/deleted клауз, верифіковність вердиктів запобіжника
      зовнішнім drat-trim. Перетворює наш USP «доведена коректність» на зовнішньо перевірний факт.
- [ ] **P1.3 Cardinality constraints**: AtMostK/AtLeastK/ExactlyK енкодери (sequential counter +
      totalizer), публічний API + використання у власних запитах.
- [ ] **P1.4 Pseudo-Boolean constraints**: лінійні PB-обмеження через adder/BDD-енкодинг поверх
      P1.3. (Свідомий перегляд колишнього non-goal: домен configuration/optimization — найбільший
      функціональний розрив проти LogicNG/Z3 за оцінкою порівняння.)
- [ ] **P1.5 MaxSAT**: linear search (SAT-UNSAT) + core-guided (Fu–Malik/OLL-lite) поверх P1.1;
      weighted — другим кроком.
- [ ] **P1.6 Backbone + model enumeration**: `ComputeBackbone` (через P1.1 assumptions),
      `EnumerateModels`/projected enumeration (BDD-шлях безкоштовний, SAT-шлях через blocking
      clauses) + `BackboneSimplifier` як трансформація.
- [ ] **P1.7 Formula factory / канонізація AST при створенні** (v2, разом із n-ary `AndNode`/
      `OrNode` з відкладеного T8): hash-consing основного AST, `ForceParentheses` геть із
      семантичного вузла (у display-шар). Мажорний реліз — злам публічного API.
- [ ] **P1.8 Дисципліна публічного API**: реалізаційні деталі SAT/BDD/minimizer — у чіткі
      namespace/пакети, `internal` за замовчуванням, публічний контракт — мінімальний і
      задокументований (зараз surface завеликий).

## P2. Розрив проти Z3: промислова потужність солвера ✅ ВИКОНАНО (P2.1–P2.4)
Реалізовано: heap-VSIDS (індексована купа, O(log n) рішення), LBD-чистка learnt-бази (glue ≤3 і
бінарні зберігаються, reason-клаузи захищені, делеції в DRAT), Luby-рестарти, bounded
subsumption + self-subsuming resolution препроцесинг (DRAT-коректний: спершу додається посилена
клауза, потім видаляється слабша). P2.5: перф-смок на фазовому переході (60 змінних, ratio 4.2)
у Performance-категорії + BenchmarkDotNet-сюїт; SATLIB-каталог — за потреби.

- [ ] **P2.1 Heap-VSIDS** (binary heap за activity) замість O(n)-вибору змінної на кожне рішення.
- [ ] **P2.2 Чистка learnt-бази за LBD** (glue-clauses зберігати, решту періодично зрізати) —
      зараз база росте необмежено.
- [ ] **P2.3 Luby-рестарти** замість геометричних + phase saving через рестарти (є частково).
- [ ] **P2.4 Препроцесинг**: bounded variable elimination, subsumption/self-subsuming resolution
      перед пошуком (класика SatELite; CaDiCaL робить це inprocessing-ом — нам досить pre-).
- [ ] **P2.5 Бенчмарк-сюїт солвера**: SATLIB/uf125-250 + власні miter-и, регресія часу в CI
      (Performance-категорія).

## P3. Розрив проти PyEDA/Espresso: масштабування двохрівневої мінімізації ✅ ВИКОНАНО
Реалізовано: **P3.1+P3.2** `SatTwoLevelMinimizer` — прими через SAT без таблиці 2^n
(uncovered-model → unsat-core jump → greedy drops = EXPAND; blocking clauses; IRREDUNDANT через
чисто клаузальний SAT-чек), інтегрований у пайплайн для 13–24 змінних, кандидат приймається
ЛИШЕ після SAT-miter доказу еквівалентності й дає обчислюваний DNF там, де дистрибуція здається;
на випадкових функціях — у межах 20% від QM-еталона, найчастіше збігається. **P3.3**
`MultiOutputMinimizer` — PLA-style спільні куби між виходами (usable-матриця по OFF-set-ах,
greedy re-cover з перевагою вже використаних кубів, прийняття лише при виграші за
(distinct-літерали, distinct-куби)).

- [ ] **P3.1 SAT-based prime compilation** (парадигма BOOM/CoAPI): прими рахуються солвером без
      таблиці 2^n — точна/квазі-точна мінімізація для 13–20+ змінних.
- [ ] **P3.2 Espresso-стиль cube-list цикл** EXPAND → IRREDUNDANT → REDUCE над покриттям (наш
      consensus-механізм = готовий IRREDUNDANT) з budget-ами; без претензії на повний Espresso.
- [ ] **P3.3 Справжній multi-output QM**: спільні product terms між виходами (мінімізація суми
      literals усіх виходів разом), поверх наявного `--outputs=`.

## P4. Екосистема й дистрибуція ✅ ВИКОНАНО (крім P4.3/P4.5 — відкладені)
Реалізовано: **P4.1** розділення на `LogicalOptimizer` (class library, net8.0+net10.0, NuGet-
метадані, XML-доки, SourceLink) і `LogicalOptimizer.Cli` (dotnet tool `logical-optimizer`);
pack-перевірка в CI, release-workflow публікації на тег v*. **P4.2** BenchmarkDotNet-проєкт
(оптимізація по зонах, SAT фазовий перехід, QM n=10). **P4.4** публічні
`Transformations.SubsumeDnf/SubsumeCnf` + застосування в CNF/DNF-шляху понад QM-зоною.
**P4.6** зведений scale-контракт задокументовано в `ResourceBudget`. **P4.5** виконано в
циклі посилення тестування: CsCheck property-тести з автоусадкою (`PropertyBasedTests.cs`)
плюс metamorphic/algebraic/differential/fuzzing/characterization/snapshot/architecture/
pairwise-сюїти та Stryker.NET (див. [doc/TESTING.md](doc/TESTING.md)). Відкладено: **P4.3**
DocFX-сайт.

- [ ] **P4.1 NuGet-пакет**: розділити CLI (`PackAsTool`) і бібліотеку (class library), метадані,
      XML-доки, SourceLink, мультитаргет `net8.0;net10.0`, publish-workflow у CI.
- [ ] **P4.2 BenchmarkDotNet-сюїт** + опублікована порівняльна таблиця (розмір результату/час
      проти SymPy/PyEDA на спільному наборі функцій).
- [ ] **P4.3 Документаційний сайт** (DocFX): контракти операцій, статуси, budget-и, приклади.
- [ ] **P4.4 Subsumption як публічна трансформація** (утиліти вже є в `AstUtilities`) +
      застосування в CNF/DNF шляху понад QM-зоною.
- [ ] **P4.5 Property-based тести** (CsCheck) замість самописних random-циклів — автоусадка
      контрприкладів.
- [ ] **P4.6 Узгодити межі**: `TruthTable.MaxVariables=20` проти `MAX_VARIABLES=100` — звести
      контракти в `ResourceBudget`/документацію.

## Рекомендований порядок

**P0 (одразу, це репутаційний борг)** → P4.1 (пакет) → P1.2 (DRAT) → P1.1 (incremental SAT) →
P1.6 (backbone/enumeration) → P2.1–P2.4 (солвер) → P1.3–P1.5 (cardinality/PB/MaxSAT) →
P3.1–P3.3 (Espresso-клас) → P1.7–P1.8 (v2: n-ary/factory/API) → решта P4.

BDD reordering/complement edges і AIG-шар (ABC-стиль DAG-aware rewriting) — кандидати після
P1.7, коли з'явиться n-ary ядро.
