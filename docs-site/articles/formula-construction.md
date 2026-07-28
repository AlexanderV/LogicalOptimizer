# Formula Construction & the AST

Every example on this page is mirrored by a test in
`LogicalOptimizer.Tests/Documentation/DocExamplesTests.cs`, so the outputs shown are the
real, asserted values.

## `FormulaFactory` — the single construction entry point

Since v2.0 there is exactly **one** way to build And/Or trees and to parse text:
`FormulaFactory`. It canonicalizes at construction time — flatten, sort, dedup, fold
constants/complements, and intern — so equal formulas print identically and are the same
instance.

```csharp
using LogicalOptimizer;

var f = new FormulaFactory();

// Parsing produces a canonical, n-ary AST.
Console.WriteLine(f.Parse("c & a & b"));   // a & b & c   (operands sorted)

// Degenerate formulas fold to a constant at parse time.
Console.WriteLine(f.Parse("a | !a"));      // 1

// Structurally equal factory trees are reference-equal (interning).
var parsed = f.Parse("c & a & b");
var built  = f.And(f.Variable("a"), f.Variable("b"), f.Variable("c"));
Console.WriteLine(ReferenceEquals(parsed, built));   // True
Console.WriteLine(((AndNode)parsed).Operands.Count); // 3  (n-ary, flattened)
```

The factory's building blocks: `Parse`, `Variable`, `Not`, `And`, `Or`, `Xor`,
`Implication`, `Equivalence`, the constants `True` / `False`, and `Import` (which
re-canonicalizes an externally built node, decomposing derived operators into And/Or/Not).

## The n-ary AST

The canonical core is `And` / `Or` / `Not` / `Variable` / `Constant`. `AndNode` and
`OrNode` are **n-ary** (`NaryNode` with `IReadOnlyList<AstNode> Operands`); one n-ary node
counts as **1 node** in the cost model regardless of operand count. The derived binary
nodes `XorNode` / `ImpNode` / `EqvNode` / `NandNode` / `NorNode` live *outside* the
canonical core and are used for extended-syntax parsing and pattern-recognition display.

Every `AstNode` exposes `Clone()`, `GetVariables()`, structural `Equals`/`GetHashCode`,
and `ToString()` (which renders through `AstFormatter`).

## `AstFormatter` — precedence-based rendering

A single renderer inserts parentheses exactly where precedence requires:

```csharp
Console.WriteLine(AstFormatter.Format(f.Parse("!(a & b) | c")));  // c | !(a & b)
```

## `AstMetrics` — structural size

```csharp
var ast = f.Parse("a & (b | c)");
AstMetrics.CountNodes(ast);      // 5
AstMetrics.CountLiterals(ast);   // 3
AstMetrics.CountOperators(ast);  // 2
AstMetrics.GetDepth(ast);        // 3
```

## `AstVisualizer` — debugging trees

`AstVisualizer.VisualizeTree(node)` returns an indented tree dump and
`AstVisualizer.GetCompactVisualization(node)` a one-line form — both useful for debugging
and teaching.

## Next steps

- [Optimizer & options](optimizer-and-options.md) — turn a formula into its optimized form.
- [Normal forms & transformations](normal-forms.md) — CNF, DNF, ANF, Tseitin.
- [Packages & architecture](packages-and-architecture.md) — where `FormulaFactory` sits.
