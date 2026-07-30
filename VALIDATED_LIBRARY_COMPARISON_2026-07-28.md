# Валідоване порівняння LogicalOptimizer v3.0.0 з аналогами

Дата повторної перевірки: **29 липня 2026 року**.
Перевірений релізний стан: **тег `v3.0.0`**, commit **`459afba`**
(`main`, 2026-07-29).

Цей документ повністю замінює попередню редакцію, яка описувала v2.1. Факти про
LogicalOptimizer нижче звірені з поточним кодом, тестами та package metadata; факти про
конкурентів — з їхньою офіційною документацією або офіційними репозиторіями. Якісна
матриця не є єдиним cross-library benchmark.

> **Релізний статус.** Annotated tag `v3.0.0` безпосередньо вказує на commit
> `459afba`; цей самий commit є поточним `main`/`origin/main`. Усі сім
> publishable-проєктів мають `Version=3.0.0`, а `CHANGELOG.md` датує реліз
> 2026-07-29. Факт наявності тега перевірено локально; наявність усіх пакетів
> v3.0.0 у NuGet Gallery цим прогоном окремо не перевірялася.

---

## 1. Методика й відтворювані результати

### Що перевірено локально

На Windows x64, .NET SDK 10.0.301:

```powershell
dotnet build LogicalOptimizer.sln -c Release --no-restore
dotnet test LogicalOptimizer.Tests\LogicalOptimizer.Tests.csproj `
  -c Release --no-build `
  --filter "Category!=Performance&Category!=Exhaustive"
dotnet list LogicalOptimizer.sln package --vulnerable --include-transitive
```

Результат повторного прогону 29.07.2026:

- Release build: **0 warnings, 0 errors**;
- PR-gate набір: **1035/1035 passed**, 0 failed, 0 skipped, 18 s;
- відомі NuGet-вразливості: **не знайдено** в усіх 9 проєктах solution;
- production/runtime-залежностей у бібліотечних пакетів немає;
  `Microsoft.SourceLink.GitHub 8.0.0` має `PrivateAssets=All` і є build-time залежністю.

Performance та Exhaustive категорії цим gate-прогоном навмисно не охоплені. Покриття
коду в цій перевірці не перемірювалося, тому попередні coverage-відсотки не
використовуються як актуальний доказ.

### Межі доказовості

- Алгоритмічні claims LogicalOptimizer підтверджено читанням production-коду,
  контрактними/архітектурними тестами та changelog.
- Числа SymPy/PyEDA взято з committed, відтворюваного comparison harness; методика
  і середовище наведені в [`doc/BENCHMARKS.md`](doc/BENCHMARKS.md).
- CaDiCaL, Kissat, ABC, Espresso, CUDD, Z3 і LogicNG **не запускалися на одному
  стенді** з LogicalOptimizer. Твердження про їхню спеціалізацію спираються на
  офіційні джерела, а не на локальний performance benchmark.
- Позначка «краще/гірше» стосується конкретної категорії, а не універсальної якості.

---

## 2. Перевірений стан LogicalOptimizer v3.0.0

### Платформа, пакети й API

- Бібліотеки multi-targeted: `net8.0;net10.0`; CLI: `net10.0`.
- Сім publishable пакетів:
  `LogicalOptimizer.Core`, `.Sat`, `.Bdd`, `.Dnnf`, `.Minimization`,
  facade `LogicalOptimizer` і dotnet tool `LogicalOptimizer.Cli`.
- Facade залежить від Core/Sat/Bdd/Minimization. DNNF — окремий opt-in пакет,
  який залежить від Core і Sat; facade його автоматично не підтягує.
- Залежності ациклічні й спрямовані вниз; це закріплено architecture test.
- Публічна поверхня: **58 top-level типів**:
  Core 23 · Sat 10 · Bdd 1 · Dnnf 2 · Minimization 5 · facade 17.
  Member-level API закріплено `PublicApi.approved.txt`.
- Документаційні приклади дзеркаляться виконуваними `DocExamplesTests`.

### Канонічне symbolic core

- Immutable n-ary AST для AND/OR; розширені оператори XOR/IMP/EQV/NAND/NOR
  лишаються binary nodes.
- `FormulaFactory` виконує flattening, stable sorting, deduplication, constant і
  complement folding та structural interning.
- Важлива межа контракту: factory — канонічний шлях побудови; public-конструктори
  `AndNode`/`OrNode` лишаються low-level raw AST і самі канонізацію не гарантують.
- Є parser, precedence-aware formatter, AST visualization, truth tables до
  20 змінних і метрики AST.
- Rewrite engine застосовує De Morgan, absorption, consensus, redundancy,
  factorization і bounded expand/reduce. Candidate приймається лише після
  позитивного доказу еквівалентності.

### Точна та евристична мінімізація

- До **10 змінних** guarantee zone використовує Quine–McCluskey, essential primes,
  row/column dominance, branch-and-bound cover і lower-bound pruning без
  штучного cover-step cap; `MinimalProven` означає завершений пошук мінімуму.
- Для 11–12 змінних exact-пошук можливий, але вичерпання work budget чесно
  повертає `BudgetExceeded`.
- Для 13–24 змінних працює SAT-based prime-cover path; для більших — Espresso-lite.
  Обидва результати евристичні й проходять equivalence verification.
- SOP і POS мають окремі proof statuses:
  `MinimizationStatus` та `CnfMinimizationStatus`.
- Є don't-cares, CSV truth tables і internal multi-output shared-cube minimizer.
- `OptimizationQualityAnalyzer.IsOptimal` істинний лише для доведеного two-level
  мінімуму; 0–100 `OptimalityScore` явно лишається евристичною оцінкою.

### SAT, CNF і аналіз формул

Власний dependency-free CDCL solver містить:

- two-watched literals, implication trail, decision levels і 1UIP learning;
- binary-heap VSIDS, Luby restarts, LBD і learned-clause DB reduction;
- bounded subsumption та self-subsuming resolution preprocessing;
- incremental solve з assumptions, three-valued `SatResult`, UNSAT cores;
- optional DRAT logging; тестовий RUP checker незалежно перевіряє additions;
- cardinality, pseudo-Boolean encoders і weighted partial MaxSAT;
- Tseitin та Plaisted–Greenbaum equisatisfiable CNF.

Facade також надає SAT-miter equivalence/counterexample, backbone і model
enumeration. `OptimizationResult.CheckEquivalence()` зберігає three-valued
семантику; boolean `IsEquivalent()` повертає true лише після позитивного доказу.

### BDD

`BinaryDecisionDiagram` є ROBDD engine з:

- unique table, hash-consing, memoized ITE;
- канонічними complemented edges і O(1) negate;
- exact `BigInteger` model count, model search/enumeration;
- restrict, compose, existential і universal quantification;
- static order heuristics та справжніми adjacent-level swaps для sifting;
- reachable-node garbage collection, node budget і cancellation.

Це повноцінний малий managed ROBDD engine, але не заміна зрілому CUDD за
різноманіттям diagram types, reorder strategies, memory management і масштабом.

### d-DNNF knowledge compilation

Новий окремий `LogicalOptimizer.Dnnf` пакет закрив найбільшу функціональну
прогалину попереднього звіту:

- top-down decision-d-DNNF compiler;
- full biconditional Tseitin CNF, unit propagation;
- connected-component decomposition, component caching і hash-consing;
- smooth scopes та explicit free-variable choices;
- exact `BigInteger` #SAT, weighted model counting і lazy projected enumeration;
- typed node-budget failure та cancellation.

Full biconditional Tseitin encoding тут є equi-count over input variables:
кожна satisfying input assignment має рівно одне розширення на функціонально
визначені auxiliaries. Counts перевіряються тестами проти BDD і brute force.

Обмеження: API значно вужчий за LogicNG; немає широкого набору compiled-circuit
queries/transformations, persistence format або промислової історії експлуатації.

### AIG DAG-aware rewriting — головна зміна v3.0.0

У v3.0.0 `OptimizationOptions.EnableAigRewriting=true` за замовчуванням.
Внутрішній pipeline містить:

- structural hashing і complemented edges;
- reference counts, MFFC, ≤4-input k-feasible cut enumeration;
- NPN canonicalization;
- precomputed exact minimum-AND library для всіх 1–4 input NPN classes
  (2, 4, 14, 222 класи);
- DAG-aware substitution лише за строгого зменшення;
- незалежний equivalence proof перед прийняттям facade candidate.

Отже оцінку «multi-level synthesis: лише задум» із v2.1 треба замінити на
«реальний bounded AIG cut rewriting». Водночас це ще не technology mapping:
немає standard-cell/LUT library mapping, sequential synthesis, retiming,
timing/area constraints або широкого набору ABC-style passes.

### Контракти ресурсів і чесність fallback

Реліз v3.0.0 також виправляє кілька contract gaps:

- SAT `Unknown` більше не приймається як equivalence success;
- QM budget exhaustion не маскується як `Heuristic`;
- typed exceptions розділяють budget/size failures і invariant failures:
  `ComputationBudgetExceededException`, `NodeBudgetExceededException`,
  `NormalFormTooLargeException`;
- єдиний cooperative 10-second deadline охоплює весь facade call;
- POS status не втрачається;
- метрики містять convergence trace та calling-thread allocation traffic.

«10 секунд» — cooperative deadline, не hard real-time preemption: синхронна фаза
перерветься на найближчій token check.

---

## 3. Оновлена матриця можливостей

Легенда: `++` сильна спеціалізована підтримка · `+` штатна підтримка ·
`±` часткова/обмежена · `−` відсутня або не є предметом продукту.

| Можливість | LogicalOptimizer v3.0.0 | LogicNG | Z3 | CaDiCaL/Kissat + PySAT | Espresso | ABC | CUDD/dd | SymPy | PyEDA |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Нативний managed .NET | ++ | − | ± | − | − | − | − | − | − |
| Нуль runtime dependencies | ++ | + | − | ± | − | − | − | + | − |
| Parser + immutable canonical formula layer | ++ | ++ | ± | − | − | ± | ± | + | ++ |
| Пояснюване symbolic rewriting | ++ | ++ | + | − | − | − | − | ++ | + |
| Exact мала SOP/POS з proof status | ++ | + | − | − | ± | ± | − | ++ | ± |
| Heuristic велика two-level мінімізація | + | + | − | − | ++ | ++ | − | − | ++ |
| Multi-output shared cubes | + | ± | − | − | ++ | ++ | − | − | ++ |
| Multi-level AIG rewriting | + | − | − | − | − | ++ | − | − | ± |
| Technology mapping / sequential synthesis | − | − | − | − | − | ++ | − | − | − |
| Equivalent CNF/DNF | ++ | ++ | ± | − | − | ± | ± | ++ | ++ |
| ANF / Zhegalkin | + | ± | ± | − | − | − | − | + | ± |
| Tseitin + Plaisted–Greenbaum CNF | ++ | ++ | ++ | ++ | − | ± | − | ± | ± |
| CDCL SAT | + | ++ | ++ | ++ | − | + | − | ± | + |
| Incremental assumptions / UNSAT core | + | ++ | ++ | ++ | − | ± | − | − | ± |
| Proof trace | + (DRAT) | ± | ++ | ++ | − | ± | − | − | − |
| MaxSAT | + | ++ | ± | ++ | − | − | − | − | − |
| Cardinality / PB | + | ++ | ++ | ++ | − | − | − | − | − |
| ROBDD + complemented edges | + | ++ | ± | − | − | ± | ++ | − | ++ |
| BDD dynamic reordering | + | + | − | − | − | ± | ++ | − | ± |
| BDD quantify/compose/restrict | + | + | ± | − | − | ± | ++ | − | ± |
| d-DNNF compilation | + | ++ | − | − | − | − | − | − | − |
| Exact #SAT | + (BDD/d-DNNF) | ++ | ± | ± | − | − | + | ± | + |
| Weighted model count | + (d-DNNF) | ± | ± | ± | − | − | + | − | ± |
| Backbone / model enumeration | + | ++ | + | + | − | − | ± | ± | ± |
| Cooperative cancellation / budgets | ++ | ++ | ++ | + | ± | ± | ± | ± | ± |
| DIMACS/BLIF/Verilog/LaTeX/C# export | ++ | ± | ± | ± | ± | ++ | − | ± | ± |
| Готовий CLI / dotnet tool | ++ | − | ± | ± | ++ | ++ | − | − | ± |

Матриця показує breadth, а не throughput. `+` у SAT/BDD/d-DNNF не означає
паритет зі спеціалізованими engines: LogicalOptimizer має реальну реалізацію,
але меншу зрілість, менше алгоритмічних варіантів і слабшу зовнішню валідацію.

---

## 4. Порівняння за конкурентами

### LogicNG

Найближчий концептуальний аналог: immutable formula factory, canonical formulas,
normal-form transformations, кілька SAT/MaxSAT engines, cardinality/PB, BDD,
d-DNNF, model counting і handlers. Офіційна документація прямо описує три SAT
solver families (MiniSat, Glucose, MiniCARD), кілька MaxSAT algorithms і BDD/DNNF
knowledge compilation.

**Що змінило проти попереднього звіту:** LogicalOptimizer тепер також має
d-DNNF, ANF, true BDD adjacent-swap sifting та bounded AIG rewriting. За breadth
основних propositional задач розрив значно менший.

**Перевага LogicalOptimizer:** native managed .NET, zero runtime dependencies,
єдиний facade, явний SOP/POS proof status, читабельний optimized expression і
готовий dotnet tool.

**Залишковий gap:** LogicNG має роки production use, кілька SAT і MaxSAT
алгоритмів, ширші encodings/transformations/handlers, глибшу документацію та
багатший DNNF/model-counting stack. Паритет «за кількістю галочок» не є паритетом
за зрілістю.

### Z3

Z3 — SMT solver, а не boolean-expression minimizer. Він охоплює arithmetic,
bit-vectors, arrays, strings, quantifiers, tactics, optimization і soft
constraints. Його .NET binding потребує native Z3.

**Перевага LogicalOptimizer:** компактний dependency-free propositional stack,
two-level minimization із proof status, human-readable output, BDD/d-DNNF і
domain-specific exporters.

**Gap:** усі SMT theories, industrial scale, tactic ecosystem, proof/model
інфраструктура. LogicalOptimizer не повинен позиціонуватися як заміна Z3.

### CaDiCaL / Kissat + PySAT

CaDiCaL — зрілий incremental CDCL library/solver; Kissat — highly optimized
bare-metal C SAT solver. Офіційний опис Kissat прямо називає його портом
CaDiCaL назад у C з оптимізованими структурами та scheduling; офіційні матеріали
рекомендують CaDiCaL для incremental usage, Kissat — для найшвидшого solving.
PySAT додає великий Python ecosystem encodings і solver adapters.

**Перевага LogicalOptimizer:** немає native binding/deployment, формула проходить
повний шлях parse → optimize → prove → export в одному managed API.

**Gap:** raw SAT throughput, preprocessing/inprocessing depth, competition-scale
validation, proof ecosystem і кількість backends. Локальний generated SAT corpus
є добрим regression gate, але не доказом state-of-the-art SAT performance.

### Berkeley Espresso

Espresso спеціалізується на heuristic two-level PLA minimization, включно з
ON/OFF/DC sets і multi-output covers. Він лишається еталонним спеціалізованим
інструментом для великих two-level задач.

**Перевага LogicalOptimizer:** exact small-zone proof status, symbolic
multi-level output, SAT/BDD/d-DNNF, managed embedding і safety contracts.

**Gap:** масштаб, якість і десятиліття практики саме multi-output/two-level
heuristics. Назва `EspressoLite` чесно відображає цей розрив.

### Berkeley ABC

ABC — система sequential logic synthesis і formal verification. Офіційний
репозиторій демонструє DAG-aware AIG rewriting, equivalence checking, AIGER/BLIF
flows і library embedding.

**Що змінило:** у LogicalOptimizer тепер справді є ABC-style локальний cut rewrite
з MFFC, NPN та exact ≤4-input library, увімкнений за замовчуванням у v3.0.0.
Тому це вже не лише future roadmap.

**Gap:** ABC все одно на кілька рівнів ширший: iterative AIG optimization,
technology mapping, LUT/standard-cell targets, sequential circuits, retiming,
formal verification flows і реальні netlists. LogicalOptimizer — expression
optimizer з одним bounded combinational rewrite pass, не EDA synthesis system.

### CUDD / Python `dd`

CUDD — спеціалізована native decision-diagram library; Python `dd` надає Python
backends, включно з CUDD. Сильні сторони — mature unique/cache management,
garbage collection, широкий dynamic reordering і різні diagram families.

**Перевага LogicalOptimizer:** pure managed .NET, простий AST entry point,
інтеграція з optimizer/SAT/d-DNNF, typed budgets.

**Gap:** BDD scale, reorder portfolio, ZDD/ADD та low-level tuning. Наш true
adjacent-level sifting і complemented edges — суттєві, але не CUDD parity.

### SymPy

SymPy — широкий CAS із Boolean expressions, SOP/POS/ANF і `simplify_logic`.
Офіційна документація попереджає про exponential simplification і default
8-variable limit без `force=True`.

**Перевага LogicalOptimizer:** спеціалізований .NET deployment, scalable zone
routing, explicit proof/budget status, SAT/BDD/d-DNNF, multi-output CSV і
exporters.

**Gap:** загальна symbolic mathematics, Python ecosystem і expression
interoperability. Для малої exact two-level мінімізації результати на спільному
corpus переважно збігаються.

### PyEDA

PyEDA — Python toolkit із expression/truth-table/BDD representations та C
extension до Espresso, включно з `espresso_exprs` і `espresso_tts`.

**Перевага LogicalOptimizer:** managed .NET, proof statuses, integrated CDCL,
d-DNNF, resource contracts, multi-level AIG candidate.

**Gap:** mature Espresso binding і Python EDA workflow. У two-level shared corpus
обидві бібліотеки дали однаковий literal count на всіх рядках, де порівняння було
виконано; це parity на малому corpus, не універсальна перевага.

---

## 5. Контрольоване порівняння із SymPy / PyEDA

Committed corpus містить 17 функцій: 10 small (≤10 variables) і 7 mid
(11–24). Обидві сторони читають один `tools/comparison_corpus.txt`.
LogicalOptimizer вимірює median-of-7 після warm-up; Python script має
per-function timeout і self-skip, якщо dependency відсутня.

### Apples-to-apples two-level SOP

На 12 функціях, для яких є competitor result (`--max-vars 14`):

- LogicalOptimizer і PyEDA мають однаковий literal count **12/12**;
- LogicalOptimizer і SymPy збігаються на всіх функціях, де SymPy завершився;
- SymPy timeout: `pairs10`, `pairs12`, `collapse14`;
- це доводить parity результату на цьому corpus, але не на всіх функціях.

### Default multi-level output

Default optimizer може бути меншим за two-level output завдяки factorization і
AIG candidate. За зафіксованою таблицею:

- `maj3`: 5 literals проти 6;
- `xor3`: 10 проти 12;
- `maj4`: 9 проти 12;
- `pos6`: 6 проти 24.

Це чесна product-level перевага multi-level representation, але не доказ, що
власний SOP minimizer перевершує Espresso. Саме `--dnf` таблиця є коректним
two-level зіставленням.

Timings LogicalOptimizer (Windows) і competitors (Linux CI) не можна напряму
ранжувати між машинами. Для числових performance-висновків потрібен новий
single-machine benchmark усіх бібліотек.

---

## 6. Що принципово змінилося після v2.1

| Область | Стан у звіті v2.1 | Реліз v3.0.0 |
|---|---|---|
| Knowledge compilation | DNNF відсутній | Окремий d-DNNF package: #SAT, WMC, enumeration |
| BDD reordering | Rudell-style, частково через rebuild | Adjacent-level in-place-style swaps + GC |
| Normal forms | CNF/DNF | Додано canonical ANF |
| Multi-level synthesis | AIG лише internal foundation | Bounded DAG-aware AIG cut rewriting |
| AIG library | ≤3-variable subcircuits / майбутня робота | Exact minimum-AND recipes для всіх ≤4-input functions |
| Default behavior | AIG rewrite відсутній | У v3.0.0 увімкнений за замовчуванням |
| API | 53 types / 5 library packages | 58 types / 6 library packages + CLI |
| Contract honesty | Частина edge cases не була перевірена | Unknown rollback, typed failures, separate POS status |
| Gate | 888 cases у старому звіті | 1035/1035 cases |

Найбільша зміна позиціонування: LogicalOptimizer більше не лише інтегрує
symbolic minimization + SAT + BDD. Він має третій semantic engine (d-DNNF) і
реальний, хоча bounded, logic-synthesis pass.

---

## 7. Зважена оцінка поточного стану

| Категорія | Оцінка | Обґрунтування |
|---|---:|---|
| Native .NET integration | 10/10 | Managed, multi-target, zero runtime dependencies |
| API/architecture discipline | 9/10 | Baseline, layering tests, runnable docs examples і узгоджений релізний тег |
| Correctness contracts | 9/10 | Positive proof acceptance, typed budgets, 1035-case gate; немає external formal audit |
| Small exact minimization | 9/10 | Proven SOP/POS minimum в guarantee zone |
| Large two-level minimization | 6/10 | Реальна SAT/Espresso-lite routing, але не Espresso-scale validation |
| Multi-level synthesis | 5/10 | Справжній AIG rewrite, але без mapping/sequential flow |
| SAT engine | 6/10 | Повноцінний CDCL feature set; не competition-grade performance claim |
| BDD engine | 7/10 | Complement edges, operations, true sifting; менше CUDD |
| d-DNNF / model counting | 7/10 | Реальний compiler + #SAT/WMC/enumeration; вузький API та молода реалізація |
| Documentation/reproducibility | 9/10 | Runnable docs, benchmark corpus, чесні status contracts |
| Ecosystem/maturity | 4/10 | Немає багаторічного adoption, зовнішніх benchmarks і широкої інтеграційної бази |

Зведена оцінка — приблизно **7.5/10 як інтегрований managed propositional
toolkit**, але вона не переноситься на окремі спеціалізовані домени. Як raw SAT,
EDA synthesis або SMT продукт бібліотека суттєво поступається відповідним
лідерам.

---

## 8. Ранжування бібліотек

### Загальний рейтинг для integrated propositional toolkit

Цей рейтинг відповідає конкретному питанню:

> Наскільки продукт придатний як єдиний toolkit для побудови, перетворення,
> мінімізації, розв'язання й аналізу пропозиційних Boolean formulas?

Ваги: breadth та інтеграція — 25% · correctness/maturity — 20% ·
мінімізація — 15% · SAT/optimization — 15% · BDD/d-DNNF/model counting — 15% ·
deployment, API й документація — 10%.

| Місце | Бібліотека / стек | Зважена оцінка | Чому тут |
|---:|---|---:|---|
| 1 | **LogicNG** | 8.8/10 | Найповніший зрілий propositional framework: formula factory, кілька SAT/MaxSAT engines, encodings, BDD, d-DNNF і production history |
| 2 | **LogicalOptimizer v3.0.0** | 8.2/10 | Найсильніше поєднання breadth, explainable minimization і zero-dependency managed .NET; поступається LogicNG зрілістю та глибиною engines |
| 3 | **Z3** | 7.8/10 | Найпотужніший solver і найширша теорійна база, але не спеціалізований toolkit для SOP/POS, BDD/d-DNNF та читабельної Boolean optimization |
| 4 | **PyEDA** | 6.9/10 | Добре інтегрує Python expressions, truth tables, Espresso і BDD; слабший SAT/proof/resource-contract stack |
| 5 | **CaDiCaL/Kissat + PySAT** | 6.8/10 | Дуже сильний SAT/encoding ecosystem, але symbolic rewriting, BDD/d-DNNF та expression minimization потребують інших компонентів |
| 6 | **SymPy** | 6.7/10 | Сильна symbolic логіка, exact SOP/POS/ANF і великий CAS ecosystem; слабше масштабується як спеціалізований Boolean toolkit |
| 7 | **ABC** | 6.6/10 | Беззаперечний лідер logic synthesis, але його scope — circuits/netlists, а не ergonomic general-purpose formula toolkit |
| 8 | **CUDD / dd** | 6.2/10 | Найсильніший спеціалізований decision-diagram stack, але вузький поза BDD/ADD/ZDD задачами |
| 9 | **Espresso** | 5.5/10 | Еталонна велика two-level мінімізація, проте це спеціалізований minimizer, а не інтегрований toolkit |

Оцінки округлені й експертні: вони спираються на верифіковану feature matrix,
але не є результатом єдиного performance benchmark. Зміна ваг змінює порядок:
якщо SAT throughput має вагу 60%, переможуть CaDiCaL/Kissat або Z3; якщо
technology mapping — ABC; якщо BDD scale — CUDD.

### Рейтинг за сценаріями

| Сценарій | 1 місце | 2 місце | 3 місце | Позиція LogicalOptimizer |
|---|---|---|---|---|
| Embedded managed .NET Boolean toolkit | **LogicalOptimizer** | Z3 .NET | інші потребують JVM/Python/native integration | **1** |
| Загальний propositional framework | **LogicNG** | **LogicalOptimizer** | PyEDA | **2** |
| Explainable optimization + proof status | **LogicalOptimizer** | LogicNG | SymPy | **1** |
| Exact мала SOP/POS | **LogicalOptimizer / SymPy** | LogicNG | PyEDA | **1–2, залежно від API та cost model** |
| Велика heuristic two-level мінімізація | **Espresso** | PyEDA | **LogicalOptimizer** | **3** |
| Multi-output PLA мінімізація | **Espresso** | ABC / PyEDA | **LogicalOptimizer** | **3–4** |
| Raw SAT performance | **Kissat/CaDiCaL** | Z3 | LogicNG | **поза топ-3** |
| Incremental SAT toolkit | **CaDiCaL/PySAT** | LogicNG / Z3 | **LogicalOptimizer** | **3–4** |
| SMT та theory solving | **Z3** | — | — | **не конкурує** |
| BDD scale та reorder depth | **CUDD/dd** | LogicNG | **LogicalOptimizer** | **3** |
| d-DNNF knowledge compilation | **LogicNG** | **LogicalOptimizer** | — серед порівнюваних | **2** |
| Logic synthesis / technology mapping | **ABC** | **LogicalOptimizer** лише як bounded rewrite | PyEDA | **2 за наявністю AIG rewrite, але з великим gap** |
| Dependency-free CLI для Boolean formulas | **LogicalOptimizer** | Espresso / ABC | Z3 | **1** |

### Практичний висновок із ранжування

- Для нового **чистого .NET-проєкту**, якому потрібні parsing, optimization,
  equivalence proof, SAT, BDD або d-DNNF без native deployment,
  LogicalOptimizer — **перший вибір серед порівнюваних продуктів**.
- Для JVM-конфігуратора з максимально зрілим propositional ecosystem —
  **LogicNG**.
- Для SMT — **Z3**; для великих SAT instances — **CaDiCaL/Kissat**; для
  synthesis — **ABC**; для великих BDD — **CUDD**; для PLA — **Espresso**.
- Тому загальне друге місце LogicalOptimizer означає сильну інтеграцію й
  збалансованість, а не перевагу над кожним спеціалізованим engine.

---

## 9. Підсумок і рекомендоване позиціонування

Найточніше позиціонування релізу v3.0.0:

> **LogicalOptimizer — широкий dependency-free managed .NET toolkit для
> пропозиційної логіки:** canonical AST і parser, equivalence-verified symbolic
> optimization, proven small-zone SOP/POS minimization, scalable heuristic
> routing, власний incremental CDCL SAT, ROBDD, d-DNNF knowledge compilation,
> cardinality/PB/MaxSAT, bounded AIG DAG rewriting, exporters і CLI.

Сильна сторона — не світовий рекорд одного engine, а рідкісне поєднання:

1. pure managed .NET без runtime dependencies;
2. end-to-end API від текстової формули до оптимізації, доказу й export;
3. явні proof/budget/unknown contracts;
4. SAT + BDD + d-DNNF в одному модульному стеку;
5. exact small-zone та practical fallback zones;
6. runnable documentation і pinned public API.

Необхідно уникати тверджень:

- «найшвидший SAT solver»;
- «заміна Z3»;
- «паритет з ABC/CUDD/Espresso»;
- «глобально найкращий boolean optimizer»;
- «усі пакети v3.0.0 доступні в NuGet», доки це не підтверджено Gallery або
  успішним post-publish verification workflow.

Найбільш обґрунтований differentiator:

> **Один self-contained .NET stack для explainable, equivalence-verified Boolean
> optimization і knowledge compilation, з чесним статусом мінімальності та
> контрольованими ресурсними fallback.**

---

## 10. Джерела

### Внутрішні, код-верифіковано

- [`CHANGELOG.md`](CHANGELOG.md) — повний склад релізу v3.0.0.
- [`README.md`](README.md) — packages, capabilities, limits, test snapshot.
- [`doc/BENCHMARKS.md`](doc/BENCHMARKS.md) — benchmark methodology і таблиці.
- [`LogicalOptimizer.Tests/Techniques/ArchitectureTests.cs`](LogicalOptimizer.Tests/Techniques/ArchitectureTests.cs)
  — layering та 58-type public surface.
- [`LogicalOptimizer.Tests/TestData/PublicApi.approved.txt`](LogicalOptimizer.Tests/TestData/PublicApi.approved.txt)
  — member-level API baseline.
- `LogicalOptimizer.Core/AigRewriter.cs`, `AigRewriteLibrary.cs`,
  `AigMinLibraryData.cs`, `NpnCanonicalizer.cs` — AIG pipeline.
- `LogicalOptimizer.Sat/SatSolver.cs`, `CardinalityEncoder.cs`,
  `MaxSatSolver.cs`, `TseitinConverter.cs` — SAT stack.
- `LogicalOptimizer.Bdd/BinaryDecisionDiagram.cs` — ROBDD.
- `LogicalOptimizer.Dnnf/DnnfCompiler.cs`, `DnnfCircuit.cs` — d-DNNF.
- `LogicalOptimizer.Minimization/TruthTableMinimizer.cs`,
  `SatTwoLevelMinimizer.cs`, `EspressoLiteMinimizer.cs` — zone engines.

### Зовнішні першоджерела

- [LogicNG formula factory](https://logicng.org/documentation/formula-factory/)
- [LogicNG SAT solving](https://logicng.org/documentation/solvers/sat-solving/)
- [LogicNG MaxSAT](https://logicng.org/documentation/solvers/maxsat-solving/)
- [LogicNG knowledge compilation](https://logicng.org/documentation/knowledge-compilation/)
- [LogicNG d-DNNF](https://logicng.org/documentation/knowledge-compilation/dnnf/)
- [Z3 Guide: optimization](https://microsoft.github.io/z3guide/docs/optimization/arithmeticaloptimization/)
- [CaDiCaL official repository](https://github.com/arminbiere/cadical)
- [Kissat official repository](https://github.com/arminbiere/kissat)
- [Berkeley Espresso manual](https://people.eecs.berkeley.edu/~alanmi/research/espresso/espresso_5.html)
- [Berkeley ABC official repository](https://github.com/berkeley-abc/abc)
- [CUDD official repository](https://github.com/cuddorg/cudd)
- [SymPy logic documentation](https://docs.sympy.org/latest/modules/logic.html)
- [PyEDA documentation](https://pyeda.readthedocs.io/en/latest/)
