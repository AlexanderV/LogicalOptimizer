# Реліз LogicalOptimizer

Публікація 7 NuGet-пакетів (`LogicalOptimizer.Core` / `.Sat` / `.Bdd` / `.Dnnf` /
`.Minimization` / `LogicalOptimizer` / `LogicalOptimizer.Cli`) автоматизована: push тега `v*` запускає
[`.github/workflows/release.yml`](.github/workflows/release.yml), який білдить, тестує, пакує й
пушить на nuget.org.

## Одноразове налаштування

Публікація використовує **Trusted Publishing (OIDC)** — жодного довгоживучого API-ключа в репо.
`NuGet/login@v1` обмінює короткоживучий OIDC-токен GitHub на тимчасовий ключ (дійсний ~1 год)
на час запуску workflow.

1. **Політика Trusted Publishing на nuget.org** — увійти → аватар → **Trusted Publishing** →
   додати політику:
   - **Repository Owner:** `AlexanderV`
   - **Repository:** `LogicalOptimizer`
   - **Workflow File:** `release.yml` (лише ім'я файлу, без `.github/workflows/`)
   - **Environment:** лишити порожнім (workflow не використовує environment).
   - Owner політики — твій акаунт; вона діє на всі пакети цього власника (тож і на нові ID).
   - *Примітка:* для приватного репо політика спершу «temporarily active» на 7 днів, поки перший
     успішний push не зафіксує GitHub repo/owner ID (захист від resurrection-атак).
2. **Repository variable `NUGET_USER`** — Settings → Secrets and variables → **Actions** →
   вкладка **Variables** → **New repository variable**:
   - Name: **`NUGET_USER`**, Value: твоє **ім'я профілю на nuget.org** (не email).
   - Це не секрет (ім'я профілю публічне), тому саме variable, а не secret. **Жодного
     `NUGET_API_KEY` більше не потрібно.**
3. **GitHub Pages** (для DocFX-сайту) — Settings → **Pages** → Build and deployment →
   **Source = GitHub Actions**.
4. **Доступність package ID** — перевір `https://www.nuget.org/packages/<ID>` для кожного з 7.
   Особливо голий `LogicalOptimizer`: якщо зайнятий кимось іншим — перейменуй `PackageId` у всіх
   csproj. Вільний ID (404) реєструється на твій акаунт при першому push.

## Випуск версії

Версія публікованих пакетів береться **з тега** (`/p:Version=${GITHUB_REF_NAME#v}`), тому
`<Version>` — це dev/fallback-значення. Воно централізоване в
[`Directory.Build.props`](Directory.Build.props) (одне місце на всі пакети), а не в кожному csproj.

```bash
# 0) КОД МАЄ БУТИ НА GITHUB (workflow робить checkout тега):
git push origin main

# 1) тег + push (приклад для 2.1.0):
git tag -a v2.1.0 -m "v2.1.0: BDD complement edges"
git push origin v2.1.0
```

`release.yml`: setup .NET 10 → `dotnet build -warnaserror` → `dotnet test` (CI-фільтр, `.trx`) →
**claim-critical exhaustive evidence** → `dotnet pack` 9 пакетів → **audit вмісту пакетів (gate
перед публікацією)** → **checksums + provenance attestation** → `dotnet nuget push
--skip-duplicate` → **верифікація присутності в реєстрі** → **installation + Native AOT smoke test
з опублікованих пакетів** → **release evidence bundle**. Якщо тести, exhaustive evidence або audit
впадуть — публікації не буде.

**Claim-critical exhaustive evidence:** README і [`doc/CLAIMS.md`](doc/CLAIMS.md) посилаються на
exhaustive-прогони по всіх 65534 чотиризмінних функціях як на доказ claims `verified` і
`MinimalProven`. Ці тести належать до категорії `Exhaustive`, яку і CI, і основний release-прогін
виключають — тобто для конкретного published commit їх ніщо не переперевіряло. Тому вони додатково
позначені категорією `ReleaseEvidence` і запускаються окремим кроком **до `dotnet pack`**
(≈2 хв на обидва), а їхній `.trx` потрапляє в evidence bundle як `exhaustive-evidence.json`. Без
нього `-RequireAll` валить job. Повна категорія `Exhaustive` крутиться щоночі —
[`exhaustive.yml`](.github/workflows/exhaustive.yml).

```bash
# локально, те саме що робить gate:
dotnet test LogicalOptimizer.sln -c Release --filter "Category=ReleaseEvidence"
```

**Supply-chain гарантії кожного релізу:**

- **детермінований build за фіксованого toolchain** — `ContinuousIntegrationBuild=true`
  автоматично на CI (див. [`Directory.Build.props`](Directory.Build.props)) нормалізує шляхи до
  джерел і прибирає machine-specific вміст із compiler output. Це означає: той самий коміт,
  зібраний **тією самою версією SDK на тій самій ОС із тим самим dependency graph**, дає
  ідентичний вихід. Це *не* повна reproducible-build гарантія — сам по собі прапорець не фіксує
  версію SDK, ОС і native tooling, тому bit-for-bit збіг між різними середовищами не заявляється.
  Що дійсно перевіряється для кожного релізу — `SHA256SUMS.txt` (байти, які workflow зібрав і
  запушив, тобто **до** repository signature, яку nuget.org додає вже на своєму боці) і build
  provenance attestation (ким і з якого коміту зібрано);
- **symbol-пакети** — `.snupkg` поруч із кожним `.nupkg` (`dotnet nuget push` вантажить їх
  автоматично) + SourceLink, тож споживач може зайти дебагером у код;
- **package validation** — статичні перевірки SDK під час `pack`;
- **SHA-256 checksums** — `artifacts/SHA256SUMS.txt`, разом із пакетами прикріплюється до run;
- **build provenance attestation** — підписане твердження, що ці байти зібрані цим workflow із
  цього коміту.

  > **Увага: nuget.org repository-підписує кожен пакет**, дописуючи ~13 КБ, тому SHA-256 копії
  > з nuget.org **не збігається** з атестованим. `gh attestation verify` на такій копії падає з
  > голим `HTTP 404` — виглядає як відсутня атестація, хоча насправді це інший дайджест. Саме
  > тому release-крок чіпляє `.nupkg`/`.snupkg` і `SHA256SUMS.txt` до GitHub release: атестація,
  > checksums і package-contract audit працюють з **цими** байтами, а копію з nuget.org
  > перевіряють через `dotnet nuget verify` (вона доводить repository signature й власника).
  ```bash
  gh release download v3.2.1 --pattern '*.nupkg'
  gh attestation verify LogicalOptimizer.3.2.1.nupkg --repo AlexanderV/LogicalOptimizer
  dotnet nuget verify logicaloptimizer.3.2.1.nupkg   # для копії з nuget.org
  ```
- **Trusted Publishing (OIDC)** — довготривалого API-ключа не існує взагалі.

Підпис самих NuGet-пакетів author-сертификатом не робимо: для цього потрібен code-signing
сертифікат. Provenance attestation + checksums дають публічно перевірюване походження без нього.

**Audit вмісту пакетів (перед публікацією):** крок «Verify package contract» запускає
[`tools/verify_package_contract.ps1`](tools/verify_package_contract.ps1), який **відкриває кожен
`.nupkg` як zip** і перевіряє контракт: власний README всередині пакета (і що він згадує саме цей
пакет), змістовний і **унікальний** `Description`, теги, `PackageProjectUrl` і repository
url/type/commit, SPDX-вираз ліцензії, `.snupkg` із `.pdb` на кожен TFM, наявність контрактних
target frameworks у `lib/` (і `tools/` + `DotnetToolSettings.xml` + `packageType=DotnetTool` для
CLI), відсутність будь-якої **сторонньої** runtime-залежності, і що meta-пакет транзитивно тягне
всі library-пакети. Запускається до `nuget push` — опублікований пакет уже не видалити. Той самий
audit працює і в CI на кожному PR (там же артефакт `package-contract-report`).

```bash
dotnet pack LogicalOptimizer.sln -c Release -o artifacts /p:Version=3.1.0
pwsh tools/verify_package_contract.ps1 -Version 3.1.0
# аудит уже опублікованих пакетів: завантаж їх у теку й вкажи -ArtifactsPath
```

Звіт — `package-contract-report.json` (machine-readable, з переліком того, чого він **не**
доводить). Наразі: 9 пакетів, 161 перевірка.

**Автоматична перевірка присутності:** після push крок «Verify packages on nuget.org» запускає
[`tools/verify_nuget.ps1`](tools/verify_nuget.ps1), який опитує flat-container-індекс
(`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) для всіх 7 пакетів на випущену версію,
з retry+backoff (індексація має лаг, до ~10 спроб по 30 с). Якщо якийсь пакет так і не з'явиться —
workflow падає. Скрипт можна запустити й локально проти вже опублікованої версії:

```bash
pwsh tools/verify_nuget.ps1 -Version 2.1.0
# швидкий разовий чек без очікування: -MaxAttempts 1
```

**Автоматичний installation smoke test:** останній крок workflow запускає
[`tools/smoke_install.ps1`](tools/smoke_install.ps1) — він створює тимчасовий проєкт **поза
репозиторієм** (щоб `Directory.Build.props` і project-references не впливали), ставить
опублікований пакет саме з nuget.org, проганяє оптимізацію через публічний API з перевіркою
`IsEquivalent()`/`MinimizationStatus`, тоді ставить CLI як global tool і перевіряє його
`--format=json` звіт. Присутність в індексі ще не означає придатність — цей крок доводить її.

З `-IncludeAot` той самий консьюмерський проєкт додатково збирається з `PublishAot=true` і
**запускається як нативний бінарник**. [`aot.yml`](.github/workflows/aot.yml) доводить
AOT-сумісність лише через project reference — це не ловить поломку на рівні пакування, тож реліз
перевіряє AOT саме **з опублікованого пакета** (звіт: `aot-package-smoke.json`). Потрібен нативний
toolchain: clang + zlib headers на Linux, MSVC build tools на Windows (локально — з Developer
Command Prompt, інакше `link.exe` не знайдеться).

```bash
# локально проти вже опублікованої версії:
pwsh tools/smoke_install.ps1 -Version 3.1.0
# без частини з global tool:
pwsh tools/smoke_install.ps1 -Version 3.1.0 -SkipTool
# + Native AOT з пакета:
pwsh tools/smoke_install.ps1 -Version 3.1.0 -IncludeAot -AotReportPath aot-package-smoke.json
```

**Release evidence bundle:** останній крок збирає
[`tools/build_evidence_bundle.ps1`](tools/build_evidence_bundle.ps1) — усі докази релізу в одній
теці: audit пакетів, перевірка індексу, AOT-звіт, `test-summary.json` (з `.trx`), `SHA256SUMS.txt`,
`claim-changes.md` (секція CHANGELOG цієї версії) і `verifying-provenance.md` — інструкція, як
**самостійно** перевірити attestation, checksums, контракт пакетів, install/AOT і JSON-схему CLI.
`INDEX.md` каже, що кожен файл доводить (і чого **не** доводить), `manifest.json` дублює це
machine-readable із SHA-256 кожного файлу. `-RequireAll` валить job, якщо якогось ключового доказу
немає, тож bundle не може виглядати повним, коли він неповний. Bundle вантажиться як run-артефакт
`release-evidence-<version>` і, якщо GitHub release для тега вже існує, прикріплюється до нього.

```bash
# локальний dry run (без -RequireAll відсутні входи просто позначаються 'absent'):
pwsh tools/build_evidence_bundle.ps1 -Version 3.1.0 -PackageContractReport package-contract-report.json
```

**Перевірка:** вкладка **Actions** → run «Release»; за ~5–15 хв пакети на nuget.org.

## Версіювання та порядок тегів

- **Завжди push `main` → потім push тег** (щоб гілка й docs-сайт були актуальні; docs-workflow
  тригериться на push у `main`).
- Поточна dev-версія — **3.2.2** (у [`Directory.Build.props`](Directory.Build.props)). Патч 3.2.2
  не змінює публічний API (`PublicApi.approved.txt` ідентичний до `v3.2.1`) — це перформанс
  точної мінімізації та виправлення релізного конвеєра. Мінор 3.2
  додає публічний API адитивно (`OptimizationTrace`, `TryParse`/`ParseDiagnostic`,
  `FormulaParseException`); мажор 3.0
  увімкнув AIG DAG-aware rewriting за замовчуванням + доведено-мінімальну min-AIG бібліотеку
  (`EnableAigRewriting=false` повертає до-3.0 поведінку).
- **Історична примітка щодо тегів:** BDD complement edges (C1) влилися комітом `247afcd`. Якщо
  потрібен окремий реліз-міграція **без** complement edges, тегни `v2.0.0` на коміті `9090092`
  (останній перед C1), а `v2.1.0` — на `HEAD`. Якщо це не потрібно — просто випусти все одним
  тегом `v2.1.0` (перший опублікований реліз міститиме і міграцію, і complement edges).
- Перед новим мінорним/мажорним тегом: онови `<Version>` у
  [`Directory.Build.props`](Directory.Build.props) (одне місце) і додай запис у
  [CHANGELOG.md](CHANGELOG.md).
- **`dotnet nuget push` по глобу не атомарний.** Він публікує пакети послідовно й припиняє роботу
  на першій помилці — тобто відмова на одному пакеті лишає попередні вже опублікованими, а
  наступні ненадісланими. Саме так сталося з 3.2.0: nuget.org відхилив порожній
  `LogicalOptimizer.Full.3.2.0.snupkg` (`400`, немає жодного `.pdb`), і 3 пакети пішли в реєстр, а
  6 — ні; опубліковані посилалися на залежності, яких не існує. Відкотити це неможливо, лише
  unlist + новий номер версії. Тому «Verify package contract» — це справжній gate: усе, що
  nuget.org може відхилити, треба ловити **до** `push`.

## Після релізу

- Пакети не видаляються з nuget.org — лише «unlist». Тому ID і номер версії мають бути правильні
  до першого push (`--skip-duplicate` не перезаписує вже опубліковану версію).
- DocFX-сайт: `https://AlexanderV.github.io/LogicalOptimizer/` (деплой при push у `main`).
