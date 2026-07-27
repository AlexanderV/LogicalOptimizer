# LogicalOptimizer AI Coding Instructions

## Project Overview
LogicalOptimizer is a C# .NET 10.0 boolean expression optimization engine that parses, optimizes, and transforms boolean expressions using abstract syntax trees (AST). The architecture follows a pipeline pattern: `FormulaFactory.Parse` (canonicalizing parser) → n-ary AST → `RewriteEngine` → Normal Forms, with an exact Quine–McCluskey backend for small expressions. It is split into layered packages: `LogicalOptimizer.Core` (AST, factory, formatter, truth tables), `.Sat`, `.Bdd`, `.Minimization`, and the `LogicalOptimizer` facade.

## Core Architecture Patterns

### AST-Based Processing (n-ary since v2.0)
- The canonical core node types are `AndNode`/`OrNode` (sealed **n-ary** `NaryNode`s exposing `IReadOnlyList<AstNode> Operands`), `NotNode`, `VariableNode`, `ConstantNode` (0/1). Derived binary nodes (`XorNode`, `ImpNode`, `EqvNode`, `NandNode`, `NorNode`) derive from `BinaryNode` (`Left`/`Right`) and exist only for pattern-recognition display
- **Nodes are fully immutable** — there is no `ForceParentheses` flag (removed in v2.0); `Clone()` returns `this`; `GetHashCode` is cached in the constructor
- Every `AstNode` implements: `Clone()`, `ToString()`, `GetVariables()`, `Equals()`, `GetHashCode()`. Equality is structural and order-sensitive, but operand order is always canonical
- **Never `new` And/Or trees directly.** Build through `FormulaFactory` (`Parse`, `And`/`Or`/`Not`/`Variable`, `Import`), which flattens, sorts operands into canonical order, deduplicates, folds constants/complements and interns the result — so equal factory-built formulas are the same instance (compare with `Equals` or, for interned trees, `ReferenceEquals`)

### Two-Tier Optimization
- **≤ `MAX_EXACT_MINIMIZATION_VARIABLES` (10) variables**: `TruthTableMinimizer` (Quine–McCluskey + branch-and-bound cover) computes the guaranteed-minimal SOP/POS; the final answer is the cheapest of {pipeline output, factored min-SOP, min-SOP} by literal count
- **Larger expressions**: the rewrite pipeline alone, including bounded expand-reduce (distribute → simplify → accept only if cheaper)

### Rewrite Engine (rule pattern)
- The classic constant, complement, associativity/flatten, commutativity/canonical-order and idempotence laws are applied at **construction time** by `FormulaFactory`, so no tree they could fire on ever reaches the engine
- Each remaining rule is an internal `IRewriteRule` (De Morgan, absorption, consensus, redundancy, factorization) in the `LogicalOptimizer.Rewrite` namespace
- `RewriteEngine` coordinates a single-traversal fixpoint: per node DeMorgan → Absorption → Consensus (per-node acceptance) → Redundancy → Factorization (under a growth-rollback guard, compared by literals then nodes), plus bounded expand-reduce above the QM gate
- Iterate to convergence (max 20 iterations) with cycle detection via interned reference identity
- **Soundness guard**: for ≤12 variables the result is verified against the input's truth table (SAT miter beyond); a mismatch rolls back to the input and records `SoundnessRollback` in metrics — rules must be sound on their own

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
dotnet run --project LogicalOptimizer.Cli -- "a & b | a & c"
dotnet run --project LogicalOptimizer.Cli -- --verbose "complex_expression"  # AST + metrics
dotnet run --project LogicalOptimizer.Cli -- --benchmark
dotnet run --project LogicalOptimizer.Cli -- --demo
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
- Core rules: `a & b | a & c = a & (b | c)`, `(a | b) & (a | c) = a | b & c`, subgroup factoring, all-pairs reverse factoring over n-ary operand lists
- Operands are already flattened by the factory; use the list helpers in `AstPrimitives` (the ported successor of the deleted `AstUtilities`) when a rule needs to regroup them
- Display grouping is automatic: `AstFormatter` places parentheses by precedence, so rules just build the correct tree via the factory — there is no formatting flag to set

### Advanced Pattern Recognition
- Detect XOR: `a & !b | !a & b` → `a XOR b`; Implication: `!a | b` → `a → b`; Equivalence: `a & b | !a & !b` → `a ↔ b`
- Pattern-matching primitives live in `PatternRecognizer`; `AdvancedPatternDetector` adds the multi-term pair-scan strategy
- Only applied to expressions with ≤5 variables; display-only (never fed back into optimization)

## Integration Points

### Public API surface
Narrowed to a documented 53-type surface in v2.0 (pinned by `ApiSurfaceTests.PublicApi_MatchesApprovedBaseline` and `ArchitectureTests.PublicSurface_IsTheDocumentedSet`). Highlights: `BooleanExpressionOptimizer` (+ `OptimizationOptions`/`OptimizationResult`), the AST node types, `FormulaFactory`, `AstFormatter`, `AstVisualizer`, `TruthTable`, `TruthTableMinimizer`, exporters (`BooleanExpressionExporter`, `CSharpExpressionExporter`), `CsvTruthTableParser`, `OptimizationMetrics`, plus the Sat/Bdd/Minimization engine entry points. `Lexer`/`Parser`/`Token`/`TokenType`, `AndInverterGraph`, the `RewriteEngine`/rules and other internals are now `internal` (tests see them via `InternalsVisibleTo`); parse text with `FormulaFactory.Parse`. A change to this surface is a deliberate baseline edit (regenerate with `LOGICALOPTIMIZER_REGENERATE_API=1`) requiring a major version bump — see `MIGRATION-v2.md`.

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

When implementing new rewrite rules: implement `IRewriteRule` in the `LogicalOptimizer.Rewrite` namespace, wire into `RewriteEngine`, and add both classic unit tests and a soundness check — the exhaustive sweeps are the safety net that catches minterm loss. (If a simplification is really a canonicalization law, add it to `FormulaFactory` instead.)
