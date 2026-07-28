# Реліз LogicalOptimizer

Публікація 6 NuGet-пакетів (`LogicalOptimizer.Core` / `.Sat` / `.Bdd` / `.Minimization` /
`LogicalOptimizer` / `LogicalOptimizer.Cli`) автоматизована: push тега `v*` запускає
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
4. **Доступність package ID** — перевір `https://www.nuget.org/packages/<ID>` для кожного з 6.
   Особливо голий `LogicalOptimizer`: якщо зайнятий кимось іншим — перейменуй `PackageId` у всіх
   csproj. Вільний ID (404) реєструється на твій акаунт при першому push.

## Випуск версії

Версія публікованих пакетів береться **з тега** (`/p:Version=${GITHUB_REF_NAME#v}`), тому
`<Version>` у csproj — це dev/fallback-значення.

```bash
# 0) КОД МАЄ БУТИ НА GITHUB (workflow робить checkout тега):
git push origin main

# 1) тег + push (приклад для 2.1.0):
git tag -a v2.1.0 -m "v2.1.0: BDD complement edges"
git push origin v2.1.0
```

`release.yml`: setup .NET 10 → `dotnet build -warnaserror` → `dotnet test` (CI-фільтр) →
`dotnet pack` 6 пакетів → `dotnet nuget push --skip-duplicate` → **верифікація присутності в
реєстрі**. Якщо тести впадуть — публікації не буде.

**Автоматична перевірка присутності:** після push крок «Verify packages on nuget.org» запускає
[`tools/verify_nuget.ps1`](tools/verify_nuget.ps1), який опитує flat-container-індекс
(`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) для всіх 6 пакетів на випущену версію,
з retry+backoff (індексація має лаг, до ~10 спроб по 30 с). Якщо якийсь пакет так і не з'явиться —
workflow падає. Скрипт можна запустити й локально проти вже опублікованої версії:

```bash
pwsh tools/verify_nuget.ps1 -Version 2.1.0
# швидкий разовий чек без очікування: -MaxAttempts 1
```

**Перевірка:** вкладка **Actions** → run «Release»; за ~5–15 хв пакети на nuget.org.
Smoke-тест:

```bash
dotnet new console -n Probe && cd Probe
dotnet add package LogicalOptimizer
dotnet tool install --global logical-optimizer
logical-optimizer "a & b | a & c"
```

## Версіювання та порядок тегів

- **Завжди push `main` → потім push тег** (щоб гілка й docs-сайт були актуальні; docs-workflow
  тригериться на push у `main`).
- Поточна dev-версія в csproj — **2.1.0** (містить BDD complement edges, C1).
- **Історична примітка щодо тегів:** BDD complement edges (C1) влилися комітом `247afcd`. Якщо
  потрібен окремий реліз-міграція **без** complement edges, тегни `v2.0.0` на коміті `9090092`
  (останній перед C1), а `v2.1.0` — на `HEAD`. Якщо це не потрібно — просто випусти все одним
  тегом `v2.1.0` (перший опублікований реліз міститиме і міграцію, і complement edges).
- Перед новим мінорним/мажорним тегом: онови `<Version>` у 6 csproj і додай запис у
  [CHANGELOG.md](CHANGELOG.md).

## Після релізу

- Пакети не видаляються з nuget.org — лише «unlist». Тому ID і номер версії мають бути правильні
  до першого push (`--skip-duplicate` не перезаписує вже опубліковану версію).
- DocFX-сайт: `https://AlexanderV.github.io/LogicalOptimizer/` (деплой при push у `main`).
