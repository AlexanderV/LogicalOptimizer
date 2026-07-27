# План v2.0: n-ary AST, єдиний rewrite-traversal, звуження API

> **Статус (27.07.2026, реалізовано):** Етапи 0.3–0.4, 1, 2, 3, 4, 5 (5.1–5.5), 6.1–6.3 — ✅.
> N-ary ядро (`NaryNode`/`AndNode`/`OrNode`), `AstFormatter`, `FormulaFactory` з
> construction-time канонізацією та hash-consing, єдиний `RewriteEngine` (Rewrite-шар замість
> 10 `IOptimizer`), n-ary Tseitin, звужений публічний API (53 типи), MIGRATION-v2.md, CHANGELOG,
> версії 2.0.0 — виконано; суїта зелена. Свідомі відхилення: `TseitinCnf`/`CnfBuilder` лишилися
> публічними (їх повертає/приймає публічний контракт фасаду й енкодерів); `AstVisualizer` лишився
> публічним (використовує CLI); кроки 0.1/0.2 (v1-бейзлайни для порівняння) пропущено — порівняння
> з v1 відкладено на пост-реліз; замість PR-послідовності — конвеєр етапів одним комітом.
> Два продакшн-фікси знайдено тестами: `SubcircuitLibrary` operand-grouping (n-ary flatten ховав
> підсхеми) і factorization growth-guard cost model (літерали→вузли, не лише вузли).
> **Залишок:** 6.4–6.5 (тег `v2.0.0` + публікація — дія користувача), Трек B (DocFX),
> Трек C (SATLIB), Трек D (пост-v2 BDD/AIG).
>
> **Первинний статус:** запланований цикл (27.07.2026). Попередній цикл — [TODO.md](TODO.md),
> виконаний повністю, крім блоку v2.0 (P1.7/P1.8), який і є предметом цього плану. Супутні треки:
> DocFX (P4.3), SATLIB (P2.5), пост-v2 BDD/AIG.
>
> **Суть релізу v2.0.0 (breaking):** внутрішнє n-ary представлення `AndNode`/`OrNode`,
> видалення `ForceParentheses` із семантичного вузла, єдиний канонічний rewrite-traversal
> замість 10 `IOptimizer`-класів, звуження публічного API (~56 → ~35 типів).

## Вихідний стан (зафіксовано аудитом коду 27.07.2026)

- **AST строго бінарний:** 7 конективів (`And/Or/Imp/Xor/Nand/Nor/Eqv`) через `BinaryNode.Left/Right`
  ([BinaryNode.cs:12-13](LogicalOptimizer.Core/BinaryNode.cs#L12-L13)). N-ary вузлів немає.
- **`ForceParentheses`** ([BinaryNode.cs:16](LogicalOptimizer.Core/BinaryNode.cs#L16)) — єдина мутабельна
  властивість AST (виняток у `ImmutableAstContract`, [ArchitectureTests.cs:196](LogicalOptimizer.Tests/Techniques/ArchitectureTests.cs#L196)).
  Походить лише з 2 місць: [PatternRecognizer.cs:65](LogicalOptimizer/PatternRecognizer.cs#L65) і
  [FactorizationOptimizer.cs:76](LogicalOptimizer/Optimizers/FactorizationOptimizer.cs#L76);
  далі вручну копіюється у ~25 місцях в 11 оптимізаторах/утилітах. Читається лише рендерерами:
  `BinaryNode.ToString`, `BooleanExpressionExporter.FormatNode` (тільки Or-гілка), `AstVisualizer`.
- **`FormulaFactory`** ([FormulaFactory.cs](LogicalOptimizer.Core/FormulaFactory.cs)) — готовий фундамент:
  n-ary API з flatten/constant folding/dedup/complement folding/інтернінгом, але результат
  right-fold-иться назад у бінарні вузли; **сортування операндів не робить**; оптимізатори й парсер
  його не використовують (усюди прямий `new`).
- **10 `IOptimizer`-класів**, кожен із власною дубльованою рекурсією; порядок застосування
  зафіксований у [ExpressionOptimizer.cs:104](LogicalOptimizer/Optimizers/ExpressionOptimizer.cs#L104);
  fixpoint-цикл із cycle detection по `ToString()` і soundness guard з rollback.
- **Зонний роутинг фасаду** ([BooleanExpressionOptimizer.cs](LogicalOptimizer/BooleanExpressionOptimizer.cs)):
  ≤12 змінних — QM (≤10 — guarantee-зона), ≤24 — SAT-мінімізатор із miter-доказом, далі — евристики
  (Espresso-lite). Ця логіка у v2 **не змінюється**.
- **Захист API:** `PublicApi.approved.txt` (5 збірок, PublicApiGenerator, регенерація через
  `LOGICALOPTIMIZER_REGENERATE_API=1`) + жорсткий список типів у `PublicSurface_IsTheDocumentedSet`.
- **Blast radius у тестах:** ~35–40 файлів (77 входжень `new AndNode/OrNode(`, 103 входження
  `.Left/.Right`, `BinaryNodeContractTests` — переписати повністю), 10 pinned-файлів
  (golden master + 8 Verify-снапшотів + API baseline).

---

## Ключові проєктні рішення (зафіксувати до початку кодування)

- [ ] **Р1. Доля extended-вузлів (`XorNode/NandNode/NorNode/EqvNode/ImpNode`).**
      Рекомендація: **лишити бінарними sealed «derived»-вузлами** поза канонічним ядром.
      `FormulaFactory.Import` уже декомпозує їх у And/Or/Not; вони потрібні тільки як *вихід*
      pattern-recognition для поля `Advanced` і для парсингу розширеного синтаксису.
      Канонічне ядро (оптимізатори, engines) працює лише з `And/Or/Not/Var/Const`.
- [ ] **Р2. Форма n-ary вузла.** Рекомендація: `NaryNode : AstNode` з
      `IReadOnlyList<AstNode> Operands` (інваріант: ≥2 операндів, жоден операнд не того ж типу —
      flatten гарантується фабрикою); `AndNode`/`OrNode` — sealed спадкоємці. `BinaryNode`
      залишається базою тільки для derived-вузлів (Xor/Nand/Nor/Eqv/Imp) — без `ForceParentheses`.
- [ ] **Р3. Канонічний порядок операндів.** Рекомендація: фабрика **сортує** операнди стабільним
      канонічним ключем (нинішня логіка `CommutativityOptimizer`). Це робить структурну рівність
      фактично комутативною (через канонізацію при побудові), дозволяє викинути
      `CommutativityOptimizer` цілком і робить інтернінг ефективним. Наслідок: усі вихідні
      рядки змінюються → повна регенерація golden/снапшотів (захищена equivalence-guard-ом).
- [ ] **Р4. Парсер.** Рекомендація: `Parser` будує вузли **через `FormulaFactory`** (канонізація
      на вході). «Сирого» дерева більше немає; хто потребує структури «як написано» — тільки
      display-шар, а він working off рядка. `Lexer/Parser/Token/TokenType` йдуть у `internal`,
      публічний вхід — `FormulaFactory.Parse` (+ статичний shortcut у фасаді).
- [ ] **Р5. Рівність.** Структурна рівність лишається порядко-чутливою (порядок уже канонічний
      після Р3). Інтернінг фабрики поверх — reference equality для канонічних дерев.
      `GetHashCode` кешується у вузлі (обчислення один раз у ктор — дерева immutable).
- [ ] **Р6. Рендеринг.** Єдина реалізація дужок: precedence-based formatter у Core
      (`AstFormatter`), яким користуються `ToString`, експортери й візуалізатор.
      `ForceParentheses` зникає; «красиві» дужки для factored-форм і XOR-патернів — рішення
      formatter-а (n-ary вже прибирає головну потребу: `a & b & c` без вкладених дужок).

---

## Етап 0. Підготовка (non-breaking, у main до зламу)

- [ ] **0.1 Гілка підтримки v1**: тег `v1-final` + гілка `release/v1.x` (можливість патчів,
      поки v2 у розробці). Задокументувати в README.
- [ ] **0.2 Порівняльний бейзлайн**: прогнати BenchmarkDotNet-сюїт і зберегти результати
      (`BenchmarkDotNet.Artifacts`) + зняти метрики якості (літерали/вузли) на golden-корпусі
      й на 1000 випадкових формул (скрипт у тестах, вивід у файл) — це еталон для порівняння v2.
- [ ] **0.3 Тестова інфраструктура — знизити blast radius заздалегідь**:
      у тест-хелпери (`RandomExpressions`, генератори CsCheck у `PropertyBasedTests`,
      differential-харнеси) ввести єдину точку побудови вузлів (хелпер/фабрика), щоб міграція
      тестів на n-ary була правкою одного місця. Прямі `new AndNode(` у тестах, де перевіряється
      не структура, а поведінка, замінити на parse рядків.
- [ ] **0.4 Витягнути formatter**: створити `AstFormatter` у Core (precedence-дужки, поки з
      підтримкою `ForceParentheses`), перевести на нього `BinaryNode.ToString`,
      `BooleanExpressionExporter.FormatNode` (усунути другу незалежну реалізацію дужок),
      `AstVisualizer`. Це non-breaking і виносить display-логіку з вузлів ще до v2.
- [ ] **0.5 Формальний план API v2**: чорновий список типів, що лишаються публічними
      (див. Етап 4), рев'ю списку до початку зламу.

## Етап 1. N-ary ядро в Core (початок гілки v2)

- [ ] **1.1 `NaryNode`**: `IReadOnlyList<AstNode> Operands`, get-only, кешований хеш,
      структурна рівність по списку; `AndNode`/`OrNode` → sealed спадкоємці з `Operator`.
      Конструктор `internal` — публічна побудова **тільки через `FormulaFactory`**
      (гарантія інваріантів flatten/≥2/canonical order).
- [ ] **1.2 `BinaryNode` (derived-вузли)**: залишити для `Xor/Nand/Nor/Eqv/Imp`, прибрати
      `ForceParentheses` і сеттер; `ImmutableAstContract` — без винятків.
- [ ] **1.3 `FormulaFactory` v2**: `Nary` повертає справжні n-ary вузли (без right-fold);
      додати канонічне сортування операндів (перенести ключ із `CommutativityOptimizer`);
      інтернінг як є; `Import` — адаптувати під n-ary. Зробити фабрику потокобезпечною або
      задокументувати scoped-використання (рішення: `ConcurrentDictionary` — дешево).
- [ ] **1.4 `Parser` через фабрику** (рішення Р4): n-ary збирання ланцюжків `a & b & c` напряму
      списком (без лівої згортки), канонізація на виході.
- [ ] **1.5 `AstFormatter` для n-ary**: `string.Join(" & ", ...)` з precedence-дужками;
      `NotNode` над n-ary — `!(...)`. Видалити читання `ForceParentheses` (0.4 вже звузив це
      до одного місця).
- [ ] **1.6 Оновити Core-утиліти**: `TruthTable.Evaluate`, `AstMetrics.CountNodes/CountLiterals`
      (визначити вартість n-ary вузла: node = 1, не n−1 — задокументувати в cost model README),
      `AstVisualizer.GetChildren`, `AndInverterGraph` (AST↔AIG: n-ary → збалансована AIG-згортка,
      бонус: менша глибина).

## Етап 2. Єдиний канонічний rewrite-traversal

- [ ] **2.1 Дизайн двигуна**: `RewriteEngine` (internal) — один bottom-up обхід;
      правила — `IRewriteRule { AstNode? TryRewrite(AstNode node, FormulaFactory f); }` —
      **локальні, без власної рекурсії**; двигун відповідає за: обхід, fixpoint, cycle detection
      (по interned-reference, а не `ToString()` — дешевше), ліміт ітерацій, метрики,
      soundness guard із rollback (перенести з `ExpressionOptimizer`), rollback-обгортку
      з node-count guard (з `AstUtilities.ApplyOptimizationRuleWithRollback`).
- [ ] **2.2 Мапа доль 10 оптимізаторів** (що вмирає у фабриці, що стає правилом):
      | Оптимізатор | Доля у v2 |
      |---|---|
      | `ConstantsOptimizer` | 💀 фабрика (constant/complement folding, `!!a→a`) |
      | `AssociativityOptimizer` | 💀 фабрика (flatten + dedup) |
      | `CommutativityOptimizer` | 💀 фабрика (канонічне сортування, Р3) |
      | `ComplementOptimizer` | 💀 фабрика (complement folding по списку операндів) |
      | `DeMorganOptimizer` | правило NNF-нормалізації (push-not-down) |
      | `AbsorptionOptimizer` | правило над `Operands`-списками (сильно спрощується) |
      | `ConsensusOptimizer` | правило з нинішнім acceptance-критерієм (виграш абсорбції) |
      | `RedundancyOptimizer` | правило(а) — ревізувати перекриття з absorption після n-ary |
      | `FactorizationOptimizer` | правило з rollback-guard (n-ary робить пошук спільних множників природним) |
      | `DistributiveOptimizer` | окремий не-pipeline експандер (expand-reduce, normal forms) — як зараз |
- [ ] **2.3 Перенести примітиви `AstUtilities`** на операнд-списки (Flatten стає тривіальним
      читанням `Operands`; absorb/consensus/resolvent — над `IReadOnlyList`), видалити
      `Rebuild`/копіювання `ForceParentheses`/`NodeComparer`-дублікати.
- [ ] **2.4 Видалити** 10 класів `IOptimizer` + `ExpressionOptimizer` → залишити тонкий
      `RewriteEngine` + набір правил. Порядок правил і expand-reduce-стратегію зберегти 1:1
      (це поведінковий контракт, звірений differential-тестами).
- [ ] **2.5 Диференціальна перевірка двигуна**: тимчасовий тест «старий pipeline (v1 гілка
      через golden-корпус і 1000 випадкових) vs новий» — еквівалентність (обов'язково) і
      не-регресія літералів (цільово; допустимі окремі відхилення з розбором).

## Етап 3. Міграція споживачів (усі проєкти)

- [ ] **3.1 Engines**: `CnfBuilder`/`TseitinConverter` (Sat), `BinaryDecisionDiagram.FromAst`
      (Bdd), `TruthTableMinimizer`/`SatTwoLevelMinimizer`/`EspressoLiteMinimizer`/
      `SubcircuitLibrary`/`MultiOutputMinimizer` (Minimization) — обхід по `Operands`;
      Tseitin для n-ary And/Or — одна клауза на вузол замість ланцюжка (бонус: менше клауз).
- [ ] **3.2 Фасадний шар**: `NormalFormConverter` (розподіл над n-ary), `PatternRecognizer`/
      `AdvancedPatternDetector` (XOR/IMP-патерни над операнд-списками; замість
      `ForceParentheses` derived-вузли форматуються `AstFormatter`-ом), `Transformations`,
      `EquivalenceChecker`, `BooleanExpressionExporter`/`CSharpExpressionExporter` (через
      `AstFormatter`/n-ary switch), `OptimizationQualityAnalyzer`, `FormulaAnalysis`.
- [ ] **3.3 `BooleanExpressionOptimizer`**: підключити `RewriteEngine`; зонний роутинг,
      статуси, budget-и — без змін.
- [ ] **3.4 CLI**: без функціональних змін; перевірити демо/бенчмарк-раннери.

## Етап 4. Звуження публічного API (P1.8)

Цільова поверхня (чорновик — фіналізувати у 0.5):

- [ ] **Core**: `AstNode`, `NaryNode`, `AndNode`, `OrNode`, `NotNode`, `VariableNode`,
      `ConstantNode`, derived-вузли (Р1), `FormulaFactory`, `TruthTable`, `AstFormatter`,
      `AstMetrics`, `OptimizationMetrics`, `ResourceBudget`.
      → `internal`: `Lexer`, `Parser`, `Token`, `TokenType`, `AndInverterGraph` (доступ через
      фасадні операції), `AstVisualizer` (або лишити — рішення при рев'ю 0.5).
- [ ] **Sat**: публічними лишаються `SatSolver` (високорівневі члени), `SatResult`,
      `MaxSatSolver`/`MaxSatResult`/`MaxSatStatus`, `CardinalityEncoder`, `PseudoBooleanEncoder`
      (це заявлені фічі P1.3–P1.5). → `internal`: `CnfBuilder`, `SatProofStep` (DRAT — через
      `EnableProofLogging`/`ToDrat` як рядок), `TseitinConverter`/`TseitinCnf` (Tseitin —
      через фасад/`Transformations`).
- [ ] **Bdd**: `BinaryDecisionDiagram` — залишити high-level (`Build*`, `AreEquivalent`,
      `CountSatisfyingAssignments`, енумерація моделей); int-handle API (`Root`, `Ite`,
      `Compose`, `Restrict`, `Exists/ForAll`, `Negate`) → `internal` або окремий
      `AdvancedBdd`-тип із явним «unstable» дисклеймером (рішення при рев'ю 0.5).
- [ ] **Minimization**: як є (4 типи); `ParseCsvToPartialTable` — типизований результат
      замість `ValueTuple`.
- [ ] **Фасад**: як є (це і є продукт), ревізія по ходу.
- [ ] Регенерувати `PublicApi.approved.txt` (`LOGICALOPTIMIZER_REGENERATE_API=1`),
      оновити список у `PublicSurface_IsTheDocumentedSet`, зняти виняток `ForceParentheses`
      у `ImmutableAstContract_NoPublicSettersOnNodes`, перевірити `AstNodes_AreSealedOrAbstract`
      під нову ієрархію.

## Етап 5. Тестовий цикл v2

- [ ] **5.1 Переписати** `BinaryNodeContractTests` → `NaryNodeContractTests` (інваріанти:
      flatten, ≥2 операндів, канонічний порядок, кешований хеш, рівність, immutability).
- [ ] **5.2 Оновити конструкцію** в ~11 файлах (77 `new AndNode/OrNode(`) — через хелпер з 0.3;
      **навігацію** в ~13 файлах (103 `.Left/.Right`) — на `Operands`/parse-рядки.
- [ ] **5.3 Регенерувати pinned-файли**: golden master (`LOGICALOPTIMIZER_REGENERATE_GOLDEN=1`;
      guard `GoldenCorpus_EveryPinnedResultIsStillEquivalent` захищає від закріплення хибного
      результату), 8 Verify-снапшотів — з ручним рев'ю дифів (це основне рев'ю рендерингу v2).
- [ ] **5.4 Спецсюїти**: CsCheck-генератори на фабрику; metamorphic/fuzzing — порівняння через
      еквівалентність, не рядки, де можливо; differential (SymPy, Z3, cross-engine) — мають
      пройти без змін еталонів (вони порівнюють семантику); characterization літерал-каунтів —
      звірити не-регресію якості проти бейзлайну 0.2.
- [ ] **5.5 Нові v2-тести**: property «фабрика ⇒ канонічність» (ідемпотентність
      `Import(Import(x)) == Import(x)`, reference-equality інтернінгу, сортованість операндів);
      «жоден шлях коду не будує неканонічний вузол» (архітектурний: конструктори internal);
      Tseitin n-ary — менше-або-стільки-ж клауз.
- [ ] **5.6 Stryker** на новий `RewriteEngine` + правила (scoped-конфіг, як у пам'ятці стека).
- [ ] **5.7 Перф-звірка**: BenchmarkDotNet проти бейзлайну 0.2; бюджет: без регресії >10%
      на зонах ≤12/≤24; очікуване покращення на глибоких ланцюжках (flatten + інтернінг).

## Етап 6. Реліз v2.0.0

- [ ] **6.1 Міграційний гайд** `MIGRATION-v2.md`: таблиця «було → стало» (ктор-и вузлів →
      `FormulaFactory`, `.Left/.Right` → `Operands`, `ForceParentheses` → `AstFormatter`,
      `Parser` → `FormulaFactory.Parse`, зниклі типи → заміни).
- [ ] **6.2 README**: секції про AST/оператори (формулювання «3 core + derived»), versioning
      policy (перший мажорний break — приклад роботи політики), cost model для n-ary.
- [ ] **6.3 CHANGELOG** + `<Version>2.0.0</Version>` у всіх csproj (узгоджено з тегом).
- [ ] **6.4 Реліз**: тег `v2.0.0` → release-workflow публікує 6 пакетів; перевірити
      `--skip-duplicate` і SourceLink.
- [ ] **6.5 Пост-реліз смок**: встановити пакети з NuGet у чистий проєкт, прогнати приклади
      з README.

---

## Трек B. DocFX-сайт (P4.3) — можна паралельно з Етапами 0–1

- [ ] B.1 `docfx.json` + API-доки з XML-коментарів (5 збірок).
- [ ] B.2 Статті: контракти операцій, `MinimizationStatus`/`ComputationStatus`, budget-и
      (`ResourceBudget`), зонна модель, приклади CLI і бібліотеки, TESTING-огляд.
- [ ] B.3 GitHub Pages workflow (deploy на push у main); лінк у README і NuGet-метадані.
- [ ] B.4 Після v2 — розділ Migration guide на сайті.

## Трек C. SATLIB-каталог (P2.5, залишок) — низький пріоритет

- [ ] C.1 Скрипт завантаження uf125-538/uuf125-538 (або vendored підмножина ~20 інстансів).
- [ ] C.2 Performance-тести: розв'язання підмножини з таймаутом, регресія часу.
- [ ] C.3 Nightly/manual CI job (не в PR-гейті).

## Трек D. Після v2.0 (кандидати v2.1+)

- [ ] D.1 BDD complement edges (потребує стабільного ядра, виграш ~2× пам'яті).
- [ ] D.2 AIG-шар: DAG-aware rewriting у стилі ABC (n-ary ядро + `SubcircuitLibrary` як
      база рерайтів; збалансована AIG-згортка з 1.6 — перший крок).
- [ ] D.3 Ревізія `RedundancyOptimizer`-правил: після n-ary частина його випадків має
      покриватись absorption/consensus — виміряти й спростити.

## Ризики та мітигації

| Ризик | Мітигація |
|---|---|
| Канонічне сортування змінить усі вихідні рядки — «великий регенераційний вибух» | Робити регенерацію одним PR-ом з equivalence-guard-ами (golden guard + differential); ручне рев'ю снапшот-дифів |
| Регресія якості мінімізації (інший порядок правил/канонізація змінює траєкторію fixpoint) | Диференціальний тест 2.5 (v1 vs v2 по літералах) на корпусі до злиття; відхилення розбираються поіменно |
| Перф-регресія від інтернінгу/алокацій списків | Бейзлайн 0.2 + бенч-гейт 5.7; кешований хеш; `ConcurrentDictionary` тільки якщо потрібна потокобезпека |
| Blast radius тестів більший за оцінку | Етап 0.3 (єдина точка конструкції) виконується ДО зламу; міграція тестів механічна |
| Довга гілка v2 розходиться з main | Етапи 0.x і Трек B йдуть у main; злам — одна гілка `v2`, PR-и в неї дрібні, main заморожується на фічі до злиття |
| Приховання `Parser`/BDD int-API зламає невідомих споживачів | Це і є суть мажорного релізу; migration guide 6.1 + `v1-final` гілка підтримки |

## Порядок PR-ів (орієнтовно)

1. Етап 0.4 (AstFormatter, non-breaking) → main
2. Етап 0.1–0.3, 0.5 (бейзлайни, тест-хелпери, API-чорновик) → main
3. Гілка `v2`: Етап 1 (ядро + фабрика + парсер) — компілюється Core + тести Core
4. Етап 2 (RewriteEngine + правила, видалення IOptimizer) + 2.5 (диференціал)
5. Етап 3 (engines + фасад + CLI)
6. Етап 4 (API-звуження + baselines)
7. Етап 5 (регенерації + нові тести + Stryker + перф)
8. Етап 6 (доки + реліз) → злиття в main, тег v2.0.0
9. Трек B (DocFX) — паралельно; Трек C — після релізу; Трек D — v2.1+
