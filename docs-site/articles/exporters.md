# Export Formats

The facade exports a formula to the formats used by SAT solvers, EDA tools, typesetting
and code generation. Every example is asserted in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`.

## `BooleanExpressionExporter`

```csharp
using LogicalOptimizer;

const string expression = "a & b | c";

BooleanExpressionExporter.ToDimacs(expression);                 // DIMACS CNF (SAT solvers)
BooleanExpressionExporter.ToBlif(expression, "my_circuit");     // BLIF (logic synthesis)
BooleanExpressionExporter.ToVerilog(expression, "my_module");   // Verilog module
BooleanExpressionExporter.ToMathematicalNotation(expression);   // c ∨ a ∧ b
BooleanExpressionExporter.ToLatex(expression);                  // c \lor a \land b
BooleanExpressionExporter.TruthTableToCsv("a & b");             // a,b,Result CSV
```

`ToDimacs` starts with a comment header and a `p cnf <vars> <clauses>` line; `ToBlif`
emits a `.model` / `.inputs` / `.outputs` / `.names` netlist; `ToVerilog` emits a
`module … endmodule` with intermediate `wire` assignments. `TruthTableToCsv` round-trips
through `CsvTruthTableParser` (see [minimization](minimization.md)).

## `CSharpExpressionExporter` — code generation

Turn an AST into runnable C#:

```csharp
var ast = new FormulaFactory().Parse("a & b | c");

CSharpExpressionExporter.ToExpression(ast);      // (c || (a && b))
CSharpExpressionExporter.GenerateLambda(ast);    // (a, b, c) => (c || (a && b))
CSharpExpressionExporter.GenerateMethod(ast);    // public static bool EvaluateExpression(bool a, bool b, bool c) { ... }
CSharpExpressionExporter.GenerateClass(ast);     // a full static evaluator class
```

`GenerateMethod` and `GenerateClass` take optional method/class-name arguments. The
generated code uses `&&` / `||` / `!` and one `bool` parameter per variable, in
alphabetical order.

## Next steps

- [Normal forms & transformations](normal-forms.md) — the CNF/DNF/ANF the DIMACS/BLIF exports build on.
- [CLI usage](cli-usage.md) — the same formats from the command line.
