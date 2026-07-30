# LogicalOptimizer samples

Five small, self-contained recipes showing how to solve a real task with the public API.
Each recipe verifies its own output, so running the project is also a test: it exits
non-zero if any recipe stops producing the result described here.

```bash
dotnet run --project samples/LogicalOptimizer.Samples
```

The project is part of the main solution (so CI builds and runs it), and there is a
standalone `samples/LogicalOptimizer.Samples.sln` you can open on its own. A consumer would
reference the published NuGet packages; the samples use project references to the same
public API.

## Recipes

| # | Recipe | Practical task | Key API |
|---|--------|----------------|---------|
| 1 | [Feature configuration validation](LogicalOptimizer.Samples/Recipes/FeatureConfigurationValidation.cs) | Is a product/feature configuration valid? Get one, and find the choices forced in every valid configuration. | `FormulaAnalysis.EnumerateModels`, `ComputeBackbone` |
| 2 | [Business-rule regression check](LogicalOptimizer.Samples/Recipes/BusinessRuleRegressionCheck.cs) | Did refactoring a rule change its behaviour? Prove equivalence, or get a counterexample. | `EquivalenceChecker.Check` |
| 3 | [Count valid configurations](LogicalOptimizer.Samples/Recipes/CountValidConfigurations.cs) | Count exactly how many configurations a feature model allows (with a BDD ↔ d-DNNF cross-check). | `KnowledgeCompilation.CompileToDnnf`, `BinaryDecisionDiagram` |
| 4 | [Optimize generated conditions](LogicalOptimizer.Samples/Recipes/OptimizeGeneratedConditions.cs) | Shrink a machine-generated condition and prove it still means the same. | `BooleanExpressionOptimizer.OptimizeExpression` |
| 5 | [CI verification](LogicalOptimizer.Samples/Recipes/CiVerification.cs) | Gate a build on a set of rule refactors and emit a machine-readable JSON artifact. | `EquivalenceChecker.Check` |

📚 Full documentation: <https://AlexanderV.github.io/LogicalOptimizer/>
