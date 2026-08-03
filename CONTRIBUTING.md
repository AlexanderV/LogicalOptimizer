# Contributing to LogicalOptimizer

Thanks for helping out. This project values **verified** behaviour over breadth: a change is
finished when the tests prove it, not when it happens to work.

## Getting set up

Requires the **.NET 10 SDK** (the library packages target `net8.0`; the CLI, tests and benchmarks target `net10.0`).

```bash
git clone https://github.com/AlexanderV/LogicalOptimizer.git
cd LogicalOptimizer
dotnet build
dotnet test --filter "Category!=Performance&Category!=Exhaustive"
```

## What CI checks

Reproduce the gate locally before opening a pull request — these are exactly the
[CI](.github/workflows/ci.yml) steps:

```bash
dotnet format LogicalOptimizer.sln --verify-no-changes    # formatting is enforced
dotnet build LogicalOptimizer.sln -c Release -warnaserror  # warnings are errors
dotnet test LogicalOptimizer.sln --filter "Category!=Performance&Category!=Exhaustive"
dotnet run --project samples/LogicalOptimizer.Samples      # recipes must still pass
```

Two test categories are excluded from the gate because they are timing-sensitive or slow;
run them locally when you touch the relevant area:

```bash
dotnet test --filter "Category=Performance"
dotnet test --filter "Category=Exhaustive"   # all 65k 4-variable functions, minutes
```

Coverage of the library is enforced at an 80% line floor in CI.

## Conventions that are easy to miss

- **Public API is pinned.** Adding or changing a public type or member fails
  `ApiSurfaceTests.PublicApi_MatchesApprovedBaseline` and
  `ArchitectureTests.PublicSurface_IsTheDocumentedSet`. That is deliberate: regenerate the
  baseline only when the change is intended, then review the diff.
  ```bash
  LOGICALOPTIMIZER_REGENERATE_API=1 dotnet test --filter "FullyQualifiedName~ApiSurfaceTests"
  ```
  Also extend the documented type list in `ArchitectureTests` and the public docs.
- **Snapshot (Verify) tests**: a mismatch writes `SnapshotTests.*.received.txt` next to the
  `.verified.txt`. Approve an intended change by replacing the verified file with the
  received one.
- **Characterization golden master**: regenerate with
  `LOGICALOPTIMIZER_REGENERATE_GOLDEN=1 dotnet test --filter "FullyQualifiedName~Characterization"`.
- **Package layering is enforced** by an architecture test: `Core` ← `Sat` ← `Minimization`,
  `Bdd`/`Dnnf`/`Formats` beside them, facade on top. Do not introduce an upward or cyclic
  reference.
- **Zero production dependencies.** Library packages must not take a runtime `PackageReference`
  (SourceLink is build-only). Test-only dependencies are fine.
- **AOT/trim safety**: library projects enable the trim/AOT analyzers with warnings as errors —
  keep the code reflection-free.
- **Documented examples are executed.** Every snippet in the README and `docs-site/` has a twin
  assertion in `LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`. When you change one,
  change the other.
- **Shared package metadata** (version, authors, license, repository) lives in
  [`Directory.Build.props`](Directory.Build.props) — not in the individual `.csproj`.

## Tests

The suite layers ten techniques (property-based, metamorphic, algebraic, differential,
fuzzing, characterization, snapshot, architecture, pairwise, mutation) — see
[doc/TESTING.md](doc/TESTING.md) for the map and the rationale per technique. For a new
behaviour, prefer a test that could fail for a real reason: an independent oracle, a
metamorphic relation, or a property, rather than restating the implementation.

Mutation testing on a focused file:

```bash
dotnet tool restore
cd LogicalOptimizer.Tests && dotnet stryker --mutate "**/YourFile.cs"
```

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`,
  `test:`, `build:`, `refactor:`, `perf:`), with a scope where it helps: `feat(cli): …`.
- Keep a pull request to one coherent change, and say how you verified it.
- Update [CHANGELOG.md](CHANGELOG.md) for user-visible changes.
- A breaking public-API change requires a major version bump — call it out explicitly.

## Reporting bugs and proposing features

Use the [issue templates](https://github.com/AlexanderV/LogicalOptimizer/issues/new/choose).
A bug report is most useful with the exact input expression, the observed and expected
output, and the package version. For questions and use-case discussion, see
[SUPPORT.md](SUPPORT.md).

By contributing you agree that your contribution is licensed under the
[Apache 2.0 License](LICENSE).
