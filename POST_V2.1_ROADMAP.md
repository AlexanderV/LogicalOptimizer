# План розвитку LogicalOptimizer після v2.1 (Tier 1–Tier 3)

Дата: 28 липня 2026 року. Базова точка — тег **v2.1.0** (`c36e9b5`).

Цей документ деталізує повну розробку шести напрямів із розбору
[VALIDATED_LIBRARY_COMPARISON_2026-07-28.md](VALIDATED_LIBRARY_COMPARISON_2026-07-28.md),
впорядкованих **від найкориснішого**. Він продовжує та уточнює відкриті треки
[POST_V2_ROADMAP.md](POST_V2_ROADMAP.md) (Phase C2, D1, D2/D3) і B2.

Кожен трек описано код-прив'язано: пакет, публічний API, алгоритм, файли, тести,
що саме треба оновити в pinning-тестах, ризики, оцінка обсягу.

---

## 0. Незмінні рамки (стосуються всіх треків)

- **Нуль production/runtime залежностей.** Будь-який трек, що тягне native/сторонню
  залежність у core — поза планом (SMT, справжній Espresso C-ext, CUDD).
- **Стратегія** ([LEADERSHIP_ROADMAP.md](LEADERSHIP_ROADMAP.md)): «краще за всіх у ніші,
  достатньо в суміжних». Мета — підняти слабкі категорії, не гнатися за паритетом зі
  спеціалізованими лідерами.
- **Версія береться з git-тегу** (`RELEASING.md:36-38`); `<Version>` у csproj — dev-fallback,
  що бампиться перед тегом у всіх пакувальних csproj.
- **Кожен новий публічний тип/метод/пакет ЗАВЖДИ синхронно оновлює** (інакше падають тести):
  - `LogicalOptimizer.Tests/TestData/PublicApi.approved.txt` (member-level baseline);
  - `ArchitectureTests.PublicSurface_IsTheDocumentedSet` — `HashSet expected` з лічильниками
    по пакетах (`ArchitectureTests.cs:124-145`);
  - `ArchitectureTests.PackageLayering_IsAcyclicAndPointsDownward` — `allowed` мапа залежностей
    (`:92-103`) — для нового пакета;
  - `ArchitectureTests.LibraryAssemblies` (`:18-25`) — для нового пакета;
  - `ArchitectureTests.PublicStaticEngineClasses_TakeCancellationOnExpensiveEntryPoints` — список
    `expensive` (`:165-175`) — для будь-якого 2ⁿ/довгого entry point;
  - `CHANGELOG.md`, `README.md`, `docs-site/`, `RELEASING.md` чекліст, `ci.yml` Pack-крок,
    `release.yml` push-крок (для нового пакета).

### Реліз-мапа (рекомендована; детальніше — §7)

| Версія | Вміст | Ризик | Новий пакет |
|---|---|---|---|
| **v2.2.0** | Трек 4 (`--dnf` таблиця) + Трек 5 (ANF) + Трек 6 (NuGet-верифікація) | низький | — |
| **v2.3.0** | **Трек 1 — DNNF** (флагман) | середній | `LogicalOptimizer.Dnnf` |
| **v2.4.0** | Трек 3 — in-place BDD sifting | середній | — |
| **v2.5.0 → v3.0.0** | Трек 2 — AIG cut-based rewriting (поетапно) | високий | — |

> Порядок у §1–§6 — **за корисністю** (як просив запит). Порядок релізів у таблиці —
> **прагматичний** (дешеве й безпечне раніше, флагман другим). Обґрунтування — §7.

---

## 1. DNNF — компіляція знань (Tier 1, найкорисніше) · Phase D3

### Мета й цінність
Єдиний реальний feature-gap проти найближчого аналога LogicNG. Дає **сімейство**
спроможностей одним артефактом: exact `#SAT` (модельний підрахунок за один прохід),
weighted model counting, швидка енумерація, consistency/clause-entailment. Повністю managed,
лягає в нішу «пояснюваність + доведена коректність». Піднімає рядок «Model counting» і закриває
«DNNF — у нас немає».

### Розташування
**Новий пакет `LogicalOptimizer.Dnnf` → Core, Sat** (паралельно до `Bdd`; та сама
модель — один-два публічні типи на пакет). Компілятор споживає **публічний** `TseitinCnf`
(`TseitinConverter.cs:240`) і власну легку unit-propagation — тому НЕ потребує приватних
internals `SatSolver` і не порушує шарування. (Альтернатива — покласти в `Sat` заради
reuse trail/`Propagate` — відкинута: CDCL заточений під satisfiability, а top-down компіляція
потребує іншого пошуку з декомпозицією та кешуванням компонент; чистіше — власний компактний
цикл.)

### Публічний API (2 типи в новому пакеті)
```csharp
namespace LogicalOptimizer;

public sealed class DnnfCircuit
{
    public IReadOnlyList<string> Variables { get; }
    public int NodeCount { get; }
    public bool IsSatisfiable { get; }
    public System.Numerics.BigInteger CountModels();                 // #SAT
    public double WeightedModelCount(
        IReadOnlyDictionary<string, (double positive, double negative)> weights);
    public IEnumerable<IReadOnlyDictionary<string, bool>> EnumerateModels(
        CancellationToken cancellationToken = default);
}

public static class KnowledgeCompilation
{
    public static DnnfCircuit CompileToDnnf(
        AstNode formula, int nodeBudget = 1_000_000, CancellationToken cancellationToken = default);
}
```
Фасад re-exposes через зручний хелпер (напр. `FormulaAnalysis.CompileToDnnf` делегує), щоб
користувач не мусив підключати пакет напряму — але канонічні типи живуть у `Dnnf`.

### Алгоритм (top-down decision-DNNF, стиль c2d/D4)
1. `TseitinConverter.Convert(formula)` → `TseitinCnf`. Tseitin equisatisfiable й **equicount**
   на вхідних змінних (кожна вхідна модель ⇒ рівно одна aux-присвойка), тож `#models` повної
   CNF = `#models` формули на входах. Це знімає проблему проекції.
2. Рекурсивна компіляція залишкової CNF:
   - **Unit propagation** → набір імплікованих літералів (кон'юнкт `And` літерал-вузлів);
   - **Декомпозиція**: розбити залишкові клаузи на зв'язні компоненти за спільними змінними →
     **decomposable AND** (компоненти компілюються незалежно);
   - **Рішення**: обрати змінну, розгалузити `v` / `¬v` → **deterministic OR** (гілки взаємно
     виключні за `v`).
3. **Кешування компонент**: результат компіляції компоненти кешується за ключем (нормалізований
   набір активних клауз/змінних). Це джерело експоненційної економії — обов'язкове.
4. **Представлення**: DAG у пласких масивах — `Literal`, `And` (decomposable), `Or` (deterministic,
   зі змінною рішення). Компактно, hash-consed.
5. **Підрахунок** (bottom-up із memo): literal→1, decomposable-AND→добуток, deterministic-OR→сума,
   з домноженням `2^(gap)` на неназвані змінні (smoothing/облік scope). `BigInteger`.
6. **Weighted count**: та сама рекурсія з вагами літералів (double).
7. **Budget + CancellationToken**: перевірка `nodeBudget` при кожному створенні вузла;
   `ThrowIfCancellationRequested` у циклі пошуку (як у `TruthTableMinimizer.CoverSearch`).

### Файли
- Новий: `LogicalOptimizer.Dnnf/LogicalOptimizer.Dnnf.csproj` (net8.0;net10.0, `<Version>`,
  `InternalsVisibleTo` для Tests; ProjectReference Core+Sat).
- Новий: `LogicalOptimizer.Dnnf/DnnfCircuit.cs`, `DnnfCompiler.cs` (internal),
  `DnnfNode.cs` (internal struct), `KnowledgeCompilation.cs`.
- Правки: `LogicalOptimizer/LogicalOptimizer.csproj` (+ProjectReference Dnnf);
  `FormulaAnalysis.cs` (делегат-хелпер, опційно); `ci.yml` (+pack Dnnf), `release.yml` (+push Dnnf);
  `LogicalOptimizer.sln`.

### Тести (лягають у наявний диференціальний/property-стек)
- **Differential vs BDD**: `DnnfCircuit.CountModels()` == `BinaryDecisionDiagram.
  BuildWithBestOrder(f).CountSatisfyingAssignments()` для випадкового корпусу — обидва exact,
  мусять збігатися точно.
- **Differential vs brute force** (≤16 змінних): проти повного перебору `TruthTable`.
- **Property**: `count(f) + count(¬f) = 2^n`; `count(f ∧ ¬f)=0`; `count(⊤)=2^n`.
- **Enumeration**: множина `EnumerateModels()` == множина `FormulaAnalysis.EnumerateModels(f)`.
- **Weighted**: усі ваги 1.0 ⇒ дорівнює `CountModels()`; ручні дрібні приклади.
- **Budget/cancel**: перевищення `nodeBudget` кидає; pre-cancelled token кидає одразу.
- Оновити `doc/TESTING.md` Part 4 (новий диференціальний оракул — власний BDD).

### Pinning-оновлення
`PublicApi.approved.txt` (+`DnnfCircuit`, `KnowledgeCompilation`); `PublicSurface` expected —
нова група «Dnnf» (2 типи), лічильник; `PackageLayering.allowed` (+`Dnnf`→Core,Sat);
`LibraryAssemblies` (6 бібліотек); `expensive` (+`CompileToDnnf`).

### Ризики
- Коректність smoothing/gap при підрахунку — головна пастка → жорсткий differential-гейт проти BDD.
- Експоненційний blow-up на «важких» CNF → budget+cancel обов'язкові; документувати як heuristic-limit,
  а не гарантію.

### Обсяг: **XL** (флагман, ~1.5–2.5 тижні). Найбільший user-visible приріст сили в ніші.

---

## 2. AIG cut-based rewriting (Tier 2, найвища стеля) · Phase D1

### Мета й цінність
Категорія «Multi-level synthesis» = **2/5** (найслабша). `AndInverterGraph` уже є базою
(hash-consed 2-input AND DAG, complement-edge літерали `node<<1|comp`, `AndNodeCount`,
`FromAst`/`ToAst`, `Cleanup`), але використовується лише для метрик у тестах/бенчмарках. DAG-aware
rewriting (стиль ABC `rewrite`) робить багаторівневу оптимізацію реальною — чого немає в жодного
managed-.NET-конкурента.

### Розташування
`LogicalOptimizer.Core/AndInverterGraph.cs` лишається **internal**; уся нова машинерія — internal
поруч. Інтеграція у фасад — як **додатковий кандидат** у `SelectCheapest` для великих виразів
(не заміна дефолту), під обов'язковим equivalence-гейтом. Публічної поверхні спочатку **не додаємо**
(мінімум API-churn); можливо пізніше — публічна метрика `AndNodeCount`.

### Чого бракує в базі (треба додати)
Розбір коду: немає fanout/reference counts, немає node→parents індексу, немає cut-enumeration,
немає видалення вузлів (лише whole-graph `Cleanup` rebuild). Це і є нова робота.

### Алгоритм (поетапно — три під-треки)
- **D1a — інфраструктура**: reference counting на вузол; MFFC (maximum fanout-free cone) обчислення
  через deref/ref; node-level bookkeeping. Тести: ref-count інваріанти, MFFC-розмір.
- **D1b — cut enumeration + NPN-бібліотека**: k-feasible cut enumeration (k=4) bottom-up (мерж
  cut-ів дітей, прибирання домінованих, ≤k входів); для кожного 4-входового cut — симуляція
  16-бітної truth table, канонізація до NPN-класу, таблиця оптимальних AIG-структур. Розширити
  наявний `SubcircuitLibrary` (нині ≤3-змінних, 256 функцій) до 4-входових NPN-класів.
  Тести: коректність NPN-канонізації та бібліотеки (кожен запис ⇔ своя функція).
- **D1c — застосування + інтеграція**: для кожного вузла × cut рахувати gain = `MFFC_розмір −
  розмір_заміни`; застосовувати при gain ≥ 0 (опційно zero-cost для майбутніх виграшів); deref
  старого конуса, build заміни з бібліотеки, ref, оновлення hash-table. Ітерувати проходи до
  відсутності gain або budget. Вбудувати як кандидат у фасад під `EquivalenceChecker`-гейтом.

### Файли
- Правки: `LogicalOptimizer.Core/AndInverterGraph.cs` (ref-count, deref/ref, delete, cut API);
  `LogicalOptimizer.Core/SubcircuitLibrary.cs` (розширення до 4-input NPN);
  нові internal: `AigCutEnumerator.cs`, `AigRewriter.cs`, `NpnCanonicalizer.cs`.
- Інтеграція: `LogicalOptimizer/BooleanExpressionOptimizer.cs` (`SelectCheapest` — новий кандидат
  для великих виразів), під `EquivalenceChecker`.

### Тести
- **Equivalence invariant** (miter+SAT `EquivalenceChecker`) на кожному проході — святе.
- **Node-count non-increasing** після rewrite; differential vs BDD; characterization golden master
  на фіксованому корпусі; property (rewrite ідемпотентний до fixpoint).
- Perf-бенчмарк `AndNodeCount` до/після на 40-змінному покритті (`Benchmarks/Program.cs:152`).

### Ризики (найвищі в плані)
- Коректність NPN-канонізації + бібліотеки; DAG-bookkeeping баги (deref/ref); перф.
  Мітигація: строгий equivalence-гейт, застосування лише коли зменшує cost, поетапність D1a→b→c,
  спершу internal-кандидат (не дефолт) — тому реліз не breaking, доки не стане дефолтом.

### Обсяг: **XL, багаторелізний** (~3–5 тижнів сумарно). Найбільша стеля, найбільший ризик.

---

## 3. In-place swap BDD sifting (Tier 2, перф/паритет) · Phase C2

### Мета й цінність
Нині `BuildWithSiftedOrder` — **rebuild-based** (`BinaryDecisionDiagram.cs:487`, будує нову діаграму
з нуля для кожної пробної позиції). Це задокументована поступка проти CUDD. Adjacent-level swap дає
справжній Rudell-sifting без повних перебудов: реальний perf-приріст і паритетна фіча.

### Розташування
Один файл `LogicalOptimizer.Bdd/BinaryDecisionDiagram.cs`. **Публічна сигнатура
`BuildWithSiftedOrder` не змінюється** (стає in-place всередині) — усі виклики
(`Benchmarks/Program.cs:140`, `BddOperationsTests.cs`, `ArchitectureTests.cs:173-174`) лишаються.

### Що треба перебудувати
Нині вузли — плаский append-only `List<(Variable,Low,High)>` (`:39`), рівні лише неявні через
`.Variable`; unique-table за `(var,low,high)`; окремий `_iteCache`; порядок фіксований при
конструюванні. In-place swap потребує:
1. **Level-indexed bookkeeping** — `List<int>[] _nodesAtLevel` поверх плаского списку.
2. **`SwapAdjacentLevels(level)`**: для кожного вузла рівня `level`, що посилається на `level+1`,
   застосувати swap-перезапис Шеннона (`f = ite(x, ite(y,f11,f10), ite(y,f01,f00))` → переставити
   `x`↔`y`), **rehash лише двох задіяних рівнів**, очистити `_iteCache`, свопнути
   `_variables`/`_variableIndex`.
3. **Критично — complement edges**: зберегти stored-invariant «THEN/high edge завжди regular»
   (`MakeNode:595`) наскрізь через swap — головна тонкість.
4. **Sifting-драйвер**: рухати кожну змінну вгору/вниз усіма рівнями через adjacent-swaps, трекати
   `NodeCount`, повертати в найкращу позицію. Budget + `CancellationToken`.

### Тести
- **Function-preserving invariant**: після серії свопів `Evaluate` та `CountSatisfyingAssignments`
  незмінні (це головний гейт коректності).
- Canonical-invariant збережено (complement-edge тест уже є).
- `NodeCount` ≤ результат rebuild-based на корпусі; perf-бенчмарк показує прискорення; cancel/budget.

### Ризики
- Complement-invariant під час свопу та `_iteCache` invalidation — середньо-високо, але **добре
  локалізовано** (один файл, поведінка публічного методу збережена).

### Обсяг: **M** (~4–6 днів). Контейнеризований, без API-churn.

---

## 4. Apples-to-apples `--dnf` таблиця (Tier 3, дешева довіра) · Phase B2

### Мета й цінність
`doc/BENCHMARKS.md:138-146` сам визнає: поточна таблиця порівнює наш **multi-level** output проти
**two-level** SymPy/PyEDA, тож «**не доводить**», що наш SOP-мінімізатор кращий. Python-бік уже
two-level (`simplify_logic(form="dnf")` `:110`; `espresso_tts` `:157`). Виправлення — **цілком на
C#-боці**: додати чесну two-level таблицю. Дешево, закриває credibility-gap, потенційно сильне
маркетингове твердження.

### Зміни (`LogicalOptimizer.Benchmarks/ComparisonHarness.cs`)
1. Розпізнати `--dnf` серед `args`; при ньому опції `{ComputeCnf=false, ComputeDnf=true,
   ComputeAdvancedForms=false}` (нині обидва false, `:47-48`).
2. Рахувати літерали **`result.DNF`** замість `result.Optimized` (`:73`). `result.DNF` — це
   two-level SOP (`TruthTableMinimizer.MinimalSopWithStatus`, `BooleanExpressionOptimizer.cs:93`,
   або SAT-cover `:157`, або espresso-lite `:174-188`).
3. Прокинути прапорець із `Program.cs:8-9` (`-- compare --dnf`); перейменувати колонку заголовка.

### Файли
`ComparisonHarness.cs`, `Benchmarks/Program.cs`; `ci.yml:53-60` (+другий виклик `-- compare --dnf`);
`doc/BENCHMARKS.md` (додати two-level таблицю поряд із multi-level, оновити прозу `:138-146`);
`docs-site/articles/benchmarks.md` (дзеркалити).

### Тести
Юніт: для малих функцій корпусу літерали `MinimalSop` == очікувані; harness друкує DNF-розміри.
Низький ризик.

### Обсяг: **S** (~1–2 дні).

---

## 5. ANF / поліном Жегалкіна (Tier 3, мала нова спроможність)

### Мета й цінність
SymPy має ANF (Reed–Muller), у нас — ні (grep: лише `XorNode` AST, жодного конвертера). Дешевий
новий normal form (XOR-of-AND монdomіали), корисний для крипто/XOR-важких функцій; природно лягає
до наявних `XorNode`/`FormulaFactory` й експортерів.

### Розташування й API
Möbius-трансформа над truth table (2ⁿ, ≤20 змінних — межа `TruthTable`), збірка результату через
`FormulaFactory` (XOR/AND). `NormalFormConverter` — internal, тож публічний вхід додаємо **методом
до наявного публічного фасадного типу** (щоб не плодити тип): напр.
```csharp
public static AstNode ToAlgebraicNormalForm(AstNode formula, CancellationToken ct = default);
```
на публічному класі у фасаді (`Transformations` або новий `NormalForms`). CLI-прапорець `--anf`
у `CommandLineProcessor.cs` (поряд із `--dnf`, `:70-71`) + вивід у `OutputFormatter`.

### Алгоритм
1. Побудувати truth table формули (`TruthTable`, ≤20 змінних).
2. **Möbius-трансформа** (fast, in-place, 2ⁿ) → коефіцієнти монomіалів ANF.
3. Зібрати `⊕` присутніх монomіалів (`AND` змінних) через `FormulaFactory`; порожній ⇒ константа.

### Тести
- **Differential vs brute force**: `Evaluate(ANF)` == `Evaluate(original)` на всіх присвойках (мале n).
- **Property**: Möbius — власна інверсія (round-trip); ANF XOR-важких функцій компактний; `x⊕x=0`.
- Cancellation (2ⁿ ⇒ у `expensive`-списку).

### Pinning-оновлення
`PublicApi.approved.txt` (+метод); `expensive` список (+`ToAlgebraicNormalForm`). Якщо новий тип —
`PublicSurface` count; якщо метод на наявному типі — лише approved.txt.

### Обсяг: **S–M** (~2–3 дні).

---

## 6. Верифікація присутності в NuGet (Tier 3, ops)

### Мета
Нині присутності в реєстрі ніхто не перевіряє автоматично (`RELEASING.md:30-32` — ручний крок;
`ci.yml:62-70` — лише packability). Закрити «adoption/Gallery не підтверджено» з §5 порівняння.

### Зміни
Крок у `release.yml` **після** push або окремий `tools/verify_nuget.ps1`: запит
`https://api.nuget.org/v3-flatcontainer/<id>/index.json` для всіх пакетів на випущену версію;
retry з backoff (індексація має лаг), advisory-фейл після N спроб. Список ID = 6 (7 із `Dnnf`).

### Файли
`.github/workflows/release.yml` (новий крок) або `tools/verify_nuget.ps1`; згадка в `RELEASING.md`.

### Обсяг: **S** (~0.5 дня).

---

## 7. Рекомендована послідовність виконання

Порядок §1–§6 — **за корисністю**. Але «найкорисніше» ≠ «робити першим»: DNNF і AIG — найбільші
та найризикованіші. Прагматична послідовність релізів:

1. **v2.2.0 — «дешева довіра + мала спроможність»**: Трек 4 (`--dnf`) + Трек 5 (ANF) + Трек 6
   (NuGet-верифікація). Без нового пакета, без ризикованої хірургії, без API-churn у ядрі. Швидкий
   реліз, що одразу закриває credibility-gap і додає ANF. **Робити першим.**
2. **v2.3.0 — DNNF** (Трек 1). Флагман: новий пакет `LogicalOptimizer.Dnnf`, найбільший
   user-visible приріст сили, закриває останній gap проти LogicNG.
3. **v2.4.0 — in-place BDD sifting** (Трек 3). Перф/паритет, локалізовано, без API-churn.
4. **v2.5.0 → v3.0.0 — AIG cut-based rewriting** (Трек 2, поетапно D1a→D1b→D1c). Спершу
   internal-кандидат у фасаді (non-breaking) → коли стане дефолтним output-ом, це **major** (v3.0.0).

### Перед кожним тегом (з `RELEASING.md`)
Бамп `<Version>` у всіх пакувальних csproj → запис у `CHANGELOG.md` → push `main` → анотований тег
`vX.Y.Z` → `release.yml` пакує/пушить. Для v2.3.0 додатково: 7-й пакет у `ci.yml` Pack і
`release.yml` push, оновити `PackageLayering`/`LibraryAssemblies`/`PublicSurface`.

### Зведення обсягів

| Трек | Корисність | Обсяг | Ризик | Реліз |
|---|---|---|---|---|
| 1. DNNF | найвища (нова спроможність) | XL | середній | v2.3.0 |
| 2. AIG rewriting | висока (найслабша категорія) | XL, багаторелізний | високий | v2.5→v3.0 |
| 3. In-place sifting | середня (перф/паритет) | M | середній | v2.4.0 |
| 4. `--dnf` таблиця | висока ROI (довіра) | S | низький | v2.2.0 |
| 5. ANF | середня (мала спроможність) | S–M | низький | v2.2.0 |
| 6. NuGet-верифікація | ops | S | низький | v2.2.0 |

### Чого план свідомо НЕ включає
SMT-теорії (Z3), справжній Espresso C-ext, ADD/ZDD, промисловий PLA-масштаб, пікова raw-SAT-перф —
принципові non-goals за [LEADERSHIP_ROADMAP.md](LEADERSHIP_ROADMAP.md): зміна ніші, а не прокачка.
Опційний `LogicalOptimizer.Z3`-адаптер (Phase D2/T7) лишається on-demand поза цим планом.
