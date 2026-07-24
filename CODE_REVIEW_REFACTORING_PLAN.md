# Код-рев’ю та план рефакторингу LogicalOptimizer

## Поточний стан

- Release-збірка успішна.
- Усі 816 тестів проходять.
- Покриття: 85,13% рядків і 81,69% гілок.
- Відомих уразливостей NuGet не виявлено.
- `dotnet format --verify-no-changes` знаходить проблеми форматування у 40 із 91 файлів.

Основний потенціал покращення — не в додаванні нових правил, а в спрощенні моделі AST, конвеєра оптимізації та обчисленні результатів лише на вимогу.

## Ранжовані зауваження

### P0 — виправити перед подальшим розширенням

#### 1. CSV дозволяє некоректні та дубльовані імена змінних

`CsvTruthTableParser.cs:29` трактує будь-який заголовок як ім’я змінної без перевірки граматики та унікальності. Заголовки на кшталт `a | 1`, `!a` або `a,a,Result` можуть створити неправильний вираз чи порушити відповідність між кількістю змінних та комбінаціями.

Додатково `CsvTruthTableParser.cs:156` використовує `1 << variables.Count`, що переповнюється після 30 змінних.

Рішення:

- перевіряти ім’я тим самим правилом, що й lexer: `[Letter_][LetterOrDigit_]*`;
- заборонити `0`, `1`, дублікати і декілька result-колонок;
- встановити явний ліміт кількості CSV-змінних до генерації DNF;
- не визначати повноту таблиці через небезпечний бітовий зсув.

#### 2. `TruthTable` дозволяє створювати внутрішньо некоректний об’єкт

Конструктор `TruthTable.cs:13` не перевіряє:

- унікальність змінних;
- відповідність `results.Count == 2^variables.Count`;
- наявність рівно одного результату для константи.

Колекції публікуються як змінювані `List`. Після створення клієнт може змінити `Variables` або `Results`, тоді як `Rows` залишиться старим.

Рішення: immutable value object із `IReadOnlyList`, defensive copy та повною перевіркою інваріантів.

### P1 — найбільша віддача від рефакторингу

#### 3. API завжди виконує дорогі CNF, DNF та pattern recognition

`BooleanExpressionOptimizer.cs:26` завжди рахує обидві нормальні форми, навіть для `--truth-table`, `--advanced`, звичайної оптимізації або коли потрібен лише CNF.

Рішення: ввести `OptimizationRequest` або flags:

```text
Optimize | CNF | DNF | Advanced | TruthTable | Metrics
```

Кожен артефакт обчислювати ліниво або тільки на запит.

#### 4. Помилки нормалізації маскуються значенням `"-"`

У `BooleanExpressionOptimizer.cs:29` будь-який `InvalidOperationException` перетворюється на `"-"`. Це може приховати не лише перевищення ліміту, а й дефект алгоритму.

Рішення:

- створити `NormalFormExpansionLimitException`;
- повертати типізований статус `Computed / TooLarge / NotRequested`;
- не використовувати магічний рядок `"-"` у доменній моделі.

#### 5. AST одночасно є семантичною моделлю та моделлю форматування

`ForceParentheses` у `BinaryNode.cs:15` — mutable display hint усередині логічного AST. Оптимізатори змушені вручну переносити його, хоча `Equals` і `GetHashCode` його ігнорують.

Рішення:

- зробити AST immutable;
- прибрати `ForceParentheses`;
- визначати дужки винятково formatter-ом через precedence та associativity.

#### 6. Конвеєр оптимізаторів жорстко закодований і дублює обходи дерева

`ExpressionOptimizer.cs:15` містить дев’ять конкретних optimizer-ів та ручний порядок викликів. Майже кожен optimizer окремо рекурсивно обходить усе дерево, flatten-ить і знову збирає його.

Рішення:

- описати правила як упорядковану колекцію `IRewriteRule`;
- використовувати один bottom-up rewrite pass;
- після локальної зміни повторювати лише відповідну гілку;
- окремо виконувати канонізацію associative/commutative операторів;
- ін’єктувати pipeline через конструктор.

#### 7. Бінарна форма AST ускладнює AND/OR алгоритми

AND і OR фактично є асоціативними n-ary операціями, але представлені бінарним деревом. Через це код постійно виконує `FlattenAnd`, `FlattenOr` та `Aggregate`.

Рішення: перейти на:

```text
AndNode(ImmutableArray<AstNode>)
OrNode(ImmutableArray<AstNode>)
```

Це дозволить використовувати стандартні операції:

- `Distinct` — ідемпотентність;
- сортування — канонізація;
- `HashSet` — complement та absorption;
- `Intersect` — пошук спільного множника;
- `Except` — залишкові терми.

Після цього окремі `AssociativityOptimizer` і значна частина `CommutativityOptimizer` стануть непотрібними.

#### 8. Ліміт оптимізації не сигналізує про відсутність збіжності

Цикл у `ExpressionOptimizer.cs:41` після 20 ітерацій просто повертає останній стан. Метод `ValidateIterations` існує, але не використовується.

Рішення:

- відстежувати canonical hash кожного стану;
- зупинятися при повторенні стану;
- повертати `Converged`, `CycleDetected` або `IterationLimit`;
- не рахувати rollback як успішно застосоване правило.

#### 9. `NormalFormConverter` має mutable стан і не є thread-safe

Поле `_distributionCalls` у `NormalFormConverter.cs:14` скидається на початку кожного виклику. Паралельне використання одного instance робить ліміт недетермінованим.

Рішення: локальний `ExpansionBudget`, що передається рекурсивним методам; converter має бути stateless.

### P2 — структурне спрощення

#### 10. Константи представлені рядковими змінними `"0"` та `"1"`

Через це кожен алгоритм має пам’ятати, що деякі `VariableNode` не є змінними.

Рішення: окремий `ConstantNode(bool Value)` або singleton-вузли `True` і `False`.

#### 11. Семантична рівність змішана зі структурною

`AstUtilities.AreEqual` лише викликає `Equals`, хоча назва натякає на семантичне порівняння. Наприклад, `a & b` і `b & a` не рівні до окремого сортування.

Рішення:

- перейменувати поточну операцію на `StructuralEquals`;
- після n-ary канонізації використовувати structural equality для AC-операторів;
- логічну еквівалентність залишити SAT/truth-table перевірці.

#### 12. Занадто широка публічна поверхня

У production-проєкті приблизно 225 публічних типів і методів. Публічними є конкретні оптимізатори, parser, lexer, mutable DTO та допоміжні алгоритми.

Рішення: залишити публічними лише стабільний facade, request/result, необхідну частину AST та exporter contracts. Решту зробити `internal`, додавши `InternalsVisibleTo` тестовому проєкту.

#### 13. Документація конфліктує з кодом

`.github/copilot-instructions.md` усе ще говорить про .NET 8, старий порядок оптимізаторів і видалений `--test`; фактичний target — .NET 10. README містить placeholder URL репозиторію.

#### 14. Форматування не контролюється CI

CI перевіряє build і tests, але не виконує `dotnet format --verify-no-changes`.

Рішення: додати `.editorconfig` і окремий format-check. Форматування варто винести в окремий коміт.

## Оптимальний план рефакторингу

### 1. Зафіксувати поведінку

- Додати property-based тести: випадково згенерований AST → optimize → перевірка еквівалентності.
- Додати CSV boundary tests: дублікати headers, невалідні identifiers, 31+ змінних, неповні таблиці.
- Додати тести iteration-limit, normal-form budget і паралельного converter.
- Не збільшувати кількість прикладних unit-тестів без потреби: потрібні саме інваріантні тести.

### 2. Відокремити запит від результату

- `OptimizationRequest` із потрібними артефактами.
- `OptimizationResult` як immutable record.
- Типізований `ComputationResult<T>` замість `"-"`.
- Прибрати `Console.WriteLine` із бібліотечного facade; debug output повертати як дані або передавати через logger.

### 3. Перебудувати AST

- Додати `ConstantNode`.
- Зробити вузли immutable.
- Перевести `AndNode` і `OrNode` на n-ary модель.
- Прибрати непотрібний для immutable дерева `Clone()`.
- Прибрати `ForceParentheses`; форматування зробити окремим visitor/formatter.

### 4. Зробити єдину канонізацію

Для `And` і `Or` в одному місці:

- рекурсивно об’єднати вкладені однойменні вузли;
- прибрати neutral constants;
- обробити absorbing constants;
- застосувати `Distinct`;
- знайти `x`/`!x` через `HashSet`;
- стабільно відсортувати;
- побудувати канонічний вузол.

Це дозволить видалити більшу частину `AssociativityOptimizer`, `CommutativityOptimizer`, `ConstantsOptimizer` і частину `ComplementOptimizer`.

### 5. Перевести оптимізацію на rewrite pipeline

- Один bottom-up traversal.
- Невеликі pure rules без власного рекурсивного обходу.
- Явна cost function: кількість вузлів, літералів і depth.
- Приймати rewrite лише при покращенні cost або для спеціально позначеного normalization rule.
- Cycle detection через structural hash.

### 6. Спрощувати булеві терми стандартними множинами

- factorization — перетин множин факторів;
- absorption — subset check;
- consensus — symmetric difference/complement pair;
- deduplication — `HashSet<AstNode>`;
- не використовувати вкладені `O(n²)` цикли там, де достатньо set lookup.

### 7. Ізолювати експоненційні операції

- CNF, DNF і truth table — окремі сервіси з явним budget/cancellation.
- Перед розподілом оцінювати верхню межу розміру.
- Не запускати їх автоматично.
- Для еквівалентності понад малий поріг у майбутньому використовувати SAT/BDD, а не таблицю істинності.

### 8. Звузити API та очистити інфраструктуру

- Позначити implementation types як `internal`.
- Оновити README та copilot instructions.
- Додати `.editorconfig`, format-check і coverage threshold.
- Залишити форматування окремим комітом, щоб не змішувати його з логікою.

## Рекомендований порядок реалізації

1. CSV та `TruthTable` invariants.
2. Обчислення результатів на вимогу і типізовані помилки.
3. Immutable AST та `ConstantNode`.
4. N-ary AND/OR і canonicalizer.
5. Єдиний rewrite pipeline.
6. Оптимізація CNF/DNF budgets.
7. Звуження API, документація та форматування.

Перші два етапи дадуть швидкий практичний виграш без масштабної перебудови. Етапи 3–5 найбільше скоротять код і усунуть дублювання; їх варто виконувати одним узгодженим архітектурним циклом, а не локальними правками окремих оптимізаторів.

## Глобальне порівняння з аналогами

Актуальність зовнішніх даних: 24 липня 2026 року.

### Межі порівняння

Пошук охопив:

- нативні .NET/C# бібліотеки;
- загальні SAT/SMT-рішення з .NET API;
- BDD-бібліотеки;
- зрілі Java/Python фреймворки для символьної булевої алгебри;
- точні та евристичні алгоритми двохрівневої мінімізації.

За результатами пошуку не знайдено зрілого нативного .NET-пакета, який повністю дублює комбінацію можливостей LogicalOptimizer: власний текстовий parser, покрокові алгебраїчні rewrites, CNF/DNF, truth tables, розпізнавання XOR/IMP/EQV і набір форматів експорту. Найближчі .NET-рішення спеціалізуються або на SMT/SAT, або на BDD, а не на пояснюваному перетворенні текстових виразів.

Це висновок із дослідженого набору пакетів, а не твердження про абсолютну відсутність будь-якого маловідомого аналога.

### Основні аналоги

| Рішення | Платформа | Основний підхід | Сильні сторони | Обмеження відносно LogicalOptimizer |
|---|---|---|---|---|
| LogicalOptimizer | .NET 10 / C# | Власний AST і набір rewrite-правил | Простий CLI, зрозумілий результат, CNF/DNF, truth table, CSV, експортери, XOR/IMP/EQV | Немає глобальної мінімальності, SAT/BDD, don’t-care; бінарний AST і повторні обходи |
| LogicNG | Java | Канонічна formula factory, DAG, transformations, SAT, BDD/DNNF | Найповніша архітектурна модель; n-ary AST; кілька CNF-стратегій; satisfiability та equivalence | Не .NET; складніший API; не орієнтований на такий компактний CLI |
| SymPy Logic | Python | Symbolic algebra, SOP/POS algorithms | Точні SOP/POS, don’t-care, ANF, багата символьна екосистема | Python; експоненційна точна мінімізація за замовчуванням обмежена 8 змінними |
| PyEDA | Python + C | Espresso, expressions, truth tables, BDD | Сильна евристична двохрівнева мінімізація; множинні вихідні функції; EDA-підхід | Python/C dependency; не фокусується на пояснюваних локальних законах |
| Microsoft Z3 | Native + .NET binding | SMT/SAT solver і simplifier | Масштабована перевірка satisfiability/equivalence, зрілий .NET API, активний розвиток | Велика native dependency; `Simplify()` не гарантує мінімальний читабельний SOP/POS; немає готового CLI домену |
| DecisionDiagrams | Native .NET | BDD/CBDD, hash-consing, complement edges | Канонічна еквівалентність, швидкі булеві операції, компактне представлення багатьох функцій | Не parser/simplifier для користувацького тексту; результат BDD не обов’язково є найчитабельнішим виразом |
| AngouriMath/xFunc | .NET | Загальна символьна математика | Нативна .NET інтеграція і ширший математичний домен | Булева мінімізація не є основним спеціалізованим сценарієм |

### Порівняння за можливостями

Легенда: `++` — сильна/спеціалізована підтримка, `+` — підтримується, `±` — частково або через додатковий шар, `−` — не є штатною можливістю.

| Можливість | LogicalOptimizer | LogicNG | SymPy | PyEDA | Z3 .NET | DecisionDiagrams |
|---|---:|---:|---:|---:|---:|---:|
| Нативний .NET API | ++ | − | − | − | ++ | ++ |
| Parser простих булевих виразів | ++ | ++ | + | + | ± | − |
| Локальні алгебраїчні rewrites | ++ | ++ | ++ | + | + | − |
| Точна SOP/POS мінімізація | − | + | ++ | ± | − | − |
| Евристична Espresso-мінімізація | − | − | − | ++ | − | − |
| CNF/DNF | ++ | ++ | ++ | ++ | ± | ± |
| CNF без експоненційного distribution | − | ++ | ± | ± | ++ | ± |
| Truth tables | ++ | + | + | ++ | − | − |
| Don’t-care умови | − | + | ++ | ++ | ± | ± |
| SAT/SMT | − | ++ | + | + | ++ | − |
| BDD | − | ++ | − | ++ | ± | ++ |
| Перевірка еквівалентності без `2^n` таблиці | − | ++ | + | ++ | ++ | ++ |
| Псевдобулеві/cardinality constraints | − | ++ | − | − | ++ | ± |
| DIMACS/BLIF/Verilog/LaTeX/CSV | ++ | ± | ± | ± | SMT-LIB | − |
| Пояснюваний компактний CLI | ++ | − | − | − | − | − |

### Що аналоги роблять архітектурно краще

#### 1. LogicNG: інваріанти під час створення формули

LogicNG використовує `FormulaFactory`. Вона:

- прибирає нейтральні константи;
- flatten-ить вкладені AND/OR;
- усуває дублікати;
- спрощує complementary operands;
- створює n-ary AND/OR;
- зберігає формули як DAG, а не як дубльовані дерева.

Це сильніше за поточну модель LogicalOptimizer, де некоректна або неканонічна форма спочатку створюється, а потім багаторазово виправляється окремими optimizer-ами.

Практичний висновок: запропонований `AstFactory`/canonicalizer повинен забезпечувати інваріанти одразу при побудові вузлів.

#### 2. LogicNG: декілька стратегій CNF

Поточний LogicalOptimizer використовує дистрибутивне розкриття з лімітом. LogicNG розділяє:

- факторизацію через distributive laws;
- еквівалентне перетворення через BDD;
- equisatisfiable Tseitin transformation;
- Plaisted–Greenbaum transformation.

Для LogicalOptimizer доцільно запропонувати режими:

```text
EquivalentSmall
EquivalentViaBdd
EquisatisfiableTseitin
```

Це усуне ситуацію, коли CNF або експоненційно розростається, або повертається як `"-"`.

#### 3. SymPy: мінімізація як окрема операція з чітким контрактом

SymPy відділяє звичайні symbolic simplifications від `simplify_logic`, `SOPform` і `POSform`. Точна мінімізація має:

- явний вибір CNF/DNF;
- `don’t-care`;
- захисний поріг у 8 змінних;
- `force=True` як усвідомлене зняття обмеження.

LogicalOptimizer також повинен розділити:

- дешеву канонізацію;
- локальне спрощення;
- точну мінімізацію;
- нормалізацію CNF/DNF.

Називати поточний локальний rewrite pipeline «maximum simplification» некоректно: він не доводить глобальну мінімальність.

#### 4. PyEDA: Espresso для практичної мінімізації

PyEDA використовує C-реалізацію Berkeley Espresso, оскільки двохрівнева логічна мінімізація є NP-complete. Espresso не гарантує математично найменшу форму, але є промислово відомою евристикою для SOP/POS і підтримує одночасну мінімізацію кількох функцій.

Для LogicalOptimizer є два реалістичні варіанти:

1. Реалізувати Quine–McCluskey/Petrick лише для малого числа змінних.
2. Додати опційний Espresso backend для більших truth-table/PLA сценаріїв.

Писати власний «майже Espresso» набір евристик недоцільно.

#### 5. Z3: перевірка коректності замість truth table

Z3 має офіційний .NET API, SAT/SMT backend і `BoolExpr.Simplify()`. Станом на лютий 2026 року актуальний upstream release — 4.16.0; репозиторій активно підтримується.

Найкраще застосування Z3 у LogicalOptimizer:

- перевірка еквівалентності через `Xor(original, optimized)` і unsatisfiability;
- property-based/oracle tests;
- перевірка великих формул, де truth table неможлива;
- отримання counterexample assignment при помилковому rewrite.

Z3 не варто робити обов’язковим ядром простого CLI: native package значно важчий, а його simplifier не оптимізований під найчитабельніший булевий вираз.

#### 6. DecisionDiagrams: канонічне представлення функції

DecisionDiagrams — нативна .NET Standard BDD/CBDD бібліотека. Вона використовує:

- hash-consing;
- unique table;
- complement edges;
- garbage collection вузлів;
- упорядкування аргументів комутативних операцій.

BDD дає дешеву перевірку еквівалентності після побудови: однакові функції мають однаковий canonical node. Це значно масштабованіше за повну truth table для сприятливого порядку змінних.

Водночас BDD може експоненційно рости при невдалому порядку змінних, а бібліотека не підтримує dynamic variable reordering. Тому backend повинен мати budget і fallback.

### Де LogicalOptimizer уже виграє

1. **Нативність і простота .NET.** Немає native runtime dependency чи міжмовного bridge.
2. **Придатність для навчання та пояснення.** Результат формується через зрозумілі закони булевої алгебри.
3. **CLI та інтеграційні формати.** CNF/DNF, truth table, CSV, DIMACS, BLIF, Verilog, mathematical notation і LaTeX зібрані в одному інструменті.
4. **Розпізнавання представлень.** XOR, implication та equivalence відновлюються у читабельну форму.
5. **Тестова база.** 816 тестів і понад 80% branch coverage є сильним фундаментом для невеликого спеціалізованого проєкту.
6. **Мала вага.** Production-проєкт не має сторонніх package dependencies.

### Де LogicalOptimizer програє

1. **Немає формального поняття оптимальності.** Cost function і гарантія global minimum відсутні.
2. **Немає точного minimizer backend.** Немає Quine–McCluskey/Petrick для малих задач.
3. **Немає практичного EDA backend.** Немає Espresso та don’t-care.
4. **Еквівалентність масштабується як `2^n`.** Немає SAT або BDD backend.
5. **CNF має лише наївну еквівалентну стратегію.** Немає Tseitin/Plaisted–Greenbaum.
6. **AST неканонічний.** Немає n-ary nodes, formula factory, hash-consing або DAG.
7. **Обчислюються непотрібні артефакти.** CNF і DNF запускаються незалежно від режиму CLI.
8. **Немає don’t-care та multi-output minimization.**
9. **Немає cancellation і детального resource budget.**
10. **Позиціонування README завищене.** Формулювання «maximum simplification» не підтверджене алгоритмом.

## Оновлений стратегічний пріоритет

### P0 — коректне позиціонування та контракти

1. Замінити обіцянку «maximum simplification» на «rule-based symbolic simplification».
2. Розділити `Simplify`, `Minimize`, `ToEquivalentCnf`, `ToEquisatisfiableCnf`.
3. Додати типізовані статуси `NotRequested`, `Completed`, `BudgetExceeded`.

### P1 — архітектура рівня LogicNG

1. Immutable n-ary AST.
2. Окремі constant і literal nodes.
3. `AstFactory` з flatten/deduplicate/complement invariants.
4. Structural hashing і, за потреби, hash-consing.
5. Один rewrite traversal замість ланцюга повних обходів.

### P2 — алгоритмічна конкурентоспроможність

1. Exact minimizer для малих формул:
   - Quine–McCluskey;
   - Petrick’s method;
   - don’t-care;
   - жорсткий variable/implicant budget.
2. Tseitin CNF як стандартний масштабований режим для SAT/export.
3. Опційний SAT oracle:
   - Z3 adapter або легший managed SAT backend;
   - equivalence і counterexamples.
4. Опційний BDD backend для repeated queries, model counting та canonical equivalence.
5. Espresso integration тільки якщо продукт орієнтуватиметься на EDA/truth-table minimization.

### P3 — продуктова диференціація

Не намагатися стати ще одним Z3 або SymPy. Найсильніша ніша:

> Легка нативна .NET-бібліотека та CLI для пояснюваного спрощення булевих виразів, із точним малим minimizer-ом і масштабованою SAT-перевіркою.

Оптимальна комбінація:

```text
Readable rewrite engine
        +
Exact minimizer for small formulas
        +
Tseitin CNF for scalable export
        +
Optional SAT/BDD verification backend
```

## Рекомендований технічний вибір

### Мінімальний варіант без зовнішніх production dependencies

- власний immutable canonical AST;
- власний локальний rewrite engine;
- Quine–McCluskey/Petrick до 8–10 змінних із budget;
- власна Tseitin transformation;
- truth tables лише для малих задач;
- Z3 тільки у тестах як oracle.

Це найкраще зберігає поточні переваги бібліотеки.

### Варіант для production verification

- те саме lightweight core;
- окремий package `LogicalOptimizer.Z3`;
- equivalence, implication, satisfiability і counterexample через Z3;
- core package не залежить від native runtime.

### Варіант для configuration/model-counting

- окремий package `LogicalOptimizer.Bdd`;
- adapter до `DecisionDiagrams`;
- canonical equivalence, existential quantification і model counting;
- явний memory/node budget.

## Джерела

- [LogicNG: formula hierarchy, DAG та операції](https://logicng.org/documentation/formulas/)
- [LogicNG: Formula Factory та її інваріанти](https://logicng.org/documentation/formula-factory/)
- [LogicNG: CNF/DNF, BDD, Tseitin і Plaisted–Greenbaum transformations](https://logicng.org/documentation/formulas/operations/transformations/normal-form-transformations/)
- [LogicNG: knowledge compilation, BDD і DNNF](https://logicng.org/documentation/knowledge-compilation/)
- [LogicNG: ліцензія та загальний огляд](https://logicng.org/)
- [SymPy Logic: `simplify_logic`, SOP/POS, ANF та don’t-care](https://docs.sympy.org/latest/modules/logic.html)
- [PyEDA: Espresso two-level minimization](https://pyeda.readthedocs.io/en/latest/2llm.html)
- [PyEDA: загальний огляд](https://pyeda.readthedocs.io/en/latest/)
- [Z3: офіційний репозиторій і .NET binding](https://github.com/Z3Prover/z3)
- [Z3 .NET: `BoolExpr.Simplify`](https://z3prover.github.io/api/html/class_microsoft_1_1_z3_1_1_bool_expr.html)
- [Z3 Guide: перелік simplifier-ів](https://microsoft.github.io/z3guide/docs/strategies/simplifiers-summary/)
- [DecisionDiagrams: NuGet, архітектура та benchmark](https://www.nuget.org/packages/DecisionDiagrams)
- [Microsoft Zen: .NET constraint solving із Z3 та BDD backends](https://github.com/microsoft/Zen)

> **Роадмап реалізації лідерства за цим порівнянням: [LEADERSHIP_ROADMAP.md](LEADERSHIP_ROADMAP.md)** (містить поправку матриці на поточний стан після Фаз 0–3).
