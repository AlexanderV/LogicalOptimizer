# Migrating from LogicalOptimizer 1.x to 2.0

v2.0.0 is a deliberate breaking release: the AST core became **n-ary and canonical**,
the ten `IOptimizer` classes were replaced by a single internal rewrite engine, and the
public API surface was narrowed from ~56 to 53 reviewed types. The facade behavior
contract (`BooleanExpressionOptimizer.OptimizeExpression`, zone routing, statuses,
budgets, verification guarantees) is unchanged.

## Quick "was → now" table

| v1 | v2 |
|---|---|
| `new AndNode(a, b)`, `new OrNode(a, b)` trees built by hand | `FormulaFactory` — `f.And(...)`, `f.Or(...)`, `f.Not(...)`, `f.Variable("a")`, `f.Parse("a & b")`, `f.Import(tree)` |
| `.Left` / `.Right` on `AndNode`/`OrNode` | `Operands` (`IReadOnlyList<AstNode>` on `NaryNode`); derived binary ops (`XorNode`, `ImpNode`, `EqvNode`, `NandNode`, `NorNode`) keep `Left`/`Right` |
| `ForceParentheses` display hint on `BinaryNode` | Removed. Rendering is purely precedence-based via `AstFormatter.Format(node)`; nodes are fully immutable |
| `new Parser(new Lexer(text).Tokenize()).Parse()` | `Parser`/`Lexer`/`Token`/`TokenType` are `internal`. Use `new FormulaFactory().Parse(text)` |
| Raw (as-written) parse tree | No raw tree exists: the parser builds through the factory, so every tree is canonical from birth (see below) |
| `BinaryDecisionDiagram` int-handle API (`Root`, `Ite`, `Compose`, `Restrict`, `Exists`, `ForAll`, `Negate`, int overloads) | Internal. Use the new parameterless root-based members: `IsTautology()`, `IsContradiction()`, `CountSatisfyingAssignments()`, `Evaluate(assignment)`, `EnumerateSatisfyingAssignments()`, `FindSatisfyingAssignment()`, plus the existing `Build*` / `AreEquivalent` |
| `CsvTruthTableParser.ParseCsvToPartialTable` returned a `ValueTuple` | Returns the typed `PartialTruthTable` (`Variables`, `OnSet`, `DontCareSet`) |
| `SatProofStep`, `SatSolver.Proof` | Internal. DRAT proofs are still available as text via `ToDrat()` / `EquivalenceChecker.CheckWithProof` |
| `TseitinConverter` | Internal. Use `Transformations.ToEquisatisfiableCnf` (facade) — it still returns the public `TseitinCnf` |
| `ExpressionOptimizer`, `IOptimizer`, the 10 optimizer classes, `AstUtilities` | Removed. Replaced by the internal `LogicalOptimizer.Rewrite` layer (`RewriteEngine` + local rules); not part of the public API |
| `AndInverterGraph` (public in Core) | Internal (used for multi-level metrics and balanced folding) |
| Node count = binary node count | N-ary cost model: one n-ary `AndNode`/`OrNode` counts as **1 node** regardless of operand count (see "Cost model" in the README) |

## Construction-time canonicalization

In v2 every `AndNode`/`OrNode` is built by `FormulaFactory`, which canonicalizes at
construction time:

- **flatten** — `a & (b & c)` becomes one `AndNode` with operands `[a, b, c]`;
- **sort** — operands are ordered by a stable canonical key (`c & a & b` → `a & b & c`);
- **dedup** — `a & a` → `a` (idempotence);
- **constant folding** — `a & 1` → `a`, `a | 1` → `1`, `!!a` → `a`;
- **complement folding** — `a & !a` → `0`, `a | !a` → `1`;
- **interning** — structurally equal factory-built trees are the *same instance*, so
  reference equality works for canonical trees.

Two consequences worth internalizing:

1. **Degenerate formulas fold to constants at parse time.** `f.Parse("a | !a")` returns
   the constant `1`, not an `OrNode`. Code that expected to inspect the "as written"
   structure must keep the input string instead.
2. **Output strings are canonically ordered.** `f.Parse("c & a & b").ToString()` is
   `"a & b & c"`. If you pinned v1 output strings in tests, re-pin them (semantics are
   unchanged and verified — only the form differs).

## Before / after snippets

Building a formula:

```csharp
// v1
var tree = new OrNode(new AndNode(new VariableNode("a"), new VariableNode("b")),
                      new VariableNode("c"));

// v2
var f = new FormulaFactory();
var tree = f.Or(f.And(f.Variable("a"), f.Variable("b")), f.Variable("c"));
// or simply:
var tree2 = f.Parse("a & b | c");
// factory interning: structurally equal trees are the same object
Console.WriteLine(ReferenceEquals(tree, tree2)); // True
```

Walking a conjunction:

```csharp
// v1: recursive Left/Right descent to collect conjuncts
void Collect(AstNode n, List<AstNode> acc)
{
    if (n is AndNode and) { Collect(and.Left, acc); Collect(and.Right, acc); }
    else acc.Add(n);
}

// v2: conjuncts are already flat
if (tree is AndNode and)
    foreach (var operand in and.Operands) { /* ... */ }
```

Parsing and rendering:

```csharp
// v1
var tokens = new Lexer("a & b | c").Tokenize();
var ast = new Parser(tokens).Parse();
Console.WriteLine(ast.ToString());

// v2
var f = new FormulaFactory();
var ast = f.Parse("a & b | c");
Console.WriteLine(AstFormatter.Format(ast)); // same as ast.ToString()
```

Derived operators (`Xor`/`Imp`/`Eqv`/`Nand`/`Nor`) are unchanged binary nodes with
`Left`/`Right`; they live outside the canonical core (pattern-recognition output and
extended display forms). `FormulaFactory.Import` decomposes them into And/Or/Not.

## Removed public types (no direct replacement)

`Lexer`, `Parser`, `Token`, `TokenType`, `AndInverterGraph` (Core);
`TseitinConverter`, `SatProofStep` (Sat) — all internalized;
`IOptimizer`, `ExpressionOptimizer`, `AstUtilities` and the ten optimizer classes
(`ConstantsOptimizer`, `AssociativityOptimizer`, `CommutativityOptimizer`,
`ComplementOptimizer`, `DeMorganOptimizer`, `AbsorptionOptimizer`,
`ConsensusOptimizer`, `RedundancyOptimizer`, `DistributiveOptimizer`,
`FactorizationOptimizer`) — deleted; their canonicalization duties moved into
`FormulaFactory` (constants, complement, associativity/flatten, commutativity/sort,
idempotence) and the rest became internal rules of the single-traversal
`RewriteEngine` (De Morgan/NNF, absorption, consensus, redundancy, factorization
with growth rollback).

If you were calling individual optimizers, call the facade instead:

```csharp
var result = new BooleanExpressionOptimizer().OptimizeExpression("a & b | a & c");
Console.WriteLine(result.Optimized); // a & (b | c)
```

## CNF / Tseitin changes

Tseitin and Plaisted–Greenbaum encode n-ary gates directly: an n-ary AND gate `g`
emits `n` clauses `(!g | x_i)` plus one clause `(g | !x_1 | ... | !x_n)` (OR is dual).
For formulas that were binary chains in v1 the CNF is identical; for wider gates v2
produces fewer auxiliary variables and clauses, never more. BLIF and Verilog exports
likewise emit n-ary gates.

## Versioning

v1.x remains available on NuGet. The public API of v2 is pinned member-by-member by
`ApiSurfaceTests` (`LogicalOptimizer.Tests/TestData/PublicApi.approved.txt`); future
breaking changes will again require a major version. See [CHANGELOG.md](CHANGELOG.md)
for the full release notes.
