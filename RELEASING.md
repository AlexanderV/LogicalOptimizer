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

`release.yml`: setup .NET 10 → `dotnet build -warnaserror` → `dotnet test` (CI-фільтр) →
`dotnet pack` 9 пакетів → **checksums + provenance attestation** → `dotnet nuget push
--skip-duplicate` → **верифікація присутності в реєстрі** → **installation smoke test**.
Якщо тести впадуть — публікації не буде.

**Supply-chain гарантії кожного релізу:**

- **детермінований build** — `ContinuousIntegrationBuild=true` автоматично на CI (див.
  [`Directory.Build.props`](Directory.Build.props)), тому той самий коміт дає ідентичний вихід;
- **symbol-пакети** — `.snupkg` поруч із кожним `.nupkg` (`dotnet nuget push` вантажить їх
  автоматично) + SourceLink, тож споживач може зайти дебагером у код;
- **package validation** — статичні перевірки SDK під час `pack`;
- **SHA-256 checksums** — `artifacts/SHA256SUMS.txt`, разом із пакетами прикріплюється до run;
- **build provenance attestation** — підписане твердження, що ці байти зібрані цим workflow із
  цього коміту. Перевірити опублікований пакет:
  ```bash
  gh attestation verify LogicalOptimizer.3.1.0.nupkg --repo AlexanderV/LogicalOptimizer
  ```
- **Trusted Publishing (OIDC)** — довготривалого API-ключа не існує взагалі.

Підпис самих NuGet-пакетів author-сертификатом не робимо: для цього потрібен code-signing
сертифікат. Provenance attestation + checksums дають публічно перевірюване походження без нього.

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

```bash
# локально проти вже опублікованої версії:
pwsh tools/smoke_install.ps1 -Version 3.1.0
# без частини з global tool:
pwsh tools/smoke_install.ps1 -Version 3.1.0 -SkipTool
```

**Перевірка:** вкладка **Actions** → run «Release»; за ~5–15 хв пакети на nuget.org.

## Версіювання та порядок тегів

- **Завжди push `main` → потім push тег** (щоб гілка й docs-сайт були актуальні; docs-workflow
  тригериться на push у `main`).
- Поточна dev-версія — **3.1.0** (у [`Directory.Build.props`](Directory.Build.props)). Мажор 3.0
  увімкнув AIG DAG-aware rewriting за замовчуванням + доведено-мінімальну min-AIG бібліотеку
  (`EnableAigRewriting=false` повертає до-3.0 поведінку).
- **Історична примітка щодо тегів:** BDD complement edges (C1) влилися комітом `247afcd`. Якщо
  потрібен окремий реліз-міграція **без** complement edges, тегни `v2.0.0` на коміті `9090092`
  (останній перед C1), а `v2.1.0` — на `HEAD`. Якщо це не потрібно — просто випусти все одним
  тегом `v2.1.0` (перший опублікований реліз міститиме і міграцію, і complement edges).
- Перед новим мінорним/мажорним тегом: онови `<Version>` у
  [`Directory.Build.props`](Directory.Build.props) (одне місце) і додай запис у
  [CHANGELOG.md](CHANGELOG.md).

## Після релізу

- Пакети не видаляються з nuget.org — лише «unlist». Тому ID і номер версії мають бути правильні
  до першого push (`--skip-duplicate` не перезаписує вже опубліковану версію).
- DocFX-сайт: `https://AlexanderV.github.io/LogicalOptimizer/` (деплой при push у `main`).
