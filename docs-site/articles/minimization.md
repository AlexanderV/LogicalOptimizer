# Two-Level Minimization

`LogicalOptimizer.Minimization` provides exact and heuristic two-level minimization from a
truth-table specification: `TruthTableMinimizer` (Quine–McCluskey with covering-table
reductions and lower-bound-pruned branch-and-bound), CSV truth-table parsing, and
multi-output tables with shared cubes. Every example is asserted in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

A function is given as its **on-set**: the minterm indices where the output is 1. Minterm
bit *j* is the value of `variables[j]`, variables in the order you pass them.

## Minimal SOP and POS

```csharp
using LogicalOptimizer;

var variables = new[] { "a", "b", "c" };
var onSet = new[] { 3, 5, 6, 7 };   // 3-input majority: true when >= 2 inputs are 1

TruthTableMinimizer.MinimalSop(variables, onSet);  // a & b | a & c | b & c
TruthTableMinimizer.MinimalPos(variables, onSet);  // (b | c) & (a | c) & (a | b)
```

## Proven-minimal status

`MinimalSopWithStatus` also reports whether the cover search completed (so the result is
**provably** minimal) or hit a work budget:

```csharp
var (expression, provenMinimal) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);
Console.WriteLine(provenMinimal);   // True
Console.WriteLine(expression);      // a & b | a & c | b & c
```

See [Operation contracts & statuses](contracts-and-statuses.md) for what "provably
minimal" means and its variable-count zone.

## Don't-cares

An optional don't-care set lets the minimizer choose the cheapest cover:

```csharp
// on-set {1}, don't-care {3}: the minimal cover is just "a".
TruthTableMinimizer.MinimalSop(new[] { "a", "b" }, new[] { 1 }, new[] { 3 });  // a
```

## CSV truth tables

`CsvTruthTableParser` reads a CSV with variable columns and a `Result` / `Output` / `Value`
column. Values may be `0/1`, `true/false`, `t/f`, `yes/no`, `y/n`.

```csharp
// Straight to a DNF expression:
CsvTruthTableParser.ParseCsvToExpression("a,b,Result\n0,0,0\n0,1,1\n1,0,1\n1,1,0");
// (!a & b) | (a & !b)

// Rows absent from the CSV become don't-care minterms:
var partial = CsvTruthTableParser.ParseCsvToPartialTable("a,b,Result\n0,0,0\n0,1,1\n1,1,1");
partial.Variables;    // [a, b]
partial.OnSet;        // {2, 3}
partial.DontCareSet;  // {1}  (the missing a=1,b=0 row)
```

`ParseCsvToPartialTable` returns a `PartialTruthTable` (`Variables` / `OnSet` /
`DontCareSet`) you can feed straight into `TruthTableMinimizer`. Helpers
`CsvTruthTableParser.LooksLikeCsv`, `ParseCsvFileToExpression` and `GenerateExampleCsv`
round out the parser.

## Multi-output tables with shared cubes

Several output columns are minimized together, sharing product terms where cheaper:

```csharp
var table = CsvTruthTableParser.ParseCsvToMultiOutputTable(
    "a,b,Sum,Carry\n0,0,0,0\n0,1,1,0\n1,0,1,0\n1,1,0,1",
    new[] { "Sum", "Carry" });

table.Variables;                                  // [a, b]
table.Outputs.Single(o => o.Name == "Sum").OnSet;   // {1, 2}
table.Outputs.Single(o => o.Name == "Carry").OnSet; // {3}
```

`MultiOutputTable` exposes `Variables` and `Outputs` (each a `MultiOutputFunction` with
`Name`, `OnSet`, `DontCareSet`). On the CLI this is the `--outputs=Sum,Carry` flag (see
[CLI usage](cli-usage.md)).

## Next steps

- [Normal forms & transformations](normal-forms.md) — the heuristic `MinimizeDnfHeuristic` for large covers.
- [Operation contracts & statuses](contracts-and-statuses.md) — minimality statuses and zones.
