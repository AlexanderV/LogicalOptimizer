# Migration to v2.0

v2.0.0 is a deliberate **breaking** release. The AST core became n-ary and canonical, the
ten `IOptimizer` classes were replaced by a single internal rewrite engine, and the public
API surface was narrowed to a reviewed, member-by-member-pinned type set. The facade **behavior** contract
(`BooleanExpressionOptimizer.OptimizeExpression`, zone routing, statuses, budgets,
verification guarantees) is **unchanged** — only construction and the type surface changed.

> The authoritative, full guide (with before/after snippets and the complete removed-type
> list) is [`MIGRATION-v2.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/MIGRATION-v2.md)
> in the repository root. This page summarizes it.

## The essential change: build through `FormulaFactory`

In v1 you built trees by hand (`new AndNode(a, b)`) and used `.Left` / `.Right`. In v2 the
canonical path is `FormulaFactory`, which canonicalizes at construction time (flatten, sort,
dedup, constant/complement folding, interning). The low-level `AndNode`/`OrNode` constructors
remain public for raw AST, but they skip canonicalization — go through `FormulaFactory`
whenever you rely on canonical form.

```csharp
// v1
var tree = new OrNode(new AndNode(new VariableNode("a"), new VariableNode("b")),
                      new VariableNode("c"));

// v2
var f = new FormulaFactory();
var tree = f.Or(f.And(f.Variable("a"), f.Variable("b")), f.Variable("c"));
// or simply:
var tree2 = f.Parse("a & b | c");
Console.WriteLine(ReferenceEquals(tree, tree2)); // True (interning)
```

## "Was → now" cheat sheet

| v1 | v2 |
|---|---|
| `new AndNode(a, b)` / `new OrNode(a, b)` for a *canonical* tree | `FormulaFactory` — `f.And(...)`, `f.Or(...)`, `f.Not(...)`, `f.Variable(...)`, `f.Parse(...)`, `f.Import(...)` (the raw constructors stay public but are non-canonical) |
| `.Left` / `.Right` on And/Or | `Operands` (`IReadOnlyList<AstNode>` on `NaryNode`); derived binary ops keep `Left`/`Right` |
| `ForceParentheses` display hint | Removed — rendering is purely precedence-based via `AstFormatter.Format(node)`; nodes are fully immutable |
| `new Parser(new Lexer(text).Tokenize()).Parse()` | `Parser`/`Lexer`/`Token`/`TokenType` are `internal` — use `f.Parse(text)` |
| `BinaryDecisionDiagram` int-handle API | Internal — use the parameterless root members (`IsTautology()`, `CountSatisfyingAssignments()`, `EnumerateSatisfyingAssignments()`, …) plus `Build*` / `AreEquivalent` |
| `TseitinConverter` | Internal — use `Transformations.ToEquisatisfiableCnf` (returns the public `TseitinCnf`) |
| The 10 optimizer classes, `IOptimizer`, `ExpressionOptimizer`, `AstUtilities` | Removed — call the facade `BooleanExpressionOptimizer.OptimizeExpression` |
| `AndInverterGraph` public in Core | Internal |
| Node count = binary node count | N-ary cost model: one n-ary And/Or = **1 node** regardless of operand count |

## Two behavioral consequences of canonicalization

1. **Degenerate formulas fold to constants at parse time.** `f.Parse("a | !a")` returns
   the constant `1`, not an `OrNode`. Keep the input *string* if you need the "as written"
   form.
2. **Output strings are canonically ordered.** `f.Parse("c & a & b").ToString()` is
   `"a & b & c"`. If you pinned v1 output strings in tests, re-pin them — the semantics are
   unchanged and verified; only the form differs.

## CNF / Tseitin

Tseitin and Plaisted–Greenbaum encode n-ary gates directly. For formulas that were binary
chains in v1 the CNF is identical; for wider gates v2 produces **fewer** auxiliary
variables and clauses, never more. BLIF and Verilog exports likewise emit n-ary gates.

## Versioning

v1.x remains available on NuGet. The v2 public API is pinned member-by-member by
`ApiSurfaceTests`; future breaking changes will again require a major version bump. See
[`CHANGELOG.md`](https://github.com/AlexanderV/LogicalOptimizer/blob/main/CHANGELOG.md) for
the full release notes.
