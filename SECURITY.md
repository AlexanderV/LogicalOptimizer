# Security Policy

## Supported versions

Security fixes land on the latest released minor version. Older minors are not patched
separately — upgrading within the current major line is always additive (see the
[versioning policy](README.md#versioning-policy)).

| Version | Supported |
|---|---|
| 3.x (current) | ✅ |
| 2.x | ❌ — upgrade via [MIGRATION-v2.md](MIGRATION-v2.md) |
| 1.x | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Report it privately through GitHub's
[Report a vulnerability](https://github.com/AlexanderV/LogicalOptimizer/security/advisories/new)
form, which opens a private security advisory. If that is unavailable to you, contact the
maintainer listed on the [GitHub profile](https://github.com/AlexanderV).

Please include:

- the affected package and version;
- the input that triggers the issue (a Boolean expression, DIMACS/WCNF/OPB file, or CSV table);
- what happens and what you expected;
- the impact you believe it has.

You can expect an acknowledgement within a few days. Fixes are released as a new patch or
minor version, and the advisory is published once a fix is available.

## Scope

This is a computational library with **zero production dependencies** and no network, file
execution, or reflection surface, so the realistic risk areas are:

- **untrusted input handling** — parsers (expression, CSV, DIMACS/WCNF/OPB) that crash in an
  unexpected way rather than reporting a clean, catchable error;
- **resource exhaustion** — input that defeats the documented limits and budgets
  (`ResourceBudget`, expression/variable/nesting caps, cancellation) to hang a process or
  exhaust memory;
- **incorrect results presented as proven** — any case where an optimization is returned as
  equivalence-verified or `MinimalProven` when it is not. Correctness claims are part of the
  security surface of this library: report these even if they need no "attacker".

Denial of service from deliberately huge inputs run *without* a budget is a documented
limitation, not a vulnerability — set a `ResourceBudget` and a `CancellationToken` when
processing untrusted formulas.

## Supply chain

Packages are published from a tagged commit by the
[Release workflow](.github/workflows/release.yml) using nuget.org **Trusted Publishing**
(OIDC) — there is no long-lived API key to steal. Releases are built deterministically, ship
SourceLink metadata and a separate `.snupkg` symbol package, carry SHA-256 checksums and
GitHub **build provenance attestations**, and their presence on nuget.org is verified
automatically after each publish. See [RELEASING.md](RELEASING.md) for the full flow.
