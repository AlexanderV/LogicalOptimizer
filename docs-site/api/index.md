# API Reference

This reference is generated from the XML documentation comments of the seven assemblies
that ship inside the single **LogicalOptimizer** package (since v4.0). All public types
live in the `LogicalOptimizer` namespace.

| Assembly | Public surface highlights |
|---|---|
| **LogicalOptimizer.Core** | `FormulaFactory` (parse + canonicalize), the n-ary AST (`AstNode`, `AndNode`, `OrNode`, `NotNode`, `VariableNode`, `ConstantNode`, plus derived `XorNode`/`ImpNode`/`EqvNode`/`NandNode`/`NorNode`), `AstFormatter`, `TruthTable`, `PerformanceValidator`, `ResourceBudget` |
| **LogicalOptimizer.Sat** | `SatSolver` (CDCL), CNF encodings and the public `TseitinCnf` result, `CardinalityEncoder`, `PseudoBooleanEncoder`, `MaxSatSolver` |
| **LogicalOptimizer.Bdd** | `BinaryDecisionDiagram` (ROBDD: model counting, quantification, restriction, composition, variable-order optimization) |
| **LogicalOptimizer.Dnnf** | `KnowledgeCompilation` / `DnnfCircuit` (d-DNNF compiler: exact `#SAT` model counting, weighted model counting, model enumeration) |
| **LogicalOptimizer.Formats** | DIMACS CNF / WCNF / OPB parsers and writers, truth-table CSV import (`CsvTruthTableParser`), BLIF/Verilog/C#/LaTeX exporters, `AstVisualizer` |
| **LogicalOptimizer.Minimization** | Quine–McCluskey, SAT prime-cover and Espresso-lite minimizers, multi-output CSV tables (`PartialTruthTable`) |
| **LogicalOptimizer** (facade) | `BooleanExpressionOptimizer`, `OptimizationResult`, `OptimizationOptions`, `MinimizationStatus`, `ComputationStatus`, `EquivalenceChecker`, `FormulaAnalysis`, `Transformations`, `BooleanExpressionExporter` |

Two kinds of published packages deliberately have **no** API reference here:
**LogicalOptimizer.Cli** is a `dotnet tool` driven from the shell (its contract is the
command line and the JSON report schema — see the CLI article and `schema/`), and the
deprecated pre-4.0 forwarding shells (`.Core` / `.Sat` / `.Bdd` / `.Dnnf` / `.Formats` /
`.Minimization` / `.Full`) ship no code, only a dependency on `LogicalOptimizer`.

Browse the full member-level reference from the table of contents on the left.

> [!TIP]
> The complete public surface is pinned member-by-member by an approval test
> (`ApiSurfaceTests`) and by an architecture rule that fixes the documented type
> list. Any breaking change to it requires a major version bump.
