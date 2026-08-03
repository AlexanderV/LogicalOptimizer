# Decision record: single `net8.0` target for the library packages (OE-03)

**Status:** Accepted 2026-07-31 (maintainer decision after the over-engineering audit in
[DEEP_RESEARCH_REVIEW.md](../DEEP_RESEARCH_REVIEW.md), OE-03). Implemented immediately —
dropping a TFM asset is not a breaking change for consumers, so it does not wait for v4.0.

## Decision

The seven library projects (Core, Sat, Bdd, Dnnf, Formats, Minimization, facade) and the
Full meta-package target **`net8.0` only**. The CLI, tests, benchmarks, samples and the AOT
smoke project stay on `net10.0` — they are executables/harnesses, not shipped library
surface, and exercising the libraries from a net10 host is exactly the compatibility
direction consumers use.

## Evidence (measured 2026-07-31, this repository)

- **No target-specific code exists.** Zero `#if NET*` directives across all seven
  libraries; both TFMs compiled identical sources into functionally identical assemblies.
  The net10 asset offered no API, behavior or trimming difference over the net8 asset.
- **Build cost:** full no-incremental Release rebuild of the library chain took
  **27.9 s dual-TFM vs 9.8 s net8-only** (~3× on every local and CI build).
- **Package cost:** every library nupkg/snupkg carried a duplicate `lib/net10.0` asset
  with the same IL.
- **Compatibility:** a `net8.0` asset is consumed unchanged by net8, net9 and net10
  applications; JIT improvements come from the *runtime*, not the library's TFM. The
  Native-AOT smoke (a net10 app consuming the libraries) continues to run in CI and
  proves the direction that matters.

## Reversal criterion

Reintroduce a `net10.0` (or later) asset only together with the evidence OE-03 demanded:
a benchmark or compatibility test in this repo demonstrating a concrete net10-specific
API, trimming or performance benefit that the `net8.0` asset cannot deliver. The
demonstration belongs in this file's successor record.

## Consequences

- `tools/verify_package_contract.ps1` contracts `net8.0` as the only library framework.
- DocFX metadata extraction uses the `net8.0` build.
- README/CONTRIBUTING/docs state `net8.0` as the library target; the .NET 10 SDK is still
  required to build the repo (CLI/tests/benchmarks target `net10.0`).
