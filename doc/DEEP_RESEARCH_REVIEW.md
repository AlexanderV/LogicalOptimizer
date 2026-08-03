# Глибокий технічний аудит LogicalOptimizer

Дата первинного аудиту: 2026-07-31; повні повторні аудити: 2026-08-03 (останній прогін — після повторного запиту користувача)  
Об'єкт: поточне робоче дерево гілки `main`, включно з незакоміченими змінами  
Формат: зауваження пріоритезовані за впливом; код під час аудиту не змінювався

## Резюме

Проєкт зберігає сильну correctness-основу. В останньому незалежному повторі Release build пройшов без warnings; поточний fast gate — **1382/1382**, targeted AOT-provenance+MetaPackage+ExternalSat — **36/36**, DocFX 2.78.5 — 0 warnings/errors. Exact CI/release `pack --no-build` sequence для всіх дев'яти package IDs, 152/152 contract checks, local consumer smoke та packaged Native AOT + SHA linkage пройшли; vulnerability audit чистий для всіх 19 solution-проєктів. `release.yml` має безпечний `workflow_dispatch` dry run усіх pre-publish gates. Нових findings не виявлено.

F-01–F-16 закриті. F-14 дозакрито спільним `Test-AotProvenance`: local AOT SHA механічно звіряється з `SHA256SUMS.txt`, nuget.org mode забороняє local hash, а 6 semantic fixtures покривають success і failure paths. Усі OE-01–OE-06 закриті.

Перед тегом `v4.0.0` лишається запустити й отримати зелений manual dry run `release.yml` на GitHub Actions; він включає pre-publish Native AOT/evidence на Linux runner і через `PUBLISH=false` не виконує незворотних операцій.

## Перевірки та фактичні результати

| Перевірка | Результат |
|---|---|
| `dotnet build LogicalOptimizer.sln -c Release --no-restore -warnaserror` | успішно, 0 warnings, 0 errors, ~7 s |
| `tools/test.ps1 -NoBuild` у Windows PowerShell | 1382 passed, 0 failed; повторні прогони 19–38 s залежно від навантаження |
| targeted `AotProvenanceContractTests|MetaPackageTests|ExternalSat` | 36 passed, 0 failed |
| `dotnet tool restore` + `dotnet docfx docs-site/docfx.json` | DocFX 2.78.5; 112 models; 0 warnings, 0 errors |
| exact CI/release `dotnet pack ... -c Release --no-build` ×9 | успішно; 9 `.nupkg` і 2 `.snupkg` |
| package contract над exact artifacts | 152/152 checks passed; усі 7 forwarding dependencies exact `[4.0.0]` |
| local install smoke, consolidated + 7 forwarding IDs | passed |
| packaged Native AOT (`win-x64`) + real AOT report/checksum linkage | passed; native binary 2,100,736 bytes; report SHA збігся з `SHA256SUMS.txt` |
| local CLI tool install/run/uninstall із exact package artifacts | passed; після cleanup global tool відсутній |
| parse усіх `tools/*.ps1` через PowerShell AST parser | 7/7 scripts без syntax errors |
| Windows PowerShell test entry point | працює; F-10 закрито документацією й smoke |
| повний `dotnet test` без фільтра | не завершився за 120 s; процес продовжував CPU-bound роботу |
| повтор з `--blame-hang --blame-hang-timeout 30s` | 1346 тестів пройшли; одночасно активні 5 exhaustive чотиризначних sweep-тестів; run примусово завершено діагностикою |
| `dotnet list ... package --vulnerable --include-transitive` | відомих вразливих залежностей не виявлено у всіх 19 solution-проєктах |
| packaged Native AOT publish (`win-x64`) | passed після додавання VS Installer до `PATH`; native consumer 2,100,736 bytes, runtime assertions passed |

Примітка: перша збірка після перерваного тестового запуску впала лише тому, що залишений `testhost` тримав DLL. Після завершення саме цього процесу повторна збірка стала зеленою. Це не дефект компіляції, але наслідок незручного безфільтрового тестового сценарію.

## Зауваження

| ID | Пріоритет | Статус повторної перевірки |
|---|---|---|
| F-01 | High | Закрито |
| F-02 | High | Закрито |
| F-03 | Medium | Закрито |
| F-04 | Medium | Переважно закрито; bare `dotnet test` свідомо лишається повним прогоном |
| F-05 | Medium | Закрито |
| F-06 | Medium | Закрито |
| F-07 | Medium | Закрито |
| F-08 | Low | Закрито |
| F-09 | Low | Закрито |
| F-10 | Low | Закрито |
| F-11 | Low | Закрито |
| F-12 | High | Закрито |
| F-13 | Medium | Закрито |
| F-14 | Medium | Закрито |
| F-15 | Low | Закрито |
| F-16 | Low | Закрито цією синхронізацією |

### F-01 — High — Закрито — `int.MinValue` обходив контракт контрольованої валідації external SAT API

**Де:** `LogicalOptimizer.Sat/IExternalSatSolver.cs:128-169`.

**Первинне спостереження:** `ExternalSatProblem` застосовував `Math.Abs(int)` до літералів до перевірки діапазону. Для `int.MinValue` .NET кидає `OverflowException`. Наслідки до виправлення:

- конструктор обіцяє відхиляти некоректний літерал як `ArgumentOutOfRangeException`, але повертає інший тип винятку;
- `IsSatisfiedBy` задокументований як булева перевірка недовіреної моделі, але зловмисний або дефектний адаптер може аварійно перервати її через `int.MinValue` замість отримання `false`;
- на trust boundary це перетворює невалідну відповідь solver-а на неконтрольований виняток.

**Виконана рекомендація:** перевіряти `literal == 0 || literal == int.MinValue` до модуля; додати тести для `int.MinValue` у clauses, assumptions і model. Для конструктора очікувати `ArgumentOutOfRangeException`, для `IsSatisfiedBy` — `false`.

**Повторна перевірка:** реалізовано ранню перевірку `int.MinValue`; додано regression-тести для clauses, assumptions і model. Усі 28 external-SAT тестів проходять.

### F-02 — High — Закрито — validated CNF залишався змінним після конструктора

**Де:** `LogicalOptimizer.Sat/IExternalSatSolver.cs:56-96`; публічні властивості `Clauses` та `Assumptions`.

**Первинне спостереження:** конструктор валідовував передані колекції, але зберігав ті самі посилання. Масиви clauses також не копіювалися. Після успішної валідації caller міг:

- вставити `0`, out-of-range literal або `int.MinValue`;
- змінити кількість clauses, через що заголовок `ToDimacs()` і фактичне тіло можуть бачити різний стан при конкурентній мутації;
- спричинити `IndexOutOfRangeException` у `IsSatisfiedBy`, оскільки внутрішній цикл вважає літерали вже перевіреними;
- змінити семантику задачі між передачею solver-у, перевіркою моделі й декодуванням результату.

Це TOCTOU-проблема саме на межі із зовнішнім компонентом. У проєкті вже є окремий тест `NaryNodeContractTests.Operands_AreDefensivelyCopied`, отже очікування immutable/value-like input узгоджується з наявним стилем API.

**Виконана рекомендація:** у конструкторі зробити snapshot зовнішнього списку, кожного `int[]` і assumptions; публічно віддавати read-only представлення. Додати mutation-after-construction та concurrent-read тести.

**Повторна перевірка:** конструктор копіює зовнішній список, кожен clause і assumptions; публічний clause view повертає копії. Додано mutation-after-construction і concurrent-read тести.

### F-03 — Medium — Закрито — модель `ExternalSatResult` не мала snapshot-семантики

**Де:** `LogicalOptimizer.Sat/IExternalSatSolver.cs:230-244`.

**Первинне спостереження:** `ExternalSatResult.Satisfiable` зберігав переданий `IReadOnlyList<int>` без копії. `IReadOnlyList` забороняє мутацію лише через цей інтерфейс, але джерелом може бути звичайний `List<int>` або масив. Адаптер міг змінювати модель конкурентно між `IsSatisfiedBy` та `DecodeCounterexample`.

**Виконана рекомендація:** копіювати модель під час створення result і додати тест, де вихідний масив змінюють після `Satisfiable(...)`.

**Повторна перевірка:** `Satisfiable(...)` копіює модель у read-only snapshot; mutation regression-тест проходить.

### F-04 — Medium — Переважно закрито — звичайний `dotnet test` мав пастку продуктивності та слабку діагностику

**Де:** щонайменше `OptimizerSoundnessTests`, `TruthTableMinimizerTests`, `AigMinLibraryTests`, дві реалізації `ProjectedModelCountingTests`; категорії `Exhaustive`/`ReleaseEvidence`.

**Первинне спостереження:** CI правильно виключав `Performance` та `Exhaustive`, однак стандартна команда з README запускала все. Під час аудиту п'ять exhaustive sweep-тестів над усіма 4-variable функціями працювали одночасно. Після 79 секунд уже пройшло 1346 тестів, але завершення не було; `--blame-hang` назвав усі п'ять активними. Перший timeout залишив `testhost`, який заблокував DLL і зробив наступну збірку хибно червоною.

**Виконані рекомендації:**

1. Додано `tools/test.ps1` для швидкого default-контуру.
2. Для exhaustive/release-evidence вимкнено паралельність колекцій xUnit.
3. У `README.md` та `doc/TESTING.md` швидку команду поставлено першою, а повну описано з очікуваним часом.
4. У GitHub Actions додано timeout і TRX/sequence upload при `always()`.

Це не доказ нескінченного циклу: наявні дані доводять погану default-ергономіку й конкуренцію CPU, а не функціональну помилку алгоритму.

**Повторна перевірка:** додано canonical fast-test script, документацію, послідовність expensive runs, `longRunningTestSeconds`, CI/release timeout, blame-hang і always-uploaded test artifacts. Окрема test assembly не є необхідною для закриття дефекту; bare `dotnet test` усе ще запускає все, але це тепер явно задокументовано.

### F-05 — Medium — Закрито — docs workflow не перебудовував сайт для всіх документованих пакетів

**Де:** `.github/workflows/docs.yml`, секція `on.push.paths`; `docs-site/docfx.json`, секція `metadata.src.files`.

Тригер реагує на Core, Sat, Bdd, Minimization і facade, але не на зміни в `LogicalOptimizer.Dnnf`, `LogicalOptimizer.Formats`, `LogicalOptimizer.Cli` чи `LogicalOptimizer.Full`. Крім того, DocFX metadata взагалі генерує API лише для п'яти проєктів, хоча solution публікує дев'ять NuGet-пакетів, а сайт має концептуальні статті про DNNF, formats/exporters і CLI.

Наслідок: зміна публічного API DNNF/Formats може не запустити deployment, а користувачі отримують нерівномірне API-покриття між пакетами.

**Рекомендація:** явно визначити, які пакети мають API reference. Якщо всі — додати відповідні `.csproj` до `docfx.json` і paths trigger. Якщо лише п'ять — пояснити це в `docs-site/api/index.md`, а для DNNF/Formats/CLI дати прямі API/README-посилання; у будь-якому разі додати їх каталоги до trigger, бо вони впливають на концептуальну документацію й приклади.

**Повторна перевірка:** DocFX metadata охоплює сім library packages; DNNF/Formats додані, а CLI та code-free Full meta-package явно виключені з API reference й описані концептуально. Усі чотири каталоги додані до workflow trigger. Локальний docs build зелений.

### F-06 — Medium — Закрито — інструмент DocFX не був зафіксований по версії

**Де:** `.github/workflows/docs.yml`, команда `dotnet tool install -g docfx`.

**Первинне спостереження:** кожен запуск встановлював поточну latest-версію. Сайт міг перестати збиратися або змінити HTML без жодної зміни репозиторію. Це суперечило сильній відтворюваності, яку проєкт уже забезпечує для package version, deterministic CI build та release evidence.

**Рекомендація:** додати local tool manifest (`.config/dotnet-tools.json`) із pinned DocFX version і використовувати `dotnet tool restore` + `dotnet docfx`; оновлювати версію окремим контрольованим PR.

**Повторна перевірка:** DocFX 2.78.5 зафіксовано в local tool manifest із `rollForward: false`; workflow використовує `dotnet tool restore` і `dotnet docfx`.

### F-07 — Medium — Закрито — GitHub Actions використовувалися за рухомими major-тегами

**Первинне місце:** workflows використовували `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`, `NuGet/login@v1` тощо.

Для release pipeline, який публікує пакети та provenance, major-тег залишає можливість непомітної зміни виконуваного action. OIDC зменшує ризик довгоживучих секретів, але не прибирає ризик supply-chain підміни самого workflow dependency.

**Рекомендація:** pin усіх third-party та GitHub actions на повний commit SHA, поруч залишити коментар із людською версією; автоматизувати контрольовані оновлення через Dependabot/Renovate.

**Повторна перевірка:** у всіх дев'яти workflows action references pinned на повні commit SHA з version comments; пошук рухомих `@v*`/branch tags порожній. Dependabot налаштовано для GitHub Actions.

### F-08 — Low — Закрито — була відсутня явна перевірка cancellation до побудови miter CNF

**Де:** `LogicalOptimizer/ExternalSatEquivalenceChecker.cs`, метод `Check`.

Token передається адаптеру, але до цього checker будує XOR AST і Tseitin CNF. Для вже скасованого token робота виконується даремно; якщо adapter ігнорує контракт, checker взагалі може повернути результат після cancellation.

**Рекомендація:** `cancellationToken.ThrowIfCancellationRequested()` на вході й після повернення adapter-а. Якщо Tseitin conversion для великих AST є суттєвою операцією, у перспективі додати cancellation-aware overload. Окремо протестувати pre-cancelled token із solver-ом, який token ігнорує.

**Повторна перевірка:** перевірки додані до побудови miter і після повернення adapter-а; regression-тести покривають pre-cancelled token та adapter, який ігнорує cancellation.

### F-09 — Low — Закрито — була відсутня автоматична перевірка залежностей у CI

**Де:** `.github/workflows/ci.yml`.

Одноразовий аудит не знайшов відомих вразливих NuGet-пакетів, але workflow не містить `dotnet list package --vulnerable --include-transitive` або еквівалентного регулярного контролю. Стан може змінитися без зміни коду.

**Рекомендація:** додати scheduled dependency audit і Dependabot для NuGet/GitHub Actions. Для PR gate слід зафіксувати політику щодо network failures, щоб збій advisory source не маскувався під чистий результат.

**Повторна перевірка:** додано weekly Dependabot для NuGet/GitHub Actions та scheduled/workflow-dispatch audit. Workflow явно падає і при знайденій вразливості, і при помилці самого advisory scan; audit log завжди завантажується.

### F-10 — Low — Закрито — canonical test command вимагав PowerShell 7 без fallback

**Де:** `README.md`, `doc/TESTING.md`; приклади `pwsh tools/test.ps1`.

У середовищі повторної перевірки доступний Windows PowerShell, але команда `pwsh` відсутня. Сам скрипт успішно працює як `& .\tools\test.ps1 -NoBuild`, тому проблема лише у задокументованій точці входу, а не в тестах.

**Рекомендація:** для Windows документувати `./tools/test.ps1` або `& .\tools\test.ps1`, а `pwsh` залишити як cross-platform варіант; або додати shell-neutral wrapper (`dotnet` target/скрипти для обох shell families).

**Повторна перевірка:** README і testing guide розділяють Windows PowerShell та cross-platform PowerShell 7; Windows PowerShell smoke зелений.

### F-11 — Low — Закрито — development plan не був синхронізований із завершеним аудитом і OE-рішеннями

**Де:** `doc/DEVELOPMENT_PLAN.md`.

**Первинне спостереження:** документ починався зі старого порядку F-01 → F-09, у проміжних критеріях називав 1374 fast tests і 26 external-SAT tests, не містив F-10 та не мав checklist/status для OE-01–OE-06.

**Рекомендація:** синхронізувати baseline до 1376/28; додати F-10; винести OE-01–OE-06 в окремий етап із owner/evidence/decision deadline. Для OE-01/OE-02 вимагати NuGet usage data до breaking package consolidation, для OE-03 — benchmark/compatibility evidence, для OE-05 — pre-publish design.

**Повторна перевірка:** baseline, F-10, owners/deadline і структурований OE-01–OE-06 backlog додані. Подальші помилки статусу v4 виділені окремо як F-16, а не перевідкривають первинний finding.

### F-12 — High — Закрито — CI/release consolidated pack сумісний із `--no-build`

**Де:** `LogicalOptimizer/LogicalOptimizer.csproj`, target `PackCompanionAssemblies`; `.github/workflows/ci.yml:97`; `.github/workflows/release.yml:84`.

Для `NoBuild=true` проєкт тепер встановлює `BuildProjectReferences=false`, тому `ResolveReferences` використовує вже зібрані outputs і не викликає заборонений Build. Повторено точну workflow-послідовність після Release build: усі дев'ять `dotnet pack ... --no-build` завершилися успішно, створено 9 `.nupkg` і 2 `.snupkg`; contract 152/152 та local install smoke пройшли над цими artifacts.

**Рекомендація:** або прибрати `--no-build` саме для consolidated package, або зробити pack target сумісним із уже зібраними outputs без виклику Build project references. Додати regression-команду, ідентичну workflow, до локального/CI contract test; після виправлення повторити всі дев'ять pack commands із чистої checkout/build послідовності.

### F-13 — Medium — Закрито — forwarding packages мають і перевіряють exact-version залежність

**Де:** `forwarding/*/*.nuspec`; `LogicalOptimizer.Full/LogicalOptimizer.Full.csproj`; `tools/verify_package_contract.ps1`, check `forwards-to-consolidated-package`; `MetaPackageTests`.

Усі сім forwarding nuspec-и, включно з `LogicalOptimizer.Full`, використовують `version="[$version$]"`. Contract checker вимагає саме `[Version]`, а уніфікований `MetaPackageTests` перевіряє `[$version$]`. Фактичні v4 artifacts містять `[4.0.0]`; contract 152/152 і consumer restore пройшли.

**Рекомендація:** визначити бажану політику явно. Якщо потрібна та сама версія — генерувати `[$version$]` для всіх семи shells, включно з Full, і навчити tests/script перевіряти range semantics. Якщо lower bound навмисний — змінити ADR, descriptions і назву check, додати upper-bound/major compatibility decision.

### F-14 — Medium — Закрито — provenance правдивий і SHA linkage механічно валідовано

**Де:** `tools/smoke_install.ps1:321-337`; `tools/AotProvenanceCheck.ps1`; `tools/build_evidence_bundle.ps1:178-191`; `AotProvenanceContractTests`; `doc/CLAIMS.md:169`.

Prose у smoke script, evidence bundle та `CLAIMS.md` правдиво розрізняє local packaged bytes і nuget.org. AOT JSON записує фактичне `source` та local `consolidatedPackageSha256`. `build_evidence_bundle.ps1` dot-source-ить `AotProvenanceCheck.ps1` і відмовляє при missing checksum, missing/null local hash, mismatch, local hash у published mode або невідомому source. `AotProvenanceContractTests` покриває шість відповідних semantic fixtures; цільовий прогін 6/6 зелений.

**Повторна перевірка:** реалізація відповідає рекомендації; F-14 закрито.

### F-15 — Low — Закрито — local smoke прибирає встановлений ним global CLI tool

**Де:** `tools/smoke_install.ps1:227-260`, cleanup `:346-349`.

Скрипт зберігає прапорець `$script:cliToolInstalled` і в `finally` best-effort видаляє global tool лише тоді, коли встановив його сам. Отже первинний витік стану усунуто. `--tool-path` усе ще був би простішою ізоляцією, але це improvement, а не незакрите порушення початкового критерію.

**Рекомендація:** використовувати `--tool-path` усередині `$workDir` і запускати binary звідти; це ізолює smoke та автоматично очищається existing finally block.

### F-16 — Low — Закрито — historical backlog відділено від поточного status evidence

**Де:** `doc/DEVELOPMENT_PLAN.md:115-128`, `:158-166`, `:185-198`.

Виявлений checklist явно позначено як historical/superseded, а наступний Етап 7 є поточним status evidence. F-12–F-16 та OE-01–OE-06 закриті за фактичними gates. Baselines розділено: 1377 — до злиття forwarding-тесту, 1376 — до provenance fixtures, поточний — 1382.

**Рекомендація:** після F-12–F-15 позначити superseded checklist items або закрити їх доказом, а не лишати unchecked під completed header; розділити historical і current baseline; додати exact workflow-command verification і не закривати OE-05 до узгодження post-push `-RequireAll` policy.

## Позитивні спостереження

- Release design перевіряє CHANGELOG, exact package bytes, packaged Native AOT, SHA linkage і precomputable evidence до publish; F-14 закрито.
- SAT trust model чесно документує асиметрію: SAT-модель перевіряється, UNSAT без proof certificate довіряється.
- Fast test gate широкий й фактично зелений; є differential checks із Z3/SymPy, fuzz/property/metamorphic, architecture, public API і schema contract tests.
- Збірка Release чиста: warnings-as-errors не виявили проблем.
- Поточний NuGet vulnerability audit чистий за даними `https://api.nuget.org/v3/index.json` на дату аудиту.
- Незакомічені файли користувача під час аудиту не перезаписувалися й не форматувалися.

## Мінімальний план виправлень

1. Перед тегом v4.0.0 вручну запустити `release.yml` через `workflow_dispatch` і перевірити завантажений `dry-run-evidence-4.0.0`.
2. Лише після зеленого dry run створювати/пушити тег, який перемикає `PUBLISH=true`.

## Межі аудиту

- У цьому останньому повторному аудиті exhaustive не запускався; plan містить задокументований зелений прогін 13/13 на 2026-08-03, але його артефакт окремо не перевірявся.
- Packaged Native AOT і реальний SHA linkage виконано локально на `win-x64`. Повний `build_evidence_bundle -RequireAll` із release/exhaustive TRX та Linux runner dry run у межах аудиту не запускався.
- Зовнішні solver binaries, benchmark corpus reproduction у контейнерах і публікація NuGet не запускалися, бо це довгі/зовнішньо залежні або незворотні операції.
- Звіт описує поточне робоче дерево, а не лише `HEAD`; частина знайденого коду ще не відстежується Git.

## Валідація на оверінжиніринг

### Вердикт

**Усі шість рішень проти інфраструктурного оверінжинірингу реалізовані; package/release delivery розблоковано, F-14 evidence hardening закрито. Алгоритмічний оверінжиніринг не підтверджено.**

| ID | Статус | Підстава |
|---|---|---|
| OE-01 | Закрито | 2 real packages + 7 migration shells; exact pack/forwarding contracts перевірені |
| OE-02 | Закрито | canonical install — `LogicalOptimizer`; Full став deprecated forwarding shell |
| OE-03 | Закрито | library projects переведені на `net8.0`; рішення та виміри зафіксовані ADR |
| OE-04 | Закрито | fast script, серіалізація дорогих профілів, timeout і diagnostics реалізовані |
| OE-05 | Закрито функціонально | exact bytes pack/test/evidence gate виконуються до push; post-push лишає visibility/index |
| OE-06 | Закрито | package/TFM/status/provenance docs синхронізовано; F-14 machine validation додано |

Складність SAT, BDD, d-DNNF, exact/heuristic minimization, budget/cancellation і перевірки еквівалентності відповідає заявленому призначенню toolkit-а. Прибирати ці механізми лише заради меншої кількості коду було б помилкою: вони реалізують різні класи задач і мають окремі correctness/performance envelopes.

Поточне дерево прибрало подвійну library-компіляцію й визначило два real packages. Сім старих IDs тимчасово лишаються forwarding shells для міграції, тому release matrix поки все ще має дев'ять IDs. Перехідна складність обмежена exact-version contract-ом, автоматичною package/evidence-перевіркою та consumer smoke.

### OE-01 — High — Закрито — надмірна гранулярність пакетів

**Факти:** дев'ять NuGet-пакетів; `.Bdd` має 2 production-файли, `.Dnnf` — 5, `.Minimization` — 6, `.Formats` — 8, `.Full` не має коду. Facade залежить від Core/Sat/Bdd/Minimization, а Full додатково агрегує facade/Dnnf/Formats.

Модульність корисна, якщо споживачі справді встановлюють двигуни окремо заради dependency або size isolation. Але всі shipped-пакети заявлені без сторонніх runtime dependencies, тому головна класична вигода package split вже слабша. Ціна розбиття конкретна:

- дев'ять pack/push/index/smoke/package-contract операцій на кожний release;
- дев'ять README/metadata/API compatibility surfaces;
- складніше пояснення різниці між `LogicalOptimizer` та `LogicalOptimizer.Full`;
- розсинхронізація DocFX і workflow paths уже сталася;
- `InternalsVisibleTo` з Core до восьми assemblies показує, що фізичні package boundaries не збігаються з реальною межею інкапсуляції.

**Рекомендація:** підтвердити пакетне розбиття даними NuGet downloads по кожному package та хоча б 2–3 зовнішніми use cases. Якщо таких даних немає, скоротити публічну матрицю до:

1. `LogicalOptimizer` — основний пакет;
2. опційно `LogicalOptimizer.Formats`, якщо format parsers справді варто ізолювати;
3. CLI tool.

BDD, SAT, minimization і DNNF можуть залишатися namespaces/assemblies всередині основного пакета або internal project boundaries без окремої публікації. Не проводити злиття до перевірки реального usage: це breaking distribution change.

**Стан 2026-08-03:** прийнято ADR консолідації до `LogicalOptimizer` + CLI; старі IDs перетворено на exact-version forwarding shells. Exact workflow pack ×9, contract 152/152 і local consumer smoke підтвердили delivery.

### OE-02 — Medium — Закрито — `LogicalOptimizer` і `LogicalOptimizer.Full` створювали зайвий вибір

README має пояснювати три способи інсталяції, а назва основного пакета не означає «повний продукт». Для користувача без контексту природне очікування протилежне: `LogicalOptimizer` має містити весь LogicalOptimizer, тоді як `.Full` виглядає як workaround історичного package split.

**Рекомендація:** обрати одну канонічну точку входу. Найпростіший контракт — `LogicalOptimizer` як повний batteries-included пакет, а engine packages як advanced opt-in для тих, хто довів потребу. Якщо facade навмисно має бути slim, перейменування наступної major-версії повинно робити це очевидним (`LogicalOptimizer.Facade`/`LogicalOptimizer.Standard`), не додавати третій синонім.

**Стан 2026-08-03:** `LogicalOptimizer` став єдиним canonical library install; `LogicalOptimizer.Full` — deprecated forwarding shell.

### OE-03 — Medium — Закрито — `net8.0;net10.0` multi-targeting не мав видимої функціональної відмінності

У production C# не знайдено умов `NET8_0`/`NET10_0`; сім бібліотек компілюються двічі з однаковим кодом. `net8.0` asset і так сумісний із застосунками на новіших .NET, якщо немає net10-specific API/optimization. Поточна схема подвоює частину build, pack validation, API та AOT-поверхні без продемонстрованої користі.

**Рекомендація:** target лише `net8.0`, доки не з'явиться вимірювана причина мати net10-specific binary. CLI/tests/benchmarks можуть залишатися на net10. Якщо причина є (JIT/API/performance), зафіксувати її benchmark-ом або compatibility test у `doc/decisions`.

**Стан 2026-08-03:** сім library projects target лише `net8.0`; CLI/tests/benchmarks/AOT hosts лишилися на net10. Рішення, виміри й reversal criterion зафіксовані в `doc/decisions/net8-single-target.md`.

### OE-04 — Medium — Закрито — доказова інфраструктура сильна, але її вартість була розподілена невдало

Тестового C# приблизно 20,4 тис. рядків проти приблизно 16,3 тис. у shipped library/CLI коді; знайдено близько 1100 `[Fact]`/`[Theory]` та test-class declarations. Саме співвідношення не є дефектом для solver library: exhaustive, differential, property і fuzz перевірки виправдовують claims про soundness/minimality.

Оверінжиніринг проявлявся в тому, що всі типи доказів живуть в одній test assembly і standard `dotnet test` запускає дорогі sweeps паралельно. Новий `tools/test.ps1` відновлює швидкий developer loop і серіалізує дорогі профілі; фізичний поділ assembly тепер є можливою подальшою оптимізацією, а не необхідним виправленням.

**Рекомендація:** не видаляти correctness-тести. Розділити їх за operational role:

- fast unit/contract tests — default;
- exhaustive/release evidence — окрема assembly або явний pipeline;
- benchmarks/performance/corpora — окрема assembly;
- external-oracle tests — окремий opt-in профіль.

Це зменшує coupling і час локальної ітерації без втрати доказів.

### OE-05 — Medium — Закрито функціонально — release workflow переносить гарантії до publish

Exact pack, contract, local install/AOT smoke, checksums, release/exhaustive TRX і `build_evidence_bundle -RequireAll` стоять до push. NuGet index закономірно додається після push як visibility check. `workflow_dispatch` повторює весь pre-publish sequence на Linux із `PUBLISH=false`, завантажує `evidence-prepush` і пропускає attestation, OIDC login, NuGet push, post-push verification та GitHub release. Локально packaged Native AOT і SHA linkage зелені; сам GitHub dry run ще не запускався в межах аудиту.

**Рекомендація:** максимум гарантій перенести до pre-publish staging: pack once, перевірити саме ці bytes, install/AOT-smoke з локального package source, створити evidence bundle, і лише потім publish ті самі артефакти. Post-publish залишити коротким verification/visibility job. Package count reduction автоматично спростить workflow найбільше.

### OE-06 — Low — Закрито — документація дублює одну інформацію в багатьох формах

Є root README (~46 KB), package README, 40 концептуальних Markdown-файлів, generated API, `CLAIMS`, `BENCHMARKS`, `COMPARISON_METHODOLOGY`, `COMPETITIVE_ASSESSMENT`, release evidence та окремі site articles. Велика частина потрібна, але поточні docs workflow gaps показують ціну множинних джерел істини.

**Рекомендація:** визначити canonical source для package matrix, engine envelope, claims і benchmark methodology; інші сторінки мають посилатися або генерувати таблиці з нього. Додати link/consistency check лише для критичних контрактів, не тест на кожне речення документації.

### Що не є оверінжинірингом

- `ResourceBudget`, cancellation та explicit `Unknown/BudgetExceeded/TooLarge`: потрібні для експоненційних алгоритмів і чесного API.
- Асиметричний trust contract зовнішнього SAT solver-а: це необхідна межа безпеки, хоча реалізацію треба зробити immutable.
- Окремі SAT/BDD/d-DNNF алгоритми: вони мають різні властивості, а не дублюють один одного.
- Property, fuzz, differential та exhaustive tests: виправдані claims про soundness і minimality; проблема лише в їх packaging/execution profile.
- AOT smoke і package-content validation: відповідають публічним заявам про Native AOT та dependency-free delivery.
- `OptimizationOptions`: приблизно десять параметрів для facade такого масштабу прийнятні; вони згруповані в один immutable-style об'єкт, а не рознесені по combinatorial overloads.

### Рішення про спрощення

| Область | Рішення |
|---|---|
| Алгоритми/engines | залишити |
| Budget/status/cancellation | залишити |
| Correctness evidence | залишити, рознести по test profiles/assemblies |
| 9 package IDs | 2 real packages + 7 exact-version тимчасових forwarding IDs; F-12/F-13 закриті |
| Facade + Full | canonical `LogicalOptimizer`; Full deprecated forwarding shell |
| `net8.0;net10.0` | library assets переведено на `net8.0` |
| Release evidence | pre-publish evidence gate і SHA linkage validation F-14 реалізовані |
| Docs | package/TFM/status/provenance канонізовано; OE-06 закрито |

### Критерій, за яким спрощувати

Не використовувати кількість файлів або рядків як самостійну мету. Компонент варто залишити, якщо він має хоча б одне з трьох: окремий зовнішній consumer, окремий security/correctness contract або вимірювану operational benefit. Якщо пакет, target framework, workflow step чи документ не проходить жоден критерій, його слід консолідувати або прибрати в наступній major-версії.
