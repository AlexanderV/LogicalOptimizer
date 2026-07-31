# External SAT Solvers & the DIMACS Hand-off

The embedded CDCL solver is a complete feature set (assumptions, unsat cores, DRAT
proofs), not a competition-grade engine — dedicated solvers such as **CaDiCaL** and
**Kissat** are far ahead on hard industrial instances, and the toolkit does not pretend
otherwise (see [Choosing a Tool](choosing-a-tool.md)). What the toolkit *does* provide is
a clean exit: everything it produces speaks DIMACS, and an opt-in adapter seam
(`IExternalSatSolver`) lets you route a query through an external solver while parsing,
Tseitin encoding and counterexample decoding stay in-library. The default packages keep
zero third-party runtime dependencies — the seam is an interface you implement, not a
bundled binding.

## When to hand off

The [budgets & zones](budgets-and-zones.md) article defines the honest limits. The
signals that an instance has outgrown the embedded engines:

- A standalone equivalence check returns an inconclusive verdict
  (`AreEquivalent == null`) because the SAT conflict budget
  (`ResourceBudget.SatConflictLimit`, default 200 000 conflicts) ran out.
- `SatSolver.Solve` reports `SatResult.Unknown` after exhausting `maxConflicts`.
- Solve times grow past your latency budget even though verdicts still arrive.

Below those thresholds the embedded solver is usually the simpler choice: no process
management, no temp files, verdicts plus models plus unsat cores in one API.

## Exporting DIMACS

Three exporters produce a standard `p cnf` file (see [Export Formats](exporters.md)):

- `BooleanExpressionExporter.ToDimacs(expression)` — a semantically equivalent CNF of
  the formula itself; falls back to a Tseitin encoding when the plain CNF would explode.
- `BooleanExpressionOptimizer.ToEquisatisfiableCnf(expression).ToDimacs()` — the linear
  **Tseitin** CNF (`TseitinCnf`), with a comment header mapping DIMACS indices to your
  variable names:

  ```text
  c 1 = a
  c 2 = b
  c 3 = c
  c 4..6 = Tseitin auxiliary variables
  p cnf 6 10
  ...
  ```

- `ExternalSatProblem.ToDimacs()` — a raw clause list for the adapter seam below;
  assumptions are appended as unit clauses (plain DIMACS has no assumption syntax, and
  for a one-shot query the two are equivalent).

## Running CaDiCaL, Kissat or d4 on the file

```bash
cadical problem.cnf          # or: kissat problem.cnf
```

Both follow the SAT-competition conventions: exit code 10 for satisfiable, 20 for
unsatisfiable, an `s SATISFIABLE` / `s UNSATISFIABLE` verdict line, and on SAT one or
more `v ` lines listing the model as signed literals terminated by `0`.

For model **counting**, `d4` (or another `#SAT` tool) consumes the same file. Count on
the **Tseitin** encoding only (`CnfEncodingStyle.Tseitin`, the default): its gate
variables are functionally determined, so models correspond one-to-one with satisfying
input assignments. A Plaisted–Greenbaum encoding is equisatisfiable but does not
preserve the model count.

## Mapping results back

- Variable indices are 1-based; input variables come first in sorted name order,
  auxiliary `_tN` gate variables after (`TseitinCnf.VariableName(i)` decodes an index).
- `s UNSATISFIABLE` on a formula's CNF means the formula is unsatisfiable; on a miter
  (`left XOR right`) it means the two sides are equivalent.
- `s SATISFIABLE`: restrict the `v `-line literals to the input-variable indices —
  positive literal means `true` — and ignore the auxiliary variables.

## The adapter seam: `IExternalSatSolver`

Implement one method to plug a solver into the library's consumer paths:

```csharp
public interface IExternalSatSolver
{
    ExternalSatResult Solve(ExternalSatProblem problem, CancellationToken cancellationToken = default);
}
```

`ExternalSatProblem` carries the clauses (DIMACS literal convention) plus optional
assumptions; `ExternalSatResult` is one of `Satisfiable(model)` / `Unsatisfiable()` /
`Unknown()`, reusing the embedded solver's `SatResult` vocabulary. The contract is
deliberately one-shot — CNF in, verdict out — not an incremental IPASIR binding.

`ExternalSatEquivalenceChecker` is the in-library consumer: an
[`IEquivalenceChecker`](equivalence-and-backbones.md) that builds the miter and decodes
the counterexample exactly like the default backend, but sends the SAT query to your
adapter. It is opt-in only; nothing changes unless you construct it.

```csharp
using LogicalOptimizer;

IExternalSatSolver solver = /* your adapter, e.g. around a cadical executable */;
var checker = new ExternalSatEquivalenceChecker(solver);

var factory = new FormulaFactory();
var verdict = checker.Check(factory.Parse("a & b | a & c"), factory.Parse("a & (b | c)"));
// verdict.AreEquivalent == true — the miter came back UNSAT from the external solver

var differ = checker.Check(factory.Parse("a & b"), factory.Parse("a | b"));
// differ.AreEquivalent == false, differ.Counterexample decoded from the verified model
```

A complete reference adapter — write the DIMACS temp file, run the executable, parse the
competition output, degrade to `Unknown` on any failure — ships as a sample, not a
package dependency:
[`samples/LogicalOptimizer.Samples/Recipes/ExternalSolverHandOff.cs`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/samples/LogicalOptimizer.Samples/Recipes/ExternalSolverHandOff.cs).
Set `LOGICALOPTIMIZER_EXTERNAL_SOLVER` to a cadical/kissat path to run it for real; the
recipe falls back to an in-process adapter when no executable is configured.

## The trust model — what is verified, what is trusted

The seam is explicit about an asymmetry:

| External verdict | Treatment |
|---|---|
| `Satisfiable` + model | **Verified.** The model must satisfy the CNF (`ExternalSatProblem.IsSatisfiedBy`, linear in the clause count). A bogus model throws `InvalidOperationException` instead of becoming a fake counterexample. |
| `Unsatisfiable` | **Trusted.** There is no cheap refutation of an UNSAT claim; a lying solver can make non-equivalent formulas pass. |
| `Unknown` | Passed through as an inconclusive verdict (`AreEquivalent == null`). |

If an equivalence claim must be independently checkable, demand a proof: run the solver
with proof logging (e.g. `cadical problem.cnf proof.drat`) and check the
certificate with `drat-trim` out of band — the same mechanism the embedded solver offers
in-process via `EquivalenceChecker.CheckWithProof` and `SatSolver.ToDrat()`.

## Next steps

- [Resource Budgets & the Zone Model](budgets-and-zones.md) — the limits that tell you when to hand off.
- [Export Formats](exporters.md) — DIMACS and the other interchange formats.
- [Equivalence & Backbones](equivalence-and-backbones.md) — the embedded equivalence path the seam mirrors.
- [Choosing a Tool](choosing-a-tool.md) — where dedicated solvers are simply the right answer.
