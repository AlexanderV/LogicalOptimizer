<!-- Thanks for the contribution. Keep this short — the checklist matters more than prose. -->

## What and why

<!-- What changes, and what problem it solves. Link the issue it closes, if any. -->

## How it was verified

<!-- The tests you added or ran. "It builds" is not verification. -->

## Checklist

- [ ] `dotnet format LogicalOptimizer.sln --verify-no-changes` passes
- [ ] `dotnet build LogicalOptimizer.sln -c Release -warnaserror` is clean
- [ ] `dotnet test --filter "Category!=Performance&Category!=Exhaustive"` is green
- [ ] Tests cover the new behaviour (and would fail without the change)
- [ ] Documented examples updated together with their twin assertions in `DocExamplesTests`
- [ ] `CHANGELOG.md` updated for user-visible changes

## Public API

- [ ] No public API change
- [ ] Public API changed — baseline regenerated deliberately (`LOGICALOPTIMIZER_REGENERATE_API=1`),
      the documented type list in `ArchitectureTests` updated, and the diff reviewed below

<!-- If the public API changed, paste the baseline diff and note whether it is additive
     (minor) or breaking (major). Breaking changes need an explicit call-out. -->
