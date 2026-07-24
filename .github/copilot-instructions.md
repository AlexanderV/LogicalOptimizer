# LogicalOptimizer AI Coding Instructions

## Project Overview
LogicalOptimizer is a C# .NET 10.0 boolean expression optimization engine that parses, optimizes, and transforms boolean expressions using abstract syntax trees (AST). The architecture follows a pipeline pattern: Lexer → Parser → AST → Optimization → Normal Forms, with an exact Quine–McCluskey backend for small expressions.

## Core Architecture Patterns

### AST-Based Processing
- All expressions are parsed into `AstNode` trees with concrete types: `AndNode`, `OrNode`, `NotNode`, `VariableNode`, `ConstantNode` (0/1), plus display-only extended nodes (`XorNode`, `ImpNode`, `EqvNode`, `NandNode`, `NorNode`)
- Structural properties (`Left`, `Right`, `Operand`, `Name`, `Value`) are immutable; `ForceParentheses` is a mutable display hint excluded from equality
- Every `AstNode` implements: `Clone()`, `ToString()`, `GetVariables()`, `Equals()`, `GetHashCode()`
- Use `AstUtilities.AreEqual()` for structural AST comparison, not reference equality
- Pattern: Create new nodes during optimization rather than mutating existing ones

### Two-Tier Optimization
- **≤ `MAX_EXACT_MINIMIZATION_VARIABLES` (10) variables**: `TruthTableMinimizer` (Quine–McCluskey + branch-and-bound cover) computes the guaranteed-minimal SOP/POS; the final answer is the cheapest of {pipeline output, factored min-SOP, min-SOP} by literal count
- **Larger expressions**: the rewrite pipeline alone, including bounded expand-reduce (distribute → simplify → accept only if cheaper)

### Optimizer Strategy Pattern
- Each optimization rule is an internal class implementing `IOptimizer`
- `ExpressionOptimizer` coordinates: DeMorgan → Constants → Absorption → Complement → Associativity → Consensus (per-node acceptance) → Redundancy → Commutativity → Factorization (global rollback)
- Iterate to convergence (max 20 iterations) with cycle detection on repeated states
- **Soundness guard**: for ≤12 variables the result is verified against the input's truth table; a mismatch rolls back to the input and records `SoundnessRollback` in metrics — rules must be sound on their own

### Truth Table Verification
- **Critical Pattern**: Every optimization must preserve logical equivalence
- Use `TruthTableAssert.AssertOptimizationEquivalence()` in optimization tests
- `OptimizerSoundnessTests` sweeps ALL 3-variable functions (and all 4-variable ones under `Category=Exhaustive`) — a new rule that loses minterms will fail there
- Display truth tables only for ≤6 variables; `TruthTable.MaxVariables` (20) is the hard generation cap

## Essential Development Workflows

### Building & Testing
```bash
dotnet build LogicalOptimizer.sln          # TreatWarningsAsErrors is on
dotnet test --filter "Category!=Exhaustive"  # regular suite
dotnet test --filter "Category=Exhaustive"   # all 65k 4-variable functions (~1-2 min)
dotnet test --filter "Category=Performance"  # timing-sensitive tests (excluded from CI)
dotnet test --collect:"XPlat Code Coverage"  # uses coverlet.runsettings
```

### CLI Testing & Debugging
```bash
dotnet run --project LogicalOptimizer -- "a & b | a & c"
dotnet run --project LogicalOptimizer -- --verbose "complex_expression"  # AST + metrics
dotnet run --project LogicalOptimizer -- --benchmark
dotnet run --project LogicalOptimizer -- --demo
```
(There is no `--test` mode; the xUnit suite is the only test runner.)

## Project-Specific Conventions

### Performance Constraints (Enforced by `PerformanceValidator`)
- Max expression length: 10,000 characters; max variables: 100; max nesting depth: 50
- Max optimization iterations: 20; max processing time: 10 seconds
- Exact minimization gate: 10 variables; equivalence-guard gate: 12 variables
- **Pattern**: Always validate constraints before processing

### Test Patterns
```csharp
[Theory]
[InlineData("input_expression", "expected_output")]
public void Test_OptimizationRule_ShouldWork(string input, string expected)
{
    // Critical: Use truth table verification, not string comparison
    TruthTableAssert.AssertOptimizationEquivalence(input, expected, _optimizer);
}
```
For rules that only matter above the QM gate, assert equivalence + a literal budget
(see `RuleCompletenessTests`), never exact strings.

### Factorization Implementation Pattern
- Core rules: `a & b | a & c = a & (b | c)`, `(a | b) & (a | c) = a | (b & c)`, subgroup factoring, all-pairs reverse factoring over flattened clause lists
- Use `FlattenOr()` and `FlattenAnd()` utilities to handle associativity
- Set `ForceParentheses = true` on created nodes to preserve display grouping

### Advanced Pattern Recognition
- Detect XOR: `a & !b | !a & b` → `a XOR b`; Implication: `!a | b` → `a → b`; Equivalence: `a & b | !a & !b` → `a ↔ b`
- Pattern-matching primitives live in `PatternRecognizer`; `AdvancedPatternDetector` adds the multi-term pair-scan strategy
- Only applied to expressions with ≤5 variables; display-only (never fed back into optimization)

## Integration Points

### Public API surface
`BooleanExpressionOptimizer` (+ `OptimizationOptions`/`OptimizationResult`), AST nodes, `Lexer`/`Parser`/`Token`, `TruthTable`, `TruthTableMinimizer`, exporters (`BooleanExpressionExporter`, `CSharpExpressionExporter`), `CsvTruthTableParser`, `OptimizationMetrics`. Everything else is `internal` (tests see it via `InternalsVisibleTo`).

### Command Line Interface
- `CommandLineProcessor` parses flags in any position; unknown `--flags` are rejected
- Modes: `--cnf`, `--dnf`, `--advanced`, `--verbose`, `--truth-table`, `--csv`, `--demo`, `--benchmark`, `--stress`
- CSV truth tables may be partial: unspecified rows are treated as don't-cares

### Export Formats
- `BooleanExpressionExporter` supports: DIMACS, BLIF, Verilog, Mathematical notation, LaTeX, CSV

### Metrics & Analytics
- `OptimizationMetrics` tracks node counts, iterations, elapsed time, applied rules (including `ExactMinimization` and `SoundnessRollback`)
- Request artifacts via `OptimizationOptions`; debug output is returned in `OptimizationResult.DebugInfo`, never written to the console by the library

## Critical Implementation Notes
- The consensus theorem only licenses removing a redundant term while BOTH parents remain — remove one term at a time, re-checking against the current list
- Two commutatively-equal terms absorb each other; keep exactly one survivor
- Optimization convergence is checked via `AreEqual()` plus a seen-state set; the iteration limit is the last resort
- Use `ArgumentException` for user input errors; normal-form blowup surfaces as `ComputationStatus.TooLarge`

When implementing new optimizers: implement `IOptimizer`, wire into `ExpressionOptimizer`, and add both classic unit tests and a soundness check — the exhaustive sweeps are the safety net that catches minterm loss.
