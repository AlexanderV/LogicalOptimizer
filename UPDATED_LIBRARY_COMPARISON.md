# Повторне порівняння LogicalOptimizer з аналогами

Дата перевірки: 24 липня 2026 року.

Цей документ оцінює оновлену реалізацію бібліотеки та замінює попередню порівняльну оцінку в `CODE_REVIEW_REFACTORING_PLAN.md`.

## Перевірений стан

- Target: .NET 10.
- Production-залежності: відсутні.
- Тести: 913 пройдено, 0 помилок, 0 пропущено.
- Release test run: приблизно 38 секунд у поточному середовищі.
- Реалізовано:
  - rule-based symbolic simplification;
  - exact SOP/POS minimizer на базі Quine–McCluskey;
  - don’t-care minterms;
  - multi-output CSV;
  - equivalent CNF/DNF;
  - лінійний equisatisfiable Tseitin CNF;
  - власний CDCL SAT solver;
  - SAT-based equivalence із counterexample;
  - ROBDD із hash-consing та model counting;
  - resource budgets і cancellation;
  - DIMACS, BLIF, Verilog, CSV, mathematical та LaTeX export.

## Що змінилося від попереднього порівняння

Попередні ключові відставання частково або повністю закриті:

| Попереднє відставання | Новий стан |
|---|---|
| Немає точного minimizer-а | Додано Quine–McCluskey і пошук покриття |
| Немає don’t-care | Додано для partial CSV та minimizer API |
| Немає multi-output minimization | Додано multi-output CSV |
| Еквівалентність лише через `2^n` truth table | Додано SAT miter і ROBDD |
| Немає масштабованої CNF | Додано Tseitin CNF |
| Немає counterexample | SAT checker повертає assignment |
| Немає resource control | Додано budgets і cancellation |
| Константи маскуються під змінні | Додано `ConstantNode` |
| Усе обчислюється безумовно | Додано `OptimizationOptions` і статуси артефактів |

Оновлена бібліотека вже не є лише навчальним rewrite engine. За набором булевих можливостей вона стала компактним self-contained logic toolkit для .NET.

## Актуальні аналоги

### LogicNG

Java-фреймворк для formulas, transformations, SAT/MaxSAT, BDD/DNNF, cardinality та pseudo-Boolean constraints.

Його архітектурні переваги:

- immutable formula model;
- formula factory з hash-consing;
- канонізація associativity/commutativity під час створення;
- n-ary AND/OR;
- MiniSat, Glucose і MiniCARD;
- incremental solving;
- proof tracing, unsat cores;
- різні CNF encodings;
- cardinality та pseudo-Boolean constraints.

### SymPy Logic

Python symbolic logic із `simplify_logic`, SOP/POS, ANF та don’t-care.

`simplify_logic` за замовчуванням не виконує експоненційну мінімізацію понад 8 змінних без `force=True`. Це близьке до нового порогового підходу LogicalOptimizer, але SymPy має значно ширшу математичну екосистему.

### PyEDA

Python EDA toolkit із expressions, truth tables, ROBDD, formal equivalence, PicoSAT та C-extension Berkeley Espresso.

PyEDA залишається сильнішим для:

- великих двохрівневих PLA/SOP задач;
- евристичної Espresso-мінімізації;
- багато-вихідної мінімізації зі спільним використанням implicant-ів;
- EDA workflow.

Документація PyEDA демонструє PLA з 50 входами і 5 виходами, що є іншим масштабом, ніж exact Quine–McCluskey.

### Microsoft Z3

Зрілий SMT solver із офіційним .NET API. Актуальний upstream release — 4.16.0 від 19 лютого 2026 року.

Z3 сильніший у:

- theory reasoning, а не лише propositional Boolean logic;
- industrial SMT solving;
- incremental contexts та assumptions;
- proofs/cores/tactics;
- довготривалій перевірці великих constraints.

LogicalOptimizer виграє у вазі, читабельній мінімізації та відсутності native dependency.

### DecisionDiagrams

Нативна .NET Standard BDD/CBDD бібліотека з complement edges, hash-consing, memory pool і власним GC.

Вона має більш оптимізовану та спеціалізовану BDD-реалізацію. LogicalOptimizer натомість інтегрує BDD безпосередньо з parser, AST, minimizer, SAT і exporters.

## Повторна матриця можливостей

Легенда:

- `++` — сильна спеціалізована підтримка;
- `+` — штатна підтримка;
- `±` — частково, з обмеженнями або через додатковий шар;
- `−` — відсутня.

| Можливість | LogicalOptimizer | LogicNG | SymPy | PyEDA | Z3 .NET | DecisionDiagrams |
|---|---:|---:|---:|---:|---:|---:|
| Нативний managed .NET core | ++ | − | − | − | ± | ++ |
| Відсутність production dependencies | ++ | + | + | − | − | + |
| Parser булевих виразів | ++ | ++ | + | ++ | ± | − |
| Пояснювані algebraic rewrites | ++ | ++ | ++ | + | + | − |
| Exact SOP/POS | + | + | ++ | ± | − | − |
| Don’t-care minimization | + | + | ++ | ++ | ± | ± |
| Espresso minimization | − | − | − | ++ | − | − |
| Multi-output input | + | ± | ± | ++ | ± | ± |
| Shared multi-output cover | − | ± | − | ++ | − | − |
| Equivalent CNF/DNF | ++ | ++ | ++ | ++ | ± | ± |
| Linear equisatisfiable CNF | ++ | ++ | ± | ± | ++ | ± |
| SAT solver | + | ++ | ± | + | ++ | − |
| Incremental SAT/assumptions | − | ++ | − | + | ++ | − |
| UNSAT core/proof tracing | − | ++ | − | − | ++ | − |
| Counterexample equivalence | ++ | ++ | ± | + | ++ | + |
| ROBDD | + | ++ | − | ++ | ± | ++ |
| Model counting | + | ++ | ± | + | ± | + |
| Dynamic BDD reordering | − | + | − | ± | − | − |
| Cardinality/PB constraints | − | ++ | − | − | ++ | ± |
| MaxSAT | − | ++ | − | − | ± | − |
| Cancellation/resource budgets | ++ | ++ | ± | ± | ++ | ± |
| DIMACS export | ++ | ++ | ± | ++ | SMT-LIB | − |
| BLIF/Verilog/LaTeX/CSV | ++ | ± | ± | ± | − | − |
| Компактний готовий CLI | ++ | − | − | ± | ± | − |

## Нове конкурентне положення

### Де LogicalOptimizer тепер сильніший

1. **Найцілісніший легкий .NET workflow серед досліджених рішень.**
   Parser, simplifier, exact minimizer, SAT, BDD, truth tables і exporters доступні без native package.

2. **Краща explainability, ніж у solver-first бібліотек.**
   Основний результат залишається читабельним булевим виразом, а не лише solver verdict або decision diagram.

3. **Два різні CNF-контракти.**
   Equivalent CNF та Tseitin equisatisfiable CNF розділені явно.

4. **Гібридна equivalence strategy.**
   Truth table для малих формул, SAT для великих і окремий BDD checker.

5. **Сильний CSV/CLI сценарій.**
   Partial tables, don’t-care і декілька outputs інтегровані в один інструмент.

6. **Хороша тестова база.**
   Додані exhaustive sweeps, SAT fuzzing, soundness guard, BDD, Tseitin і resource tests.

### Де бібліотека все ще слабша

1. **AST залишається бінарним і частково mutable.**
   `AndNode`/`OrNode` не n-ary, а `ForceParentheses` залишається змінюваною властивістю семантичного вузла.

2. **Немає formula factory та повного hash-consing AST.**
   Tseitin і BDD використовують кешування, але основний AST не канонізується під час створення так, як у LogicNG.

3. **SAT solver не incremental.**
   Немає assumptions, повторного використання learnt clauses, proof tracing або unsat core.

4. **BDD backend базовий.**
   Немає dynamic variable reordering, quantification/composition API або спеціалізованого memory manager рівня DecisionDiagrams.

5. **Multi-output не мінімізується спільно.**
   Outputs обробляються окремо; Espresso може знаходити спільні product terms для декількох функцій.

6. **Немає cardinality, pseudo-Boolean і MaxSAT.**
   Для configuration/optimization LogicNG і Z3 значно ширші.

7. **Немає Espresso backend.**
   Quine–McCluskey непридатний для масштабів на кшталт десятків входів, де потрібна евристична мінімізація.

8. **Публічний API ще завеликий.**
   Реалізаційні структури SAT/BDD/minimizer частково доступні напряму без чіткого поділу packages.

## Критична поправка щодо «provably minimal»

README заявляє гарантовану мінімальність для всіх виразів до 10 змінних. Поточна реалізація не забезпечує цю гарантію безумовно:

- prime implicants для цієї зони справді генеруються без `pairComparisonLimit`;
- але minimum-cover branch-and-bound завжди має `BranchAndBoundStepLimit = 200_000`;
- після перевищення пошук може повернути вже знайдений, але не доведено оптимальний cover;
- якщо cover ще не знайдений, виконується greedy completion.

Тобто коректність результату зберігається, але глобальна мінімальність у складному cyclic core може бути не доведена.

Рекомендовані варіанти:

1. Повернути `MinimizationStatus.MinimalProven / Heuristic / BudgetExceeded`.
2. Для guarantee-зони не використовувати тихий greedy fallback.
3. Або послабити документацію до:

> Exact minimization is attempted up to 12 variables; optimality is reported explicitly when proven.

Додатково назва «мінімальний вираз» потребує уточнення cost model. Поточний cover мінімізується за:

1. кількістю literals;
2. кількістю terms.

Фінальний multi-level результат обирається за literals, потім AST node count. Це не тотожне мінімуму gate count, depth, string length або circuit delay.

## Оцінка за категоріями

Шкала 1–5 відображає не абсолютну якість, а позицію в конкретній категорії.

| Категорія | LogicalOptimizer | Лідер серед порівнюваних | Коментар |
|---|---:|---|---|
| Легке вбудовування в .NET | 5 | LogicalOptimizer | Немає native/package dependencies |
| Читабельне symbolic simplification | 4 | LogicNG / SymPy | Сильний набір правил, але AST ще неканонічний |
| Точна мала SOP/POS мінімізація | 4 | SymPy | Функціонально близько, але статус доказу оптимальності треба виправити |
| Велика two-level мінімізація | 2 | PyEDA/Espresso | Немає Espresso |
| SAT capability | 3 | Z3 / LogicNG | Є CDCL, але немає incremental/proofs/cores |
| BDD capability | 3 | LogicNG / DecisionDiagrams | Є ROBDD і counting, немає reordering та advanced operations |
| CNF conversion | 4 | LogicNG | Equivalent + Tseitin уже сильні; немає Plaisted–Greenbaum/configurable encoders |
| Configuration/PB domain | 1 | LogicNG / Z3 | Немає PB/cardinality/MaxSAT |
| CLI та формати | 5 | LogicalOptimizer | Найкраща інтегрована surface у цьому наборі |
| Зрілість/ecosystem | 2 | Z3 / SymPy | Молодий проєкт без зовнішньої adoption evidence |

## Оновлений рейтинг конкурентів

Рейтинг залежить від сценарію:

### Для lightweight .NET boolean simplification

1. **LogicalOptimizer**
2. AngouriMath/xFunc
3. Z3 із власним presentation layer
4. DecisionDiagrams із власним parser/export layer

### Для industrial constraint solving

1. **Z3**
2. LogicNG
3. PyEDA
4. LogicalOptimizer

### Для EDA two-level minimization

1. **PyEDA/Espresso**
2. SymPy
3. LogicalOptimizer
4. LogicNG

### Для повного Java logic toolkit

1. **LogicNG**
2. Z3 Java API
3. PyEDA через окремий процес
4. LogicalOptimizer через окремий .NET процес

## Рекомендований наступний розвиток

### P0

1. Виправити контракт optimality:
   - додати `MinimizationStatus`;
   - не заявляти доказану мінімальність без завершеного exact cover search.
2. Додати differential tests проти SymPy/PyEDA для малих truth tables.
3. Додати end-to-end benchmarks із фіксованим corpus:
   - random formulas;
   - dense truth tables;
   - parity;
   - multiplexer;
   - adder outputs;
   - CNF blow-up cases.

### P1

1. Immutable n-ary AST.
2. `AstFactory` з associativity/commutativity canonicalization і hash-consing.
3. Прибрати `ForceParentheses` з AST.
4. Розділити packages:
   - `LogicalOptimizer.Core`;
   - `LogicalOptimizer.Minimization`;
   - `LogicalOptimizer.Sat`;
   - `LogicalOptimizer.Bdd`;
   - `LogicalOptimizer.Cli`.

### P2

1. Espresso backend або PLA bridge для великих two-level задач.
2. Shared-cover multi-output minimization.
3. SAT assumptions та incremental solving.
4. BDD variable ordering heuristics/reordering.
5. Plaisted–Greenbaum CNF.

### P3

Cardinality/PB/MaxSAT варто додавати лише якщо цільовим доменом стане product configuration або constraint optimization. Для поточної ніші це не першочергово.

## Підсумок

Оновлення суттєво змінило позицію бібліотеки. LogicalOptimizer тепер:

- функціонально випереджає прості .NET expression simplifiers;
- наблизився до PyEDA в малих exact-minimization сценаріях;
- має частину можливостей LogicNG у значно компактнішому вигляді;
- може перевіряти великі формули без truth-table завдяки власному SAT;
- пропонує унікально цілісний dependency-free .NET CLI/API.

Він усе ще не є прямою заміною LogicNG, Z3 або Espresso:

- LogicNG ширший архітектурно;
- Z3 незрівнянно сильніший як solver;
- Espresso краще масштабується для two-level EDA minimization.

Найкраще позиціонування:

> Dependency-free .NET toolkit for explainable Boolean simplification, exact small-function minimization, scalable CNF generation, and built-in SAT/BDD verification.

## Джерела

- [LogicNG Formula Factory](https://logicng.org/documentation/formula-factory/)
- [LogicNG formula hierarchy](https://logicng.org/documentation/formulas/)
- [LogicNG SAT solvers](https://logicng.org/documentation/solvers/sat-solving/)
- [LogicNG cardinality constraints](https://logicng.org/documentation/formulas/cardinality-constraints/)
- [LogicNG CNF transformations](https://logicng.org/documentation/formulas/operations/transformations/normal-form-transformations/)
- [SymPy Logic: SOP/POS, `simplify_logic`, don’t-care](https://docs.sympy.org/latest/modules/logic.html)
- [PyEDA overview](https://pyeda.readthedocs.io/en/latest/)
- [PyEDA Espresso minimization](https://pyeda.readthedocs.io/en/latest/2llm.html)
- [Z3 official repository and .NET binding](https://github.com/Z3Prover/z3)
- [Z3 simplifiers](https://microsoft.github.io/z3guide/docs/strategies/simplifiers-summary/)
- [DecisionDiagrams NuGet documentation](https://www.nuget.org/packages/DecisionDiagrams)
