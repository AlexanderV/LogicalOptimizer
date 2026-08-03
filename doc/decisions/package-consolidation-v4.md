# Decision record: consolidate to two packages in v4.0 (OE-01 / OE-02)

**Status:** Accepted 2026-07-31 (maintainer decision after the over-engineering audit in
[DEEP_RESEARCH_REVIEW.md](../DEEP_RESEARCH_REVIEW.md), OE-01/OE-02). Ships in **v4.0.0**.

## Decision

From v4.0.0 the project publishes **two real packages**:

| Package | Contents |
|---|---|
| **LogicalOptimizer** | The whole library: all seven assemblies (Core, Sat, Bdd, Dnnf, Formats, Minimization, facade) in one package. The single answer to "what do I install". |
| **LogicalOptimizer.Cli** | The `logical-optimizer` dotnet tool, unchanged. |

The seven previous library/meta IDs — `LogicalOptimizer.Core`, `.Sat`, `.Bdd`, `.Dnnf`,
`.Formats`, `.Minimization`, `.Full` — continue to be published at 4.x as **deprecated
forwarding packages**: no assemblies, a single exact dependency on `LogicalOptimizer
[same version]`, and a README/description that says to switch. Existing consumers who
upgrade keep compiling without edits (all types stay in the `LogicalOptimizer` namespace
and the forwarded package pulls the full library). After a transition period (at least
one minor release line), the forwarding IDs are marked *deprecated* on nuget.org (the
listing flag is set through the nuget.org UI/API after publish — recorded here because
no workflow can do it at pack time) and stop receiving new versions.

## Evidence

- **Downloads (nuget.org, 2026-07-31, all-time, latest 3.2.2):** facade 206 · Core 219 ·
  Sat 209 · Bdd 195 · Dnnf 172 · Formats 102 · Minimization 225 · Cli 204 · Full 135.
  Near-uniform counts tracking releases = mirrors/scanners + the project's own restores,
  not independent consumers choosing granular packages. Nobody demonstrably installs
  `LogicalOptimizer.Sat` without the rest.
- **The split served architecture, not users** — and the architecture does not need NuGet
  boundaries: the acyclic layering is enforced by `ArchitectureTests` (ArchUnitNET) over
  the seven *assemblies*, which all survive unchanged inside the single package.
- **Cost of the 9-package matrix:** 9× nuspec metadata, per-package READMEs,
  contract-verification rows, release/pack steps, docs tables — all maintained for
  granularity no one used.
- **Two aggregation points confused the entry path** (OE-02): `LogicalOptimizer` (facade,
  4 deps) vs `LogicalOptimizer.Full` (everything). v4.0 has one: `LogicalOptimizer` IS
  everything; `Full` forwards to it.

## What does not change

- Project/assembly structure, namespaces, and the pinned public API surface: the
  consolidation is a *packaging* change. `PublicApi.approved.txt` and
  `ApiSurfaceTests` continue to pin the same types and members.
- The CLI tool package.
- The dependency-free claim: the single package still has zero third-party dependencies.

## Migration (for release notes)

- `dotnet add package LogicalOptimizer` — the only install for library use.
- References to `LogicalOptimizer.Core/.Sat/.Bdd/.Dnnf/.Formats/.Minimization/.Full` keep
  working at 4.x via forwarding; replace them with `LogicalOptimizer` at leisure.

## Reversal criterion

Split a component back out only when a concrete external consumer needs it in isolation
(e.g. a documented dependency-size or trust-boundary requirement) — the criterion from
the audit: an external consumer, a distinct security/correctness contract, or a measured
operational benefit.
