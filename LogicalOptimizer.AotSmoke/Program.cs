// Native AOT smoke harness (roadmap P0.1).
//
// This app is published with PublishAot=true in CI (win-x64 + linux-x64). It drives
// every engine in the toolkit through its PUBLIC API and asserts a known result for
// each, so a passing run proves the whole pipeline works after native compilation and
// trimming - not just that it links. Any assertion mismatch prints FAIL and the process
// exits non-zero, failing the CI job.
//
// The reference formula is F = "a & b | a & c" (documented canonical output: "a & (b | c)").
// Over variables {a, b, c} it has exactly 3 satisfying assignments (a=1 and b|c), which
// lets the BDD, d-DNNF and minimization checks cross-validate the same number.

using System.Numerics;
using LogicalOptimizer;

const string formula = "a & b | a & c";
const string expectedOptimized = "a & (b | c)";
const int expectedModelCount = 3;

var failures = 0;

static void Check(string engine, bool ok, string detail)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {engine}: {detail}");
}

void Assert(string engine, bool condition, string detail)
{
    if (!condition) failures++;
    Check(engine, condition, detail);
}

// 1. Parser (FormulaFactory) - parse the reference formula and confirm its variables.
var factory = new FormulaFactory();
var ast = factory.Parse(formula);
var variables = ast.GetVariables().OrderBy(v => v).ToList();
Assert("Parser", variables.SequenceEqual(new[] { "a", "b", "c" }),
    $"parsed '{formula}' -> variables [{string.Join(", ", variables)}] (expected a, b, c)");

// 2. Optimizer (BooleanExpressionOptimizer) - a known, documented reduction.
var optimizer = new BooleanExpressionOptimizer();
var optimization = optimizer.OptimizeExpression(formula);
Assert("Optimizer", optimization.Optimized == expectedOptimized && optimization.IsEquivalent(),
    $"optimized '{formula}' -> '{optimization.Optimized}' (expected '{expectedOptimized}', equivalent={optimization.IsEquivalent()})");

// 3. SAT (SatSolver) - a satisfiable CNF from the formula, plus an UNSAT sanity case.
var cnf = optimizer.ToEquisatisfiableCnf(formula);
var satResult = SatSolver.FromCnf(cnf).Solve();

var unsat = new SatSolver(1);
unsat.AddClause(1);   // a
unsat.AddClause(-1);  // !a  -> contradiction
var unsatResult = unsat.Solve();

Assert("SAT", satResult == SatResult.Satisfiable && unsatResult == SatResult.Unsatisfiable,
    $"SAT('{formula}')={satResult} (expected Satisfiable), SAT(a & !a)={unsatResult} (expected Unsatisfiable)");

// 4. BDD (BinaryDecisionDiagram) - build and exact model count.
var bdd = BinaryDecisionDiagram.Build(ast);
BigInteger bddCount = bdd.CountSatisfyingAssignments();
Assert("BDD", bddCount == expectedModelCount,
    $"model count = {bddCount} (expected {expectedModelCount}), nodes = {bdd.NodeCount}");

// 5. d-DNNF (KnowledgeCompilation) - compile and count; must match the BDD oracle.
var circuit = KnowledgeCompilation.CompileToDnnf(ast);
BigInteger dnnfCount = circuit.CountModels();
Assert("d-DNNF", dnnfCount == expectedModelCount && dnnfCount == bddCount,
    $"model count = {dnnfCount} (expected {expectedModelCount}, BDD said {bddCount})");

// 6. Minimization (TruthTableMinimizer) - exact SOP must be provably minimal and
//    equivalent to F (checked by counting the models of the minimized circuit).
var onSet = new HashSet<int>();
var assignment = new Dictionary<string, bool>();
for (var minterm = 0; minterm < 1 << variables.Count; minterm++)
{
    for (var j = 0; j < variables.Count; j++)
        assignment[variables[j]] = (minterm & (1 << j)) != 0;
    if (TruthTable.Evaluate(ast, assignment)) onSet.Add(minterm);
}

var (minimalSop, provenMinimal) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet);
var minimizedCount = BinaryDecisionDiagram.Build(minimalSop).CountSatisfyingAssignments();
Assert("Minimization", provenMinimal && minimizedCount == expectedModelCount,
    $"MinimalSop = '{minimalSop}' (provenMinimal={provenMinimal}, models={minimizedCount}, expected {expectedModelCount})");

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("ALL ENGINES PASS");
    return 0;
}

Console.WriteLine($"{failures} ENGINE CHECK(S) FAILED");
return 1;
