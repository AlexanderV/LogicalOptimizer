# Maintainers

## Current maintainers

| Name | GitHub | Areas |
|---|---|---|
| Oleksandr Panasenko | [@AlexanderV](https://github.com/AlexanderV) | All areas: core engines, CLI, packaging, releases, documentation |

Code ownership is declared in [.github/CODEOWNERS](.github/CODEOWNERS).

## Maintenance model

This is an open-source project maintained on a **best-effort basis** by a single maintainer.
There is no company or foundation behind it, and no commercial SLA — see
[SUPPORT.md](SUPPORT.md#response-expectations) for what that means in practice.

Work is prioritized in the order described in SUPPORT.md:

1. **Security reports** (see [SECURITY.md](SECURITY.md)) — handled first.
2. **Bug reports with a clear reproduction** — a wrong result, a false `MinimalProven`, a crash.
3. **Feature requests**, weighed against the project's deliberate
   [scope](README.md#choosing-a-tool).

When a new major version ships, the previous major line receives security and correctness
fixes for **12 months** (or until its last .NET target leaves Microsoft support, whichever
comes first) — the full policy is in
[SUPPORT.md](SUPPORT.md#support-window-for-the-previous-major).

## Response expectations

Realistically:

- **No SLA.** Issues and pull requests are answered as time allows, not within a guaranteed
  window. Security reports can expect an acknowledgement within a few days
  ([SECURITY.md](SECURITY.md)); everything else may take longer.
- Releases ship when there is something worth shipping, not on a fixed cadence.
- An unanswered issue is not a rejected issue — a clear reproduction
  ([what makes a report actionable](SUPPORT.md#what-makes-a-report-actionable)) is the best
  way to move one forward.

If your organization needs guaranteed response times, this project does not currently offer
them; that constraint is stated here so it can be priced in honestly rather than discovered
later.

## Becoming a maintainer

The path is through sustained contribution:

1. Contribute — fixes, tests, documentation — following [CONTRIBUTING.md](CONTRIBUTING.md).
2. Review other people's pull requests; demonstrated judgment on the project's conventions
   (pinned public API, verified claims, zero runtime dependencies) matters more than volume.
3. After a track record of quality contributions and reviews, the current maintainers may
   invite you to join; you can also ask by opening a discussion.

Maintainers are expected to uphold the project's compatibility and verification policies
(SUPPORT.md, [doc/CLAIMS.md](doc/CLAIMS.md)) — a maintainer's convenience never overrides a
published contract.

## Emeritus maintainers

None yet. Maintainers who step down are listed here with thanks.
