# План-чекліст розробки за результатами аудиту

Джерело: [DEEP_RESEARCH_REVIEW.md](DEEP_RESEARCH_REVIEW.md) (аудит від 2026-07-31).
Порядок виконання: **закриті Етапи 1–4 → Етап 5 (F-10/F-11) → Етап 6 (OE-backlog) → фінальна верифікація**. Breaking distribution changes з Етапу 6 дозволені лише після окремого decision record і usage evidence.

---

## Етап 1 — Trust boundary зовнішнього SAT-адаптера (High)

### F-01: `int.MinValue` обходить валідацію літералів ✅

- [x] У `LogicalOptimizer.Sat/IExternalSatSolver.cs` перевірка `literal == 0 || literal == int.MinValue` до взяття модуля (у `ValidateLiteral` та в model-циклі `IsSatisfiedBy`).
- [x] Конструктор: `int.MinValue` у clauses/assumptions → `ArgumentOutOfRangeException` (а не `OverflowException`).
- [x] `IsSatisfiedBy`: `int.MinValue` у недовіреній моделі → `false` (а не виняток).
- [x] Тести: `int.MinValue` окремо в clauses, в assumptions і в model.

### F-02: Validated CNF мутабельний після конструктора (TOCTOU) ✅

- [x] У конструкторі `ExternalSatProblem` глибокий snapshot: копія списку clauses, кожного `int[]` clause і assumptions; копіювання виконується ДО валідації (валідується вже копія).
- [x] `Clauses` віддає `ClauseSnapshotView` (копія масиву на кожен доступ — навіть недовірений адаптер не мутує validated state); `Assumptions` — `Array.AsReadOnly` над внутрішньою копією.
- [x] `FromCnf`: покривається snapshot-ом конструктора автоматично (вибір задокументовано в XML-doc конструктора).
- [x] Тести: mutation-after-construction (`Constructor_SnapshotsClausesAndAssumptions`, `Clauses_HandOutCopies_NotTheValidatedSnapshot`) і concurrent-read (`ToDimacs_IsStableUnderConcurrentReadsAfterSourceMutation`).
- [x] Стиль звірено зі зразком `NaryNodeContractTests.Operands_AreDefensivelyCopied`.

### F-03: `ExternalSatResult` без snapshot-семантики (Medium, та сама межа довіри) ✅

- [x] `ExternalSatResult.Satisfiable(...)` копіює модель у `int[]` + `Array.AsReadOnly`.
- [x] Checker (`IsSatisfiedBy` → `DecodeCounterexample`) працює з одним стабільним snapshot.
- [x] Тест `ExternalSatResult_Satisfiable_SnapshotsTheModel`: мутація вихідного масиву після `Satisfiable(...)` не змінює result.

**Критерій готовності етапу:** ✅ виконано 2026-07-31 — після F-08 загальний external-SAT контур має 28/28 тестів (20 наявних + 6 з Етапу 1 + 2 cancellation tests), fast gate 1376/1376, Release-збірка 0 warnings. Публічний API не змінено (pinned `PublicApi.approved.txt` сумісний: сигнатури властивостей збережено).

---

## Етап 2 — Ергономіка тестового запуску (F-04, Medium) ✅

- [x] Канонічний script entry point — `tools/test.ps1`: default = fast gate (CI-фільтр, 1376 тестів, ~15-20 с); режими `-Performance`, `-Exhaustive`, `-Full`.
- [x] Ізоляція sweep-ів від паралелізму: замість переносу в окрему assembly дорогі категорії запускаються з `-- xUnit.ParallelizeTestCollections=false` (перевірено емпірично за банером «parallel test collections = off») — скрипт і exhaustive/release workflows роблять це автоматично; gate-тести тих самих класів не втрачають паралелізм.
- [x] Діагностика «зависання»: `xunit.runner.json` з `longRunningTestSeconds: 60` — у дорогих прогонах runner називає тест, який працює довше хвилини (діагностика вмикається лише для дорогих прогонів, gate лишається без шуму).
- [x] `README.md` (секція Testing) і `doc/TESTING.md` (нова Part 0): швидка команда першою, повний прогін з очікуваним часом, заборона паралельного запуску sweep-ів, workaround для залишеного `testhost`.
- [x] GitHub Actions: `ci.yml` — `timeout-minutes: 30` + `--blame-hang 20m` + upload TRX/sequence при `always()`; `release.yml` — `timeout-minutes: 60`, серіалізований ReleaseEvidence, upload TRX при `always()`; `exhaustive.yml` — серіалізований прогін (timeout і TRX upload там уже були).

**Критерій готовності:** ✅ виконано 2026-07-31 — fast gate через `tools/test.ps1`: 1376/1376 за ~15-20 с; послідовний exhaustive-прогін (`tools/test.ps1 -Exhaustive`) доведено до завершення: 13/13 за 25,9 хв.

---

## Етап 3 — Документаційний pipeline (F-05, F-06, Medium) ✅

### F-05: Покриття пакетів у DocFX і тригерах ✅

- [x] Рішення: API reference для **семи бібліотечних пакетів** (додано `Dnnf` і `Formats` — вони мають публічні типи в pinned API-списку; `Cli` — shell-інструмент із контрактом «командний рядок + JSON schema», `Full` — метапакет без власної assembly). Зафіксовано в `docs-site/api/index.md` і коментарі `docs.yml`. Побічно виправлено наявну розбіжність: `api/index.md` уже обіцяв Dnnf, якого metadata не генерувала.
- [x] `docs-site/docfx.json`: додано `LogicalOptimizer.Dnnf.csproj` і `LogicalOptimizer.Formats.csproj` у `metadata.src.files`.
- [x] `api/index.md`: сім пакетів у таблиці (+ рядок Formats) і явне пояснення, чому Cli/Full без API reference.
- [x] `docs.yml` `on.push.paths`: додано каталоги `Dnnf`, `Formats`, `Cli`, `Full` і `.config/dotnet-tools.json`.
- [x] Бонус: виправлено битий лінк `cli.md` → `cli-usage.md` у `packages-and-architecture.md` (єдиний warning збірки сайту).

### F-06: Незафіксована версія DocFX ✅

- [x] DocFX 2.78.5 запінено в наявному tool manifest `.config/dotnet-tools.json` (`rollForward: false`).
- [x] `docs.yml`: `dotnet tool install -g docfx` → `dotnet tool restore` + `dotnet docfx`; оновлення версії — окремим PR через маніфест (задокументовано в коментарі workflow).

**Критерій готовності:** ✅ виконано 2026-07-31 — локальна збірка сайту з pinned DocFX 2.78.5: 86 API-файлів (7 проєктів), 0 warnings, 0 errors.

---

## Етап 4 — Supply chain та CI-гігієна (F-07, F-09 Medium/Low; F-08 Low)

### F-07: Actions за рухомими major-тегами ✅

- [x] Усі 7 використовуваних actions (`checkout`, `setup-dotnet`, `upload-artifact`, `upload-pages-artifact`, `deploy-pages`, `attest-build-provenance`, `NuGet/login`) запінено на повні commit SHA в усіх 9 workflow-файлах (8 наявних + новий `dependency-audit.yml`); SHA резолвлено з живих тегів через `git ls-remote`.
- [x] Поруч із кожним SHA — коментар із версією (`# v4.4.0` тощо).
- [x] `.github/dependabot.yml`: екосистема `github-actions` (weekly) — Dependabot оновлює SHA і коментар версії разом.

### F-09: Автоматичний dependency audit ✅

- [x] Новий `.github/workflows/dependency-audit.yml`: щопонеділка + `workflow_dispatch`, `dotnet list package --vulnerable --include-transitive` по всьому solution, лог — артефактом при `always()`.
- [x] Dependabot для NuGet (weekly, ліміт 10 PR).
- [x] Політика network failures зафіксована в коментарі workflow і реалізована: команда під `set -o pipefail`, вердикт парситься з виводу — і знайдена вразливість, і збій сканування (недоступний advisory source) дають червоний прогін; збій ніколи не виглядає як чистий результат.

### F-08: Cancellation до побудови miter CNF ✅

- [x] `ExternalSatEquivalenceChecker.Check`: `ThrowIfCancellationRequested()` на вході (до побудови XOR-miter і Tseitin CNF) та після повернення адаптера (адаптер, що ігнорує токен, не може перетворити скасований запит на вердикт).
- [x] Тести: `Check_PreCanceledToken_ThrowsWithoutCallingTheAdapter` (адаптер не викликається взагалі) і `Check_AdapterIgnoresCancellation_VerdictIsNotReturned`.
- [x] Cancellation-aware overload для Tseitin conversion: відкладено свідомо — обидві перевірки токена вже стоять довкола конверсії, а даних, що конверсія реальних AST помітно довга, немає; повернутися, якщо профілювання покаже інше.

---

## Етап 5 — Залишкові findings F-10/F-11

### F-10: Переносимий canonical test command (Low) ✅

- [x] `README.md`: обидві команди — `.\tools\test.ps1` для Windows (обидва shell), `pwsh tools/test.ps1` для Linux/macOS; блоки перекваліфіковано з `bash` на `powershell`.
- [x] `doc/TESTING.md` Part 0: таблиця переведена на shell-нейтральний `./tools/test.ps1` + явна примітка, що PowerShell 7 на Windows НЕ вимагається.
- [x] Скрипт написаний сумісно з Windows PowerShell 5.1 (без `&&`, ternary, null-coalescing).
- [x] Smoke: `powershell -File tools\test.ps1 -NoBuild` під Windows PowerShell 5.1 — 1376/1376 за 9 с (`pwsh` у цьому середовищі відсутній — cross-platform варіант перевірить CI/інше середовище).

**Критерій готовності:** ✅ виконано 2026-07-31.

### F-11: Синхронізація плану з аудитом (Low) ✅

- [x] Baseline оновлено (поточний: 1377 fast tests / 28 external-SAT; +1 — новий forwarding-контрактний тест у `MetaPackageTests`).
- [x] F-10 додано як окрему задачу.
- [x] OE-01–OE-06 винесено в керований backlog нижче.
- [x] Owner/deadline: **owner — мейнтейнер; deadline — планування v4.0**. Виконано достроково: усі OE-рішення ухвалені 2026-08-03 (див. Етап 6) і зафіксовані ADR-ами в `doc/decisions/`.
- [x] Процесне правило (синхронізувати обидва документи одним PR) лишається чинним для майбутніх змін.

**Критерій готовності:** ✅ виконано.

---

## Етап 6 — Подолання інфраструктурного оверінжинірингу

Цей етап не санкціонує автоматичне видалення функцій або пакетів. Спочатку збираються дані й ухвалюється рішення; консолідація package/API surface є breaking change і виконується лише в major release.

### OE-01: Перевірити доцільність дев'яти NuGet-пакетів (High) ✅

- [x] Downloads зібрано (nuget.org search API, 2026-07-31, latest 3.2.2 всюди): facade 206 · Core 219 · Sat 209 · Bdd 195 · Dnnf 172 · Formats 102 · Minimization 225 · Cli 204 · Full 135. Інтерпретація: розподіл майже рівномірний і низький — патерн «дзеркала/сканери + релізні restore», а НЕ незалежні споживачі окремих пакетів; евіденсу на користь granular split немає. Пакети опубліковані (2.1.0…3.2.2), тож консолідація — справжній breaking change і потребує migration path.
- [x] **Рішення користувача (2026-08-03): консолідувати у v4.0 до 2 пакетів (бібліотека + CLI tool), старі ID — deprecated forwarding-пакети на перехідний період.** ADR: `doc/decisions/package-consolidation-v4.md`.
- [x] Реалізовано: `LogicalOptimizer` 4.0.0 несе всі 7 асембл (+XML docs, +7 pdb у snupkg, `SuppressDependenciesWhenPacking` + pack-target); 6 бібліотек стали non-packable; 7 forwarding-оболонок (6 нових під `forwarding/` через hand-written nuspec — ProjectReference дав би «ambiguous project name»; `Full` конвертовано на місці) — кожна з єдиною exact-залежністю `LogicalOptimizer [$version$]`.
- [x] Контракти/тести/скрипти: `verify_package_contract.ps1` (нова матриця: kind `forwarding`, `bundled-assemblies-complete`, `forwards-to-consolidated-package`; 152/152 pass), `MetaPackageTests` (3 тести forwarding-контракту), `smoke_install.ps1` (усі forwarding-оболонки повинні приносити ПОВНИЙ набір асембл — перевірено локально: PASSED), `verify_nuget.ps1` без змін (ті самі 9 ID).
- [x] Публічний API/асемблії/namespace не змінені — pinned `PublicApi.approved.txt` і ArchUnitNET-правила working as-is.
- [x] Migration guide: README + package README + CHANGELOG 4.0.0. Пост-publish крок (позначка deprecated на nuget.org через UI/API) зафіксовано в ADR.
- [x] Пошук зовнішніх granular use cases завершено відсутністю підтверджених кейсів; це negative evidence разом із download pattern стало підставою ADR.
- [x] Варіанти оцінено в ADR; обрано 1 library + CLI, а не 3-package compromise.
- [x] ADR `package-consolidation-v4.md` створено з migration і reversal criteria.
- [x] Старі IDs не видаляються одразу: реалізовані forwarding/meta packages на перехідний період.
- [x] F-12/F-13 закриті: exact workflow pack `--no-build` ×9, contract 152/152 і local consumer smoke пройшли; forwarding policy — exact `[4.0.0]`.

**Критерій готовності:** ✅ виконано 2026-08-03; рішення спирається на usage data, має ADR/migration/tests, exact CI/release pack sequence зелена.

### OE-02: Одна канонічна точка входу замість неоднозначних facade/Full (Medium) ✅

- [x] Canonical install: **`LogicalOptimizer`** (з v4.0 це і є все).
- [x] Доля `LogicalOptimizer.Full`: deprecation — конвертовано у forwarding-оболонку.
- [x] README (Install ×2 місця), `docs-site/articles/introduction.md`, `packages-and-architecture.md`, package descriptions — один recommendation path.
- [x] Package graph tests: `MetaPackageTests.ForwardingShells_ForwardExactlyToTheConsolidatedPackage` + `FullPackage_ForwardsToTheConsolidatedPackageOnly` + контрактні перевірки `verify_package_contract.ps1` — третій синонім не може з'явитися непоміченим.

**Критерій готовності:** ✅ виконано 2026-08-03.

### OE-03: Обґрунтувати або прибрати `net10.0` library assets (Medium) ✅ ПРИБРАНО

- [x] Виміряно: rebuild бібліотечного ланцюжка 27,9 с (dual) проти 9,8 с (net8-only) — ~3×; кожен пакет ніс дубльований net10-асет.
- [x] Перевірено: жодного `#if NET*` у семи бібліотеках — обидва TFM компілювали ідентичний код; net10-binary не давав API/поведінкової відмінності. JIT-переваги дає runtime, не TFM асемблі.
- [x] ADR з evidence: `doc/decisions/net8-single-target.md` (включно з reversal criterion).
- [x] Сім бібліотек + Full переведено на `net8.0`; CLI/tests/benchmarks/AotSmoke лишилися на `net10.0`; `verify_package_contract.ps1` і docfx (`TargetFramework: net8.0`) оновлені; README/CONTRIBUTING/articles — теж.
- [x] Побічний ефект зміни верифіковано: тести (net10-хост) споживають net8-асемблії — gate зелений; AOT-напрям (net10-застосунок + net8-пакет) покриє `aot.yml` і pre-publish AOT smoke.

**Критерій готовності:** ✅ виконано 2026-08-03.

### OE-04: Рознести correctness evidence за operational role (Medium) ✅

- [x] Fast unit/contract gate став canonical default loop.
- [x] Performance та Exhaustive зроблено явними opt-in профілями.
- [x] Дорогі collection runs серіалізовано; додано timeout, long-running diagnostics і artifacts.
- [x] Фізичний поділ test assembly оцінено як необов'язковий після усунення operational проблеми.

**Критерій готовності:** ✅ виконано — fast gate 1376/1376; exhaustive 13/13 завершено послідовно.

### OE-05: Перенести максимум release guarantees до NuGet publish (Medium) ✅ ФУНКЦІОНАЛЬНО

- [x] Release sequence перебудовано: pack once → contract verification → **install smoke + Native AOT smoke з ЛОКАЛЬНОГО package source (нове, до push)** → checksums → attestation → push → лише index/visibility verification → evidence bundle → GitHub release.
- [x] `smoke_install.ps1` отримав параметр `-Source` (локальна тека або feed) + NuGet.config у throwaway-каталозі, щоб restore бачив локальні пакети. Перевірено локально проти artifacts 4.0.0: PASSED (facade + 7 forwarding-оболонок).
- [x] Пост-push published-package smoke прибрано з release.yml — ті самі bytes уже доведені до push; після push лишилися visibility-перевірка (`verify_nuget.ps1`) і публікація релізу.
- [x] Pre-publish `build_evidence_bundle -RequireAll` перевіряє всі доступні до push докази; фінальний bundle після push додає тільки nuget-index-report/visibility.
- [x] Recovery для часткового publish: `dotnet nuget push --skip-duplicate` робить повторний прогін workflow ідемпотентним (уже опубліковані пакети пропускаються) — зафіксовано як runbook-примітку тут.
- [x] F-12: consolidated pack із `--no-build` виправлено й повторено для всіх 9 ID.
- [x] F-14: machine report чесно називає local pre-publish source; generated prose/claim синхронізовано (див. розділ F-14 нижче).
- [x] Policy: precomputable `-RequireAll` gate стоїть до push; post-push bundle додає visibility/index.

**Критерій готовності:** ✅ функціонально виконано; exact pack/contract/install sequence і F-14 checksum linkage доведено локально, повний AOT/evidence sequence очікує першого реального GitHub Actions run.

### OE-06: Скоротити дублювання документації та джерел істини (Low) ✅

- [x] Інвентаризовано й оновлено всі місця package/framework-матриці: README (Install, Features, Architecture, release-опис), `doc/CLAIMS.md`, `docs-site/articles/introduction.md` / `packages-and-architecture.md` / `testing-overview.md` / `choosing-a-tool.md`, `docs-site/api/index.md`, CONTRIBUTING.
- [x] Canonical sources: package matrix — ADR `package-consolidation-v4.md` + машинна перевірка `verify_package_contract.ps1` ($contract — єдина виконувана таблиця); install guidance — README (статті посилаються); TFM-матриця — ADR `net8-single-target.md` + той самий contract script; engine envelopes — уже мали canonical source (`EngineEnvelopeConsistencyTests`).
- [x] Consistency checks: package count/names/frameworks — contract script (гейт у CI і перед push); forwarding-контракт — `MetaPackageTests`; brittle-тестів на prose не додавали.
- [x] Величезне джерело дублювання (7 «інсталюй окремий шар» блоків у кількох доках) зникло разом із причиною — матриця з 9 самостійних пакетів згорнулася до 2.
- [x] Залишок F-14 закрито: AOT provenance prose/claim синхронізовано в `smoke_install.ps1`, `build_evidence_bundle.ps1`, `doc/CLAIMS.md` і README; F-16 закрито поточною синхронізацією status evidence.

**Критерій готовності:** ✅ package/TFM/claim/status contracts не суперечать машинним artifacts і workflow sequence.

### Порядок OE-рішень — рішення ухвалені, delivery hardening триває

Фактичний порядок виконання (2026-08-03) відповідав запланованому: OE-03 → OE-01/OE-02
(одне package-strategy рішення + реалізація) → OE-06 → OE-05. OE-04 було завершено раніше
(Етап 2). Усі шість OE-пунктів закриті (prose-залишок F-14 усунуто 2026-08-03). Рішення зафіксовані ADR-ами в `doc/decisions/`.

---

## Історичний backlog F-12–F-16 (superseded повторною верифікацією нижче)

> Цей checklist фіксує стан на момент виявлення findings і не є поточним статусом. Актуальні результати та залишок наведені в наступному розділі.

### F-12: Відновити CI/release pack consolidated package (High) ⬜

- [ ] Відтворити exact workflow command: `dotnet pack LogicalOptimizer/LogicalOptimizer.csproj -c Release --no-build ...`; очікуваний поточний результат — `NETSDK1085`.
- [ ] Узгодити `PackCompanionAssemblies` із `NoBuild=true` або прибрати `--no-build` для цього package.
- [ ] Додати regression, який виконує ті самі дев'ять pack commands після clean restore/build.
- [ ] Повторити package contract і local install smoke саме над artifacts цього exact sequence.

**Критерій готовності:** CI package job і release Pack step проходять без альтернативних локальних команд; 9 nupkg/2 snupkg очікуваного складу створені.

### F-13: Узгодити forwarding dependency range (Medium) ⬜

- [ ] Вирішити: exact `[4.0.0]` чи documented lower-bound/major range.
- [ ] Оновити шість hand-written nuspec, Full packaging, ADR і migration text відповідно до рішення.
- [ ] Переробити `forwards-to-consolidated-package` та `MetaPackageTests`, щоб вони парсили семантику NuGet range, а не порівнювали оманливий plain version string.
- [ ] Додати negative test, де `4.0.0` не проходить як exact `[4.0.0]`.

**Критерій готовності:** ADR, actual nuspec і contract report однаково описують допустимі версії dependency.

### F-14: Виправити provenance pre-publish AOT evidence (Medium) ⬜

- [ ] `smoke_install.ps1` записує resolved source/type, а не hard-coded `published nuget.org package`.
- [ ] AOT report містить hashes/ідентифікатори tested local packages або посилання на checksum set.
- [ ] `doc/CLAIMS.md` і evidence bundle prose говорять «pre-publish artifacts later published unchanged», якщо тест ішов із local source.
- [ ] Додати test для JSON report у local та nuget.org modes.

**Критерій готовності:** evidence відповідає фактичному джерелу bytes і дозволяє зв'язати smoke з опублікованими checksums.

### F-15: Ізолювати CLI tool smoke (Low) ⬜

- [ ] Замінити `dotnet tool install --global` на `--tool-path <temp>`.
- [ ] Запускати binary з temp tool path.
- [ ] Двічі послідовно запустити smoke й підтвердити відсутність стану поза `$workDir`.

**Критерій готовності:** smoke не змінює global tool state користувача/runner-а та є повторюваним.

### F-16: Зробити status evidence несуперечливим (Low) ⬜

- [ ] Після F-12–F-15 оновити status у plan та deep audit одним change set.
- [ ] Розділити historical baselines 1376 і current 1377, не називати обидва поточними.
- [ ] Не позначати OE завершеним, доки exact workflow commands і claims не перевірені.

**Критерій готовності:** жоден completed status не має unchecked acceptance item або відомого failing exact command.

---

## Етап 7 — Findings повторного аудиту (F-12…F-16, 2026-08-03)

Повторний аудит знизив статуси OE-01/OE-05/OE-06 до «частково» через п'ять нових findings.
Урок F-16 враховано: нижче статуси виставлені лише після зеленої верифікації.

### F-12 (High): consolidated pack падав із `--no-build` (NETSDK1085) ✅

- [x] Причина: pack-target facade через `ResolveReferences` за замовчуванням БУДУВАВ project references, які бачили `NoBuild=true` → NETSDK1085. Локальні прогони цього не ловили, бо пакували без `--no-build` (CI/release — з ним).
- [x] Фікс: у `LogicalOptimizer.csproj` — `<BuildProjectReferences>false</BuildProjectReferences>` при `NoBuild=true` (резолв по вже збудованих outputs, що і є контрактом `--no-build`).
- [x] Верифіковано CI-еквівалентним шляхом: `dotnet pack --no-build` усіх 9 проєктів — успішно, 14 lib-entries у consolidated пакеті.

### F-13 (Medium): forwarding-залежність була `>= 4.0.0` замість обіцяного exact `[4.0.0]` ✅

- [x] Усі 6 nuspec під `forwarding/` — `version="[$version$]"` (exact range за документацією NuGet).
- [x] `Full` конвертовано на той самий nuspec-механізм (ProjectReference не здатен виразити exact range) — `LogicalOptimizer.Full.nuspec` з `[$version$]`.
- [x] `verify_package_contract.ps1`: `forwards-to-consolidated-package` тепер вимагає саме `[Version]`; `MetaPackageTests` перевіряє `[$version$]` у всіх 7 nuspec (Full включно, тест уніфіковано).
- [x] Верифіковано: контракт 152/152; install smoke з локального feed — PASSED (exact range резолвиться).

### F-14 (Medium): pre-publish AOT provenance і SHA linkage закриті ✅

- [x] Machine-readable: поле `source` AOT-звіту відображає фактичне джерело — `local packed artifacts (pre-publish): <шлях>` або `published nuget.org package`; поле `consolidatedPackageSha256` записує hash local package (`null` для nuget.org-режиму).
- [x] Human-readable prose дозакрито: `smoke_install.ps1` — SYNOPSIS/DESCRIPTION/PARAMETER Version/коментар AOT-кроку («PACKAGE bytes under test», не «PUBLISHED from nuget.org»); `build_evidence_bundle.ps1` — DESCRIPTION, дерево файлів бандла, «Proves» для AOT і «Reading order» в generated `INDEX.md`; `doc/CLAIMS.md` — Native-AOT evidence-рядок («packaged NuGet bytes», release gate = local pre-publish, post-release = published, `source` називає режим); `README.md` — release-опис переписано (усі відмовні перевірки до push).
- [x] Свідомо збережене «published»: reproduction-інструкції бандла (читач відтворює перевірку ПІСЛЯ релізу проти nuget.org) і коментар retry-логіки tool install (про індексацію nuget.org) — там слово точне.
- [x] Residual Low закрито (2026-08-03): `build_evidence_bundle.ps1` тепер АВТОМАТИЧНО відхиляє mismatch — при наявному AOT-звіті викликається спільна функція `Test-AotProvenance` (`tools/AotProvenanceCheck.ps1`): local mode вимагає `consolidatedPackageSha256`, що збігається з рядком `LogicalOptimizer.<v>.nupkg` у `SHA256SUMS.txt`; nuget.org mode вимагає `null`; будь-яка невідповідність → червоний бандл незалежно від `-RequireAll` (неконсистентний звіт гірший за відсутній).
- [x] Semantic fixture-level test обох режимів: `Techniques/AotProvenanceContractTests` — 6 кейсів (local match/mismatch/missing-sha/missing-sums, published null-ok/with-sha-fail) ганяють САМУ PowerShell-функцію через синтетичні fixtures (без реімплементації логіки, офлайн, детерміновано; pwsh на Linux/macOS, Windows PowerShell як fallback) — у fast gate.
- [x] End-to-end на реальних артефактах: бандл із коректним хешем — «AOT provenance check passed», exit 0; зі зламаним — відмова, що називає обидва хеші, exit 1.
- [x] Верифікація: обидва скрипти 0 parse errors; нові тести 6/6; fast gate 1382/1382 за 23 с (doc-guard тести по README/CLAIMS зелені).

### F-15 (Low): smoke-скрипт залишав глобально встановлений CLI tool ✅

- [x] `finally` тепер best-effort деінсталює `LogicalOptimizer.Cli`, якщо саме цей прогін його встановив (прапорець `$script:cliToolInstalled`, ініціалізований під `Set-StrictMode`); невдалий uninstall не маскує вердикт.

### F-16 (Low): план передчасно оголошував OE завершеним ✅

- [x] Статуси Етапу 6 переоцінені за результатами повторного аудиту й закриті лише після зеленої верифікації (цей розділ).
- [x] Залишок OE-05 (`-RequireAll` після push) закрито: новий крок «Evidence gate (pre-publish, -RequireAll)» у `release.yml` валідує ВСІ доступні до push докази (contract, AOT, checksums, обидва .trx); `-NuGetIndexReport` зроблено опційним при omission (він фізично не існує до push). Фінальний бандл після push лише додає index-звіт — нове там тільки visibility.
- [x] Актуальний статус OE після повторної верифікації: OE-01 ✅ · OE-02 ✅ · OE-03 ✅ · OE-04 ✅ · OE-05 ✅ · OE-06 ✅ (prose-залишок F-14 закрито 2026-08-03).

**Верифікація Етапу 7 (останній повторний аудит 2026-08-03):** Release build 0 warnings · pack `--no-build` ×9 успішно · контракт 152/152 · targeted AOT-provenance+MetaPackage+ExternalSat 36/36 · поточний fast gate 1382/1382 (19–38 с у повторних прогонах; історичні baselines: 1377 до злиття forwarding-тесту, 1376 до 6 provenance fixtures) · install smoke з локальних артефактів PASSED · packaged Native AOT win-x64 + SHA linkage PASSED (2,100,736 bytes) · DocFX 0 warnings/errors · vulnerability audit clean · PowerShell AST і pinned Actions clean. Нових findings немає; F-14 закрито.

---

## Фінальна верифікація (пункт 5 мінімального плану аудиту)

- [x] `dotnet build LogicalOptimizer.sln -c Release` — 0 warnings, 0 errors.
- [x] `FullyQualifiedName~ExternalSat` — 28/28 (20 наявних + 6 з Етапу 1 + 2 з F-08).
- [x] Fast gate (`Category!=Performance&Category!=Exhaustive`) — 1376/1376 за ~19 с.
- [x] Послідовний exhaustive-прогін — 13/13 за 25,9 хв, доведений до завершення (Етап 2).
- [x] Docs build із pinned DocFX 2.78.5 — 0 warnings, покриття = 7 бібліотечних пакетів (рішення Етапу 3).
- [x] Статус F-01–F-09 зафіксовано в цьому документі.
- [x] F-10 закрито (обидва shell variants задокументовано, smoke під Windows PowerShell 5.1 зелений).
- [x] F-11 закрито (owner — мейнтейнер; deadline — v4.0; фактично всі OE-рішення ухвалено 2026-08-03).
- [x] OE-01/OE-02/OE-03/OE-05/OE-06 — продуктові рішення ухвалені (ADR у `doc/decisions/`), версія піднята до 4.0.0, CHANGELOG має секцію [4.0.0].
- [x] OE-01/OE-05 delivery acceptance — F-12/F-13/F-15/F-16 закриті; pre-publish evidence gate стоїть до push.
- [x] OE-06 documentation acceptance — provenance wording F-14 закрито в `smoke_install.ps1`, `build_evidence_bundle.ps1`, `doc/CLAIMS.md` і README.
- [x] Evidence hardening — `consolidatedPackageSha256` механічно звіряється із `SHA256SUMS.txt` у `build_evidence_bundle.ps1` (спільна `Test-AotProvenance`), обидва source modes покриті semantic fixture test-ом `AotProvenanceContractTests` (6/6 у fast gate).
- [x] Після package/framework/release змін повторено (2026-08-03): Release build 0 warnings · fast gate 1377/1377 (~18 с) · pack усіх 9 ID · package contract 152/152 · локальний pre-publish install smoke PASSED (facade + 7 forwarding) · docs build 0 warnings. PLA-корпусні тести ізольовано в `TimeSensitiveCollection` (двічі спостережений load-flake: щільний 8-input кейс упирався в 10-секундний wall-clock cap facade під CPU-голодуванням паралельного gate).
- [x] Послідовний exhaustive-прогін на net8-асембліях консолідованої конфігурації: 13/13 за 17,5 хв (2026-08-03); повторений на фінальному стані після F-12…F-16 + provenance-hardening: 13/13 за 32 хв (2026-08-03).
- [x] Exact workflow pack sequence відтворено локально: `dotnet pack --no-build` ×9 + контракт 152/152 (2026-08-03, після F-12).
- [x] Локальний Native AOT розблоковано й прогнано (2026-08-03): причина попереднього блокування — тека VS Installer не в PATH, через що ilcompiler-івський toolchain-discovery не знаходив `vswhere.exe`; з `$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'` publish AotSmoke (win-x64) зелений — бінарник 2,1 MB, усі 6 engines PASS (net10 AOT-застосунок на net8-асембліях — той самий напрям, що в пакетів).
- [x] `release.yml` отримав **dry-run режим** (`workflow_dispatch`): проганяє ВЕСЬ pre-publish ланцюг — build, тести, evidence-sweep, pack, контракт, install + Native AOT smoke локальних артефактів на Linux runner, checksums, `-RequireAll` evidence gate — і зупиняється; attestation/login/push/index/release гейтовані на `PUBLISH=true` (лише реальний тег). Версія в dry-run береться з `Directory.Build.props`; артефакт `dry-run-evidence-<v>` вивантажується для інспекції. Усі 9 workflow YAML-валідні.
- [x] **Зовнішня верифікація виконана (2026-08-03, коміт `2bc2956`, серія з 7 conventional-коммітів запушена в `main`):**
  1. ✅ `CI` (обидві ОС: gate, pack `--no-build` ×9, contract) — success; `Docs` — success (сайт задеплоєно); `Native AOT` — success; обидва Dependabot-прогони по новому `dependabot.yml` — success.
  2. ✅ Release **dry-run** (run 30807946635) — success: усі 16 pre-publish кроків зелені на Linux runner — Version-from-props, CHANGELOG-гейт, Build, Test, ReleaseEvidence-sweep, Pack `--no-build` ×9, package contract, install + **Native AOT smoke локальних артефактів**, checksums, `-RequireAll` evidence gate, dry-run артефакт; усі 8 publish-кроків коректно **skipped** (`PUBLISH=false`). Це і є перший реальний зелений release-ланцюг без публікації.
- [x] **Реліз v4.0.0 опубліковано (2026-08-03):** тег `v4.0.0` (з `70b3423`, після зелених CI/AOT на HEAD) → release run 30808774555 — **success**, жодного червоного кроку: усі pre-publish гейти, attestation, push, index-verification, фінальний evidence bundle, GitHub release. Зовнішньо верифіковано: усі 9 package-ID **INDEXED @ 4.0.0** на nuget.org; GitHub release v4.0.0 із 13 assets (9 nupkg + 2 snupkg + SHA256SUMS.txt + evidence bundle zip).
- [ ] Післярелізний ручний крок (крок з ADR; потребує owner-доступу до nuget.org UI): поставити deprecated-позначки («Legacy», заміна — `LogicalOptimizer`) на 7 forwarding-ID: `.Core`/`.Sat`/`.Bdd`/`.Dnnf`/`.Formats`/`.Minimization`/`.Full`. Нічний Exhaustive workflow додатково підтвердить sweep-и на GitHub-раннері (03:20 UTC).
