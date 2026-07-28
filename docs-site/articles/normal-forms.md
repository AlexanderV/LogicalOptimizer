# Normal Forms & Transformations

LogicalOptimizer produces every classic normal form. Outputs shown here are asserted in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

## CNF and DNF (from the optimizer)

`OptimizationResult` carries both an optimized **CNF** (conjunctive) and **DNF**
(disjunctive) form:

```csharp
var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");
Console.WriteLine(result.CNF);   // a & (b | c)
Console.WriteLine(result.DNF);   // a & b | a & c
```

On the CLI these are the `--cnf` and `--dnf` flags (see [CLI usage](cli-usage.md)).

## Algebraic Normal Form (Zhegalkin / Reed–Muller)

`Transformations.ToAlgebraicNormalForm` computes the canonical XOR-of-AND-monomials form
via a fast Möbius transform over the truth table (up to `TruthTable.MaxVariables`):

```csharp
var f = new FormulaFactory();

Transformations.ToAlgebraicNormalForm(f.Parse("a & !b | !a & b"));  // a XOR b
Transformations.ToAlgebraicNormalForm(f.Parse("a & b"));            // a & b
Transformations.ToAlgebraicNormalForm(f.Parse("a | b"));            // (a XOR b) XOR (a & b)
```

On the CLI this is the `--anf` flag.

## Subsumption and heuristic DNF minimization

```csharp
Transformations.SubsumeDnf(f.Parse("a | a & b"));   // a   (absorbed cube removed)
Transformations.SubsumeCnf(f.Parse("a & (a | b)")); // a   (absorbed clause removed)

// Espresso-style EXPAND / IRREDUNDANT / REDUCE on a cube list, sound by construction.
var minimized = Transformations.MinimizeDnfHeuristic(f.Parse("a & b | a & !b | !a & b"));
// equivalent to: a | b   (the 3-cube cover collapses to 2 literals)
```

`MinimizeDnfHeuristic` shrinks large DNF covers (40+ variables) where the exact minimizer
is out of range; each move is validated by exact cofactor tautology, so it never changes
the function.

## Equisatisfiable Tseitin CNF

For handing a formula to a SAT solver, distributive CNF can blow up exponentially.
`BooleanExpressionOptimizer.ToEquisatisfiableCnf` returns a linear-size `TseitinCnf` with
auxiliary variables instead:

```csharp
var cnf = new BooleanExpressionOptimizer().ToEquisatisfiableCnf("(a | b) & (b | c)");

Console.WriteLine(string.Join(",", cnf.InputVariables)); // a,b,c
Console.WriteLine(cnf.TotalVariableCount);               // 6
Console.WriteLine(cnf.AuxiliaryVariableCount);           // 3
Console.WriteLine(cnf.Clauses.Count);                    // 10
Console.WriteLine(cnf.VariableName(1));                  // a
Console.Write(cnf.ToDimacs());                           // p cnf 6 10 ...

// Feed it straight to the built-in solver:
SatSolver.FromCnf(cnf).Solve();                          // Satisfiable
```

The CLI exposes this as `--cnf-mode=tseitin`. The polarity-based Plaisted–Greenbaum style
(`CnfEncodingStyle.PlaistedGreenbaum`) cuts clause count up to ~2×.

## Truth tables

`TruthTable` builds an exhaustive table (up to 20 variables) with equivalence checks:

```csharp
var table = TruthTable.Generate("a & b");
Console.WriteLine(table.GetResultsString());   // 0001
Console.WriteLine(table.IsSatisfiable());      // True
Console.WriteLine(table.IsTautology());        // False

TruthTable.Generate("a | !a").IsTautology();          // True
TruthTable.AreEquivalent("a & b | a & c", "a & (b | c)"); // True
```

## Next steps

- [SAT solving](sat-solving.md) — consume the Tseitin CNF, or build clauses directly.
- [Minimization](minimization.md) — exact two-level SOP/POS and multi-output CSV.
- [Exporters](exporters.md) — DIMACS / BLIF / Verilog / LaTeX / C#.
