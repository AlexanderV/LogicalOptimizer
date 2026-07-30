# Adoption feedback: how use cases shape the roadmap

LogicalOptimizer collects **no telemetry**. The library makes no network calls, and no package
phones home — that is a property the [security scope](../SECURITY.md) depends on and it is not
going to change.

The cost of that choice is that nothing is known about how the toolkit is actually used unless
someone says so. So the only input to roadmap decisions is the
[**use-case report**](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=use_case_report.yml)
issue form.

## Why this is not a formality

Several planned capabilities are deliberately **not built** until a real workload shows they are
needed — a compiled evaluator, batch evaluation, reusable BDD/d-DNNF query objects, and additional
algorithmic engines. The reasoning is that an
API built for a guessed workload is worse than no API: it fixes the wrong shape, has to be
supported forever under the compatibility policy, and crowds out the design that a real workload
would have suggested.

That makes a single concrete report genuinely decisive. It is not a vote — it is the evidence a
gated item is waiting for.

**Reports where LogicalOptimizer lost are equally valuable.** "We used Z3 because we needed
arithmetic" tells us the scope boundary is holding. "We wrote our own because the API made this
awkward" tells us something fixable. Both are more actionable than praise.

## What is asked, and why

| Field | What it decides |
|---|---|
| Formula kind and typical / largest size | Which variable-count zone real work lands in, and therefore which engine and which budgets deserve attention. The exact-minimization guarantee stops at 10 variables; whether that matters depends entirely on this answer. |
| Operation frequency | Whether a compiled evaluator or batch API (D1) is worth its maintenance cost. "Once per deploy" and "per request on a hot path" lead to opposite decisions. |
| Which guarantees you depend on | What must never change. A guarantee nobody reads is a maintenance cost; one that gates a deploy pipeline is load-bearing. |
| Deployment constraints | Which of Native AOT, trimming, single-file, target frameworks and offline feeds are load-bearing rather than nice to have. Drives what the release verification must keep proving. |
| Why LogicalOptimizer, or why not | The scope boundary. If reports keep naming the same missing capability, the boundary is in the wrong place; if they name capabilities that are deliberately out of scope, it is in the right place. |
| What was missing or hard to find | Separates a genuine capability gap from a documentation gap — which need completely different fixes. |
| Citation permission | External case studies (N5) are the trust signal this project most lacks. Nothing is ever published without asking first, whatever the answer. |

## How reports are handled

1. Reports arrive as issues labelled `use-case`. They stay open while they are informing a decision
   — a use-case report is not a bug to be closed.
2. Anything actionable is split out into its own issue (a bug, a documentation fix, a feature
   request) and linked back, so the report itself is not used as a work item.
3. When a gated roadmap item is unblocked by reports, the CHANGELOG entry for the resulting work
   says which use cases motivated it. Decisions become traceable to their evidence, the same way
   claims are traceable to their tests in [CLAIMS.md](CLAIMS.md).
4. Aggregation is **manual and public**: the numbers in the table below are counted from the
   `use-case` label. There is no hidden dataset, and there is nothing to opt out of.

## Aggregated to date

No use-case reports have been received yet — the channel is new. This section is updated as they
arrive, and the counts are reproducible by anyone from the
[`use-case` label](https://github.com/AlexanderV/LogicalOptimizer/issues?q=label%3Ause-case).

| Signal | Count |
|---|---|
| Use-case reports received | 0 |
| Reports naming a capability gap | 0 |
| Reports naming a documentation gap | 0 |
| Reports where another tool was chosen | 0 |
| Roadmap items unblocked by a report | 0 |
| Consented external case studies | 0 |

Success targets for this channel: at least three actionable use cases per quarter, and at least
one consented external case study.

## Other channels

| You want to | Go to |
|---|---|
| Report a wrong result or a crash | [Bug report](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=bug_report.yml) |
| Ask for a specific capability | [Feature request](https://github.com/AlexanderV/LogicalOptimizer/issues/new?template=feature_request.yml) |
| Ask a question | [Discussions](https://github.com/AlexanderV/LogicalOptimizer/discussions) |
| Report a vulnerability | [SECURITY.md](../SECURITY.md) — privately, never as an issue |
