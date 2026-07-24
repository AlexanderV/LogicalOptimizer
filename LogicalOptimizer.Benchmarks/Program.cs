using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using LogicalOptimizer;

BenchmarkSwitcher.FromAssembly(typeof(OptimizationBenchmarks).Assembly).Run(args);

/// <summary>
///     Performance-regression benchmarks. Run locally with:
///     dotnet run -c Release --project LogicalOptimizer.Benchmarks -- --filter '*'
///     Machine-readable results (roadmap P0.3) land in
///     BenchmarkDotNet.Artifacts/results/*-report-full.json; the published summary
///     lives in doc/BENCHMARKS.md.
/// </summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class OptimizationBenchmarks
{
    private readonly BooleanExpressionOptimizer _optimizer = new();
    private readonly OptimizationOptions _resultOnly = new()
    { ComputeCnf = false, ComputeDnf = false, ComputeAdvancedForms = false };

    private string _small = "";
    private string _guaranteeZone = "";
    private string _midRange = "";

    [GlobalSetup]
    public void Setup()
    {
        _small = "a & b | a & c | b & c";
        _guaranteeZone = string.Join(" | ",
            Enumerable.Range(1, 5).Select(i => $"v{2 * i - 1:D2} & v{2 * i:D2}"));
        _midRange = string.Join(" | ",
            Enumerable.Range(1, 7).Select(i => $"p{i} & q{i} | p{i} & !q{i}"));
    }

    [Benchmark]
    public OptimizationResult SmallExpression()
    {
        return _optimizer.OptimizeExpression(_small, _resultOnly);
    }

    [Benchmark]
    public OptimizationResult GuaranteeZoneTenVariables()
    {
        return _optimizer.OptimizeExpression(_guaranteeZone, _resultOnly);
    }

    [Benchmark]
    public OptimizationResult MidRangeFourteenVariables()
    {
        return _optimizer.OptimizeExpression(_midRange, _resultOnly);
    }
}

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class SatSolverBenchmarks
{
    private List<int[]> _phaseTransition = new();

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(60606);
        _phaseTransition = new List<int[]>();
        for (var c = 0; c < 252; c++)
        {
            var vars = new HashSet<int>();
            while (vars.Count < 3) vars.Add(random.Next(1, 61));
            _phaseTransition.Add(vars.Select(v => random.Next(2) == 0 ? v : -v).ToArray());
        }
    }

    [Benchmark]
    public SatResult PhaseTransition60Variables()
    {
        var solver = new SatSolver(60);
        foreach (var clause in _phaseTransition) solver.AddClause(clause);
        return solver.Solve();
    }

    [Benchmark]
    public bool? DeMorganEquivalence30Variables()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"v{i:D2}").ToList();
        var left = new Parser(new Lexer("!(" + string.Join(" & ", names) + ")").Tokenize()).Parse();
        var right = new Parser(new Lexer(string.Join(" | ", names.Select(n => $"!{n}"))).Tokenize()).Parse();
        return EquivalenceChecker.Check(left, right).AreEquivalent;
    }
}

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class NewEnginesBenchmarks
{
    private AstNode _fortyVarCover = new VariableNode("a");
    private AstNode _pairedFunction = new VariableNode("a");

    [GlobalSetup]
    public void Setup()
    {
        var terms = new List<string>();
        for (var i = 0; i < 13; i++)
        {
            var a = $"v{2 * i:D2}";
            var b = $"v{2 * i + 1:D2}";
            var c = $"v{(2 * i + 2) % 40:D2}";
            terms.Add($"{a} & {b}");
            terms.Add($"{a} & {b} & {c}");
            terms.Add($"!{a} & {c}");
            terms.Add($"{b} & {c}");
        }

        _fortyVarCover = new Parser(new Lexer(string.Join(" | ", terms)).Tokenize()).Parse();

        var preamble = string.Join(" & ", Enumerable.Range(1, 6).Select(i => $"a{i}"));
        var pairs = string.Join(" | ", Enumerable.Range(1, 6).Select(i => $"a{i} & b{i}"));
        _pairedFunction = new Parser(new Lexer($"{preamble} | {pairs}").Tokenize()).Parse();
    }

    [Benchmark]
    public AstNode EspressoLite_FortyVariableCover()
    {
        return Transformations.MinimizeDnfHeuristic(_fortyVarCover);
    }

    [Benchmark]
    public int BddSifting_TwelveVariablePairs()
    {
        return BinaryDecisionDiagram.BuildWithSiftedOrder(_pairedFunction).NodeCount;
    }

    [Benchmark]
    public AstNode FormulaFactory_ImportFortyVarCover()
    {
        return new FormulaFactory().Import(_fortyVarCover);
    }

    [Benchmark]
    public int Aig_FromFortyVarCover()
    {
        return AndInverterGraph.FromAst(_fortyVarCover).AndNodeCount;
    }
}

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class ExactMinimizationBenchmarks
{
    private List<string> _variables = new();
    private HashSet<int> _onSet = new();

    [GlobalSetup]
    public void Setup()
    {
        _variables = Enumerable.Range(0, 10).Select(i => $"v{i:D2}").ToList();
        var random = new Random(42);
        _onSet = new HashSet<int>();
        for (var m = 0; m < 1 << 10; m++)
            if (random.Next(4) == 0)
                _onSet.Add(m);
    }

    [Benchmark]
    public AstNode QuineMcCluskeyTenVariables()
    {
        return TruthTableMinimizer.MinimalSop(_variables, _onSet);
    }
}
