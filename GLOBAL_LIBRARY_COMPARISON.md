# Глобальне порівняння LogicalOptimizer з аналогами

Дата дослідження: 24 липня 2026 року.

## Резюме

LogicalOptimizer займає рідкісну нішу:

> Dependency-free .NET toolkit для парсингу, пояснюваного спрощення, малої exact-мінімізації, CNF/DNF, SAT/BDD-перевірки та експорту булевих функцій.

Глобальний пошук не виявив іншої зрілої нативної .NET-бібліотеки, яка в одному пакеті поєднує:

- parser і AST;
- rule-based simplification;
- exact SOP/POS із don’t-care;
- equivalent та Tseitin CNF;
- CDCL SAT;
- ROBDD і model counting;
- truth tables і multi-output CSV;
- DIMACS, BLIF, Verilog, LaTeX та інші exporters;
- готовий CLI.

Проте це не означає, що LogicalOptimizer є глобальним лідером у кожному алгоритмі. Спеціалізовані продукти випереджають його у своїх категоріях:

- LogicNG — повнота logic framework;
- Z3 — SMT і industrial solving;
- PyEDA/Espresso — велика two-level minimization;
- Berkeley ABC — багаторівневий circuit synthesis;
- CUDD/DecisionDiagrams/Biodivine — зрілі BDD operations;
- SAT4J/PySAT/NanoByte — solver-oriented API.

## Методика

Порівняння охоплює чотири різні категорії, які не слід змішувати:

1. **Expression simplifiers** — перетворюють формулу на читабельнішу.
2. **Logic minimizers** — шукають мінімальний або малий SOP/POS/circuit.
3. **SAT/SMT solvers** — відповідають на satisfiability/equivalence queries.
4. **BDD та synthesis systems** — представляють функції канонічно або оптимізують логічні мережі.

Оцінка продуктивності є якісною. Без єдиного corpus, однакової cost model та ізольованого benchmark environment числове порівняння було б недостовірним.

## Поточні можливості LogicalOptimizer

Перевірено за кодом і тестами:

- .NET 10, Apache-2.0;
- відсутні production package dependencies;
- 913 тестів проходять;
- parser: `!`, `&`, `|`, константи та групування;
- AST також підтримує XOR, NAND, NOR, implication та equivalence;
- rule-based optimization;
- Quine–McCluskey prime generation;
- minimum-cover branch-and-bound із fallback;
- SOP/POS і don’t-care;
- partial та multi-output CSV;
- equivalent CNF/DNF;
- equisatisfiable Tseitin CNF;
- власний CDCL SAT solver;
- SAT miter equivalence і counterexample;
- ROBDD, satisfiability, evaluation та model counting;
- budgets і cancellation;
- CLI та декілька export formats.

## Прямі .NET-аналоги

### MintPlayer.QuineMcCluskey

Колишній пакет `QuineMcCluskey` перейменовано на `MintPlayer.QuineMcCluskey`; стара лінійка deprecated. Це нативна .NET-реалізація Quine–McCluskey без production dependencies.

Порівняння:

| Критерій | LogicalOptimizer | MintPlayer.QuineMcCluskey |
|---|---:|---:|
| Exact two-level minimization | + | + |
| Parser довільного виразу | + | −/± |
| Don’t-care | + | + |
| POS і SOP | + | ± |
| SAT/BDD verification | + | − |
| Tseitin CNF | + | − |
| Multi-output CSV | + | − |
| Exporters/CLI | + | − |

Висновок: це прямий конкурент лише minimizer-компонента, а не всієї бібліотеки.

### NanoByte.SatSolver

Активний managed CDCL SAT solver для .NET Standard 2.0. Версія 0.5.0 опублікована 8 травня 2026 року. Підтримує generic literals, clauses, implication, at-most-one та domain-specific decider.

Сильніший за вбудований `SatSolver` у:

- чистоті solver API;
- generic variable model;
- at-most-one constraint;
- окремому package lifecycle;
- реальному downstream usage.

LogicalOptimizer сильніший у:

- автоматичному Tseitin encoding AST;
- equivalence miter;
- counterexample на рівні імен вхідного виразу;
- інтеграції із simplifier/minimizer/BDD.

### DecisionDiagrams

Managed .NET Standard BDD/CBDD package. Використовує:

- memory pool;
- unique table;
- hash-consing;
- complement edges;
- власний mark/sweep/shift GC;
- weak references для інтеграції з .NET GC.

DecisionDiagrams є сильнішою BDD-бібліотекою за шириною та оптимізацією ядра. LogicalOptimizer має простіший інтегрований ROBDD, але виграє готовим parser-to-verdict workflow.

### AngouriMath

Загальна symbolic math бібліотека для .NET. Версія 1.4.0 опублікована 22 січня 2026 року; пакет має понад 300 тисяч сумарних завантажень і MIT license.

AngouriMath сильніший у:

- загальній символьній математиці;
- equations, calculus, sets, matrices;
- ecosystem/adoption;
- parser infrastructure.

LogicalOptimizer значно спеціалізованіший у Boolean minimization, SAT/BDD, truth tables та circuit-oriented exports.

### BoolExprNet

.NET port C++ `boolexpr`. GitHub-проєкт має малу adoption, native submodules і не демонструє активного package ecosystem.

Він цікавий як порт спеціалізованої Boolean expression library, але LogicalOptimizer має ширшу готову surface та не потребує native bridge.

## Symbolic logic frameworks

### LogicNG

Найближчий концептуальний аналог, хоча написаний на Java.

LogicNG підтримує:

- immutable hash-consed formulas;
- formula factory;
- canonical associativity/commutativity;
- n-ary AND/OR;
- implication, equivalence, PB та cardinality constraints;
- NNF/CNF/DNF;
- factorization, BDD CNF, Tseitin і Plaisted–Greenbaum;
- MiniSat, Glucose і MiniCARD;
- incremental solving і assumptions;
- MaxSAT;
- BDD і DNNF;
- model enumeration;
- proofs, unsat cores та explanations.

LogicNG залишається суттєво зрілішим framework. LogicalOptimizer виграє лише у:

- нативності .NET;
- меншій вазі;
- відсутності dependencies;
- компактному CLI;
- орієнтації на читабельний результат.

### SymPy Logic

SymPy підтримує:

- symbolic Boolean expressions;
- `simplify_logic`;
- exact SOP/POS;
- don’t-care;
- ANF;
- truth-table-derived forms;
- інтеграцію з повною computer algebra system.

Точна логічна мінімізація має default guard у 8 змінних через експоненційну складність. LogicalOptimizer намагається працювати до 12 змінних із budgets, але його доказ optimality потребує явного статусу.

SymPy сильніший у математичній екосистемі та стабільності API; LogicalOptimizer — у .NET, SAT/BDD, CLI та exporters.

## EDA та logic minimization

### PyEDA + Espresso

PyEDA поєднує:

- expressions;
- three-valued truth tables;
- ROBDD;
- formal equivalence;
- PicoSAT;
- Espresso C extension;
- PLA/DIMACS parsers.

Espresso є евристичним, а не exact minimizer-ом, але масштабується набагато краще за Quine–McCluskey. PyEDA демонструє multi-output PLA із 50 inputs і 5 outputs.

LogicalOptimizer не конкурує з Espresso на великих таблицях. Його перевага — exact small-function workflow і managed .NET deployment.

### Berkeley Espresso

Класичний two-level logic minimizer працює з ON/OFF/DC sets і multi-output PLA. Він шукає мале еквівалентне покриття без гарантії глобального мінімуму.

Для LogicalOptimizer Espresso є найбільш доцільним майбутнім optional backend:

- Quine–McCluskey — exact/малий режим;
- Espresso — heuristic/великий режим.

### Berkeley ABC

ABC — industrial-strength academic system для sequential logic synthesis та formal verification. Використовує AIG/DAG-aware rewriting, technology mapping і equivalence checking.

ABC оптимізує не текстовий SOP/POS, а логічні мережі та circuits. У прикладі офіційного README AIG rewriting скорочує AND count і перевіряє еквівалентність.

Порівняння:

- LogicalOptimizer — expression-level, API/CLI, educational/readable;
- ABC — circuit-level, multi-level synthesis, FPGA/ASIC mapping, formal verification.

Якщо мета — реальний hardware synthesis, ABC є правильнішим інструментом.

### Simplifier 2025

Дослідницький інструмент оптимізує малі multi-output subcircuits через базу SAT-оптимізованих схем. Автори повідомляють додаткове середнє скорочення після ABC для AIG та значніше для BENCH.

Цей підхід показує перспективний напрям для LogicalOptimizer:

- не глобальний exact search;
- локальна заміна малих підграфів;
- precomputed optimal functions;
- SAT verification кожної заміни.

## SAT/SMT ecosystem

### Microsoft Z3

Z3 4.16.0 випущено 19 лютого 2026 року. Є офіційний .NET binding і NuGet artifacts.

Z3 підтримує:

- SAT та SMT theories;
- arithmetic, arrays, bit-vectors, strings, quantifiers;
- tactics та simplifiers;
- incremental contexts;
- assumptions;
- proofs/models/cores;
- багатомовні API.

Вбудований SAT LogicalOptimizer не є конкурентом Z3 за solver power. Його мета інша: lightweight soundness/equivalence guard без native dependency.

### SAT4J

Java toolkit для SAT, MaxSAT, pseudo-Boolean і MUS. Сильний у flexibility, decorators/strategies та integration use cases.

### PySAT

Python toolkit, що уніфікує декілька state-of-the-art SAT solvers і надає cardinality/PB encodings.

### NanoByte.SatSolver

Найближчий managed .NET SAT-конкурент, але без expression minimization та BDD.

## BDD ecosystem

### CUDD

C/C++ package для BDD, ADD та ZDD із багатою спеціалізованою інфраструктурою. Значно ширший за ROBDD LogicalOptimizer, але вимагає native integration.

### BuDDy

Класична C++ BDD package з node tables, caches, reference counting і dynamic resizing.

### Biodivine/LibBDD

Сучасна Rust-бібліотека. Підтримує:

- string expressions;
- Boolean operations;
- CNF/DNF construction;
- serialization;
- valuation/path iterators;
- projection/restriction;
- export назад у Boolean expression;
- DOT graphs;
- thread-friendly owned BDDs.

За BDD API вона ширша за LogicalOptimizer.

### Rust `boolean_expression`

Поєднує AST, law-based simplification, BDD і BDD-to-expression cubelist reduction. Це один із найближчих архітектурних аналогів поза .NET, але без вбудованого CDCL/Tseitin/CLI набору LogicalOptimizer.

## Глобальна матриця

| Рішення | Основний домен | Exact мала мінімізація | Велика heuristic мінімізація | SAT/SMT | BDD | Multi-output | .NET |
|---|---|---:|---:|---:|---:|---:|---:|
| LogicalOptimizer | Expression toolkit | + | − | + | + | + | ++ |
| MintPlayer.QM | QM minimizer | + | − | − | − | − | ++ |
| NanoByte SAT | SAT | − | − | + | − | − | ++ |
| DecisionDiagrams | BDD | − | − | ± | ++ | ± | ++ |
| AngouriMath | Symbolic math | ± | − | − | − | − | ++ |
| LogicNG | Logic framework | + | + | ++ | ++ | + | − |
| SymPy | Symbolic math/logic | ++ | − | ± | − | − | − |
| PyEDA | EDA | ± | ++ | + | ++ | ++ | − |
| Z3 | SMT | − | − | ++ | ± | ± | + |
| SAT4J | SAT/PB/MaxSAT | − | − | ++ | − | ± | − |
| Berkeley Espresso | Two-level EDA | − | ++ | − | − | ++ | − |
| Berkeley ABC | Circuit synthesis | − | ++ | ++ | ± | ++ | − |
| CUDD | Decision diagrams | − | − | ± | ++ | + | − |
| Biodivine LibBDD | Decision diagrams | − | − | ± | ++ | ± | − |

## Порівняння зрілості

| Проєкт | Ознаки зрілості | Оцінка відносно LogicalOptimizer |
|---|---|---|
| Z3 | десятиліття розвитку, 12k+ GitHub stars, регулярні releases | набагато зріліший |
| SymPy | велика scientific ecosystem, стабільна документація | набагато зріліший |
| LogicNG | комерційна підтримка, глибока документація | набагато зріліший |
| ABC | 6k+ commits, research/industrial usage | набагато зріліший |
| PyEDA | відома EDA feature set, але release 0.28.0 датований 2018 роком | алгоритмічно зріліший, maintenance слабший |
| AngouriMath | 300k+ NuGet downloads, release 2026 | зріліший як package |
| DecisionDiagrams | 38k+ NuGet downloads, останній release 2023 | більш перевірений BDD package |
| NanoByte SAT | активний release 2026, downstream production package | вузький, але має adoption |
| LogicalOptimizer | 913 tests, CI, широке власне ядро | технічно перспективний, adoption не підтверджена |

## Ключова проблема позиціонування

README називає результати до 10 змінних «provably minimal». Поточний minimum-cover search має глобальний `BranchAndBoundStepLimit = 200_000` і після вичерпання пошуку може використати greedy completion або повернути найкраще знайдене покриття без доказу optimality.

Тому слід:

- додати `MinimizationStatus.MinimalProven`;
- окремо повертати `Heuristic` або `BudgetExceeded`;
- не заявляти global minimum без доказаного завершення cover search;
- документувати cost model: literals, потім terms; для фінального AST — literals, потім nodes.

Це особливо важливо у порівнянні з SymPy та exact QM tools.

## Рейтинг за сценаріями

### Lightweight Boolean toolkit для .NET

1. **LogicalOptimizer**
2. AngouriMath + окремий SAT/BDD package
3. MintPlayer.QuineMcCluskey + NanoByte + DecisionDiagrams
4. Z3 із власним expression/presentation layer

### Exact мала SOP/POS мінімізація

1. **SymPy**
2. LogicalOptimizer після виправлення optimality status
3. MintPlayer.QuineMcCluskey
4. standalone QM tools

### Велика two-level minimization

1. **Berkeley Espresso / PyEDA**
2. ABC для переходу до multi-level network optimization
3. LogicNG
4. LogicalOptimizer

### SAT/SMT

1. **Z3**
2. PySAT / сучасні native SAT backends
3. LogicNG / SAT4J
4. NanoByte.SatSolver
5. LogicalOptimizer

### BDD

1. **CUDD / сучасні specialized BDD packages**
2. LogicNG
3. DecisionDiagrams / Biodivine LibBDD
4. PyEDA
5. LogicalOptimizer

### Explainable expression CLI

1. **LogicalOptimizer**
2. standalone QM GUI/CLI tools
3. SymPy scripts
4. LogicNG custom application

## Рекомендований roadmap

### P0 — довіра до результату

1. Явний optimality status.
2. Differential corpus проти SymPy та Espresso.
3. Benchmark suite із машинно-читаними JSON results.
4. Fuzz/property tests із Z3 як зовнішнім oracle.

### P1 — усунути головне архітектурне відставання

1. Immutable n-ary AST.
2. Formula factory:
   - flatten;
   - unique operands;
   - constant folding;
   - complement folding;
   - structural interning/hash-consing.
3. Прибрати `ForceParentheses` із семантичного AST.
4. Один canonical rewrite traversal.

### P2 — масштабована мінімізація

1. Optional Espresso/PLA backend.
2. Shared-cover multi-output minimization.
3. Precomputed optimal 3–5 input subcircuits.
4. AIG representation для multi-level optimization.

### P3 — solver/BDD розвиток

1. Incremental SAT і assumptions.
2. UNSAT proof/core лише за реальної продуктової потреби.
3. BDD variable-order heuristics.
4. Existential quantification, restriction і composition.
5. Plaisted–Greenbaum CNF.

### P4 — package maturity

1. Розділити core, SAT, BDD, minimization і CLI.
2. Target `netstandard2.0` або сучасний LTS .NET для library packages; .NET 10 залишити CLI target.
3. NuGet packaging, SourceLink, symbols і API documentation.
4. Semantic versioning та compatibility tests.
5. Публічні benchmark і adoption examples.

## Остаточний висновок

LogicalOptimizer не має прямого повного аналога у дослідженій .NET ecosystem. Його конкурентна перевага — не найкращий у світі SAT, BDD чи minimizer окремо, а їхня інтеграція:

```text
Parser
  → explainable rewrite
  → exact small minimization
  → equivalent/Tseitin CNF
  → SAT/BDD verification
  → CLI/export
```

Щоб перейти від сильного prototype до конкурентної бібліотеки, найбільший ефект дадуть:

1. чесний optimality contract;
2. canonical immutable AST;
3. Espresso backend;
4. package/API stabilization;
5. відкриті порівняльні benchmarks.

## Джерела

### .NET

- [QuineMcCluskey NuGet](https://www.nuget.org/packages/QuineMcCluskey)
- [NanoByte.SatSolver NuGet](https://www.nuget.org/packages/NanoByte.SatSolver)
- [DecisionDiagrams NuGet](https://www.nuget.org/packages/DecisionDiagrams)
- [AngouriMath NuGet](https://www.nuget.org/packages/AngouriMath)
- [BoolExprNet GitHub](https://github.com/alethic/BoolExprNet)
- [Microsoft Z3 GitHub](https://github.com/Z3Prover/z3)

### Logic frameworks

- [LogicNG](https://logicng.org/)
- [LogicNG Formula Factory](https://logicng.org/documentation/formula-factory/)
- [LogicNG SAT](https://logicng.org/documentation/solvers/sat-solving/)
- [LogicNG CNF transformations](https://logicng.org/documentation/formulas/operations/transformations/normal-form-transformations/)
- [SymPy Logic](https://docs.sympy.org/latest/modules/logic.html)
- [SAT4J](https://www.sat4j.org/)
- [PySAT](https://github.com/pysathq/pysat)

### EDA і synthesis

- [PyEDA](https://pyeda.readthedocs.io/en/latest/)
- [PyEDA Espresso minimization](https://pyeda.readthedocs.io/en/latest/2llm.html)
- [Berkeley Espresso manual](https://people.eecs.berkeley.edu/~alanmi/research/espresso/espresso_5.html)
- [Berkeley ABC GitHub](https://github.com/berkeley-abc/abc)
- [Simplifier: Boolean Circuit Simplification](https://arxiv.org/abs/2503.19103)

### BDD

- [CUDD](https://github.com/SSoelvsten/cudd)
- [BuDDy](https://buddy.sourceforge.net/manual/main.html)
- [Biodivine LibBDD](https://docs.rs/biodivine-lib-bdd)
- [Rust boolean_expression](https://lib.rs/crates/boolean_expression)
