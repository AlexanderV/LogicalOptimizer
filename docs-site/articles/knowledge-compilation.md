# Knowledge Compilation & Model Counting

The `LogicalOptimizer.Dnnf` package compiles a boolean formula into a **d-DNNF**
(deterministic, decomposable Negation Normal Form) circuit. Compilation happens once;
afterwards every query — exact model counting, weighted model counting and model
enumeration — runs in time **linear in the compiled circuit size**.

## API

```csharp
using LogicalOptimizer;

AstNode formula = new FormulaFactory().Parse("(a | b) & (b | c)");

DnnfCircuit circuit = KnowledgeCompilation.CompileToDnnf(formula);

bool sat            = circuit.IsSatisfiable;     // has at least one model
System.Numerics.BigInteger count = circuit.CountModels();  // exact #SAT

// Weighted model count: per-variable (positive, negative) literal weights.
double weighted = circuit.WeightedModelCount(new Dictionary<string, (double, double)>
{
    ["a"] = (0.7, 0.3),
    ["b"] = (0.5, 0.5),
    ["c"] = (0.9, 0.1),
});

// Lazy enumeration over the original input variables.
foreach (IReadOnlyDictionary<string, bool> model in circuit.EnumerateModels())
{
    // ...
}
```

### Conditioning and evidence queries

Once compiled, a circuit can be queried under a **partial assignment** (evidence) without
recompiling the formula:

```csharp
var evidence = new Dictionary<string, bool> { ["a"] = true };

// #models consistent with the evidence — a single bottom-up pass, no new circuit.
System.Numerics.BigInteger given = circuit.CountModels(evidence);
double weightedGiven          = circuit.WeightedModelCount(weights, evidence);

// Or materialize a NEW circuit with the variables pinned; the original is untouched.
DnnfCircuit conditioned = circuit.Condition(evidence);
// conditioned.CountModels() == circuit.CountModels(evidence)
```

`Condition` keeps the **same variable universe** — `Variables` is unchanged and each pinned
variable stays in the model-count universe fixed to one value — so
`Condition(evidence).CountModels()` is exactly the number of the original circuit's models
consistent with `evidence`. Empty evidence reproduces the unconditioned query; a full
assignment yields `0` or `1`. Every evidence/assignment key must be one of the circuit's
`Variables`, otherwise an `ArgumentException` is thrown.

`CompileToDnnf(AstNode formula, int nodeBudget = 1_000_000, CancellationToken ct = default)`
caps the DAG size with `nodeBudget` (a `NodeBudgetExceededException` — a public
`InvalidOperationException` subtype — is thrown when it is exceeded) and honors the
cancellation token. Both are heuristic safety limits: knowledge
compilation can blow up on hard CNF, so treat them as guardrails, not guarantees of
tractability.

## How it works

The compiler is a **top-down decision-DNNF** compiler in the style of c2d / D4:

1. The formula is turned into its full (biconditional) **Tseitin CNF**. That encoding is
   equisatisfiable *and* equi-count over the input variables — every satisfying input
   assignment extends to exactly one assignment of the gate auxiliaries — so the model
   count of the whole CNF already equals the model count of the original formula over its
   inputs. No projection is needed for counting; enumeration simply drops the
   functionally-determined auxiliary variables.
2. The residual clause set is compiled recursively:
   - **Unit propagation** to a fixpoint yields a conjunction of implied literal nodes.
   - **Connected-component decomposition** partitions the remaining clauses by shared
     variables into a **decomposable AND** (each component is compiled independently).
   - A **decision** on a variable branches into `v` / `¬v`, forming a **deterministic OR**
     (the two branches are mutually exclusive on the decision variable).
   - **Component caching**, keyed by the normalized active clause set, turns the search
     into a shared DAG rather than an exponential tree.
3. The circuit is kept **smooth**: every variable in a node's scope is represented
   explicitly, so counting is a single bottom-up pass — literal → 1, decomposable AND →
   product of children, deterministic OR → sum of branches — with no gap/smoothing
   correction. Counts use `BigInteger`; weighted counts apply per-literal weights with the
   same recurrence.

`Variables` and `EnumerateModels` expose only the **original input variables**; the Tseitin
auxiliaries are projected away.

## Correctness

Exact model counting is only useful if it is exact. The d-DNNF count is checked against the
independent ROBDD oracle: for a large corpus of random and structured formulas,
`CompileToDnnf(f).CountModels()` must equal
`BinaryDecisionDiagram.BuildWithBestOrder(f).CountSatisfyingAssignments()` exactly, and
also matches full truth-table brute force on small formulas. Enumeration is cross-checked
against `FormulaAnalysis.EnumerateModels`.
