# Реліз LogicalOptimizer

Публікація 6 NuGet-пакетів (`LogicalOptimizer.Core` / `.Sat` / `.Bdd` / `.Minimization` /
`LogicalOptimizer` / `LogicalOptimizer.Cli`) автоматизована: push тега `v*` запускає
[`.github/workflows/release.yml`](.github/workflows/release.yml), який білдить, тестує, пакує й
пушить на nuget.org.

## Одноразове налаштування

1. **NuGet API-ключ** — nuget.org → аватар → **API Keys** → **Create**:
   - Scope: `Push` → **Push new packages and package versions** (потрібно для нових ID).
   - Glob Pattern: `LogicalOptimizer*`.
2. **Секрет GitHub** — Settings → Secrets and variables → **Actions** → New repository secret:
   - Name: **`NUGET_API_KEY`** (точно так — workflow читає `secrets.NUGET_API_KEY`).
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
`dotnet pack` 6 пакетів → `dotnet nuget push --skip-duplicate`. Якщо тести впадуть —
публікації не буде.

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
