using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Architecture testing: structural rules the codebase must keep as it grows.
///     Layering (library never depends on CLI concerns), encapsulation (the rewrite
///     pipeline stays internal), and AST design invariants are enforced as tests.
/// </summary>
public class ArchitectureTests
{
    /// <summary>The seven library assemblies (facade + Core/Sat/Bdd/Dnnf/Minimization/Formats).</summary>
    private static readonly System.Reflection.Assembly[] LibraryAssemblies =
    {
        typeof(AstNode).Assembly,
        typeof(SatSolver).Assembly,
        typeof(BinaryDecisionDiagram).Assembly,
        typeof(DnnfCircuit).Assembly,
        typeof(TruthTableMinimizer).Assembly,
        typeof(BooleanExpressionOptimizer).Assembly,
        typeof(DimacsParser).Assembly
    };

    private static readonly Architecture Arch = new ArchLoader()
        .LoadAssemblies(LibraryAssemblies.DistinctBy(a => a.FullName).ToArray())
        .Build();

    [Fact]
    public void Library_DoesNotWriteToConsole()
    {
        // The library is embeddable: all user interaction belongs to the CLI project
        foreach (var assembly in LibraryAssemblies.DistinctBy(a => a.FullName))
            Types().That().ResideInAssembly(assembly.FullName!)
                .Should().NotDependOnAny(Types().That().HaveFullName("System.Console"))
                .Because("the class library must stay silent; only the CLI may talk to the console")
                .Check(Arch);
    }

    [Fact]
    public void RewritePipeline_StaysInternal()
    {
        // The rewrite engine and its rules are an implementation detail behind the facade
        Types().That().ResideInNamespace("LogicalOptimizer.Rewrite")
            .Should().NotBePublic()
            .Because("rewrite rules are internal machinery; the public surface is the facade")
            .Check(Arch);
    }

    [Fact]
    public void RewriteRules_LiveInTheRewriteNamespace()
    {
        Classes().That().ImplementInterface(typeof(Rewrite.IRewriteRule))
            .Should().ResideInNamespace("LogicalOptimizer.Rewrite")
            .Check(Arch);
    }

    [Fact]
    public void AstNodes_AreSealedOrAbstract()
    {
        // The AST is a closed algebra: every optimizer switch-matches over node types,
        // so an unsealed leaf class invites silently unhandled subclasses
        var offenders = typeof(AstNode).Assembly.GetTypes()
            .Where(t => typeof(AstNode).IsAssignableFrom(t) && t is { IsSealed: false, IsAbstract: false })
            .Select(t => t.FullName)
            .ToList();
        Assert.True(offenders.Count == 0,
            $"AST node classes must be sealed or abstract: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Library_DoesNotDependOnTestFrameworksOrCli()
    {
        var forbidden = new[] { "xunit", "LogicalOptimizer.Cli", "BenchmarkDotNet", "Moq", "CsCheck" };
        var references = LibraryAssemblies.DistinctBy(a => a.FullName)
            .SelectMany(a => a.GetReferencedAssemblies())
            .Select(a => a.Name!)
            .Where(name => forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();
        Assert.True(references.Count == 0,
            $"The library must not reference {string.Join(", ", references)}");
    }

    [Fact]
    public void PackageLayering_IsAcyclicAndPointsDownward()
    {
        // Core depends on nothing; Sat/Bdd on Core; Minimization on Core+Sat;
        // the facade on everything. Any other edge is an architecture break.
        var allowed = new Dictionary<string, string[]>
        {
            ["LogicalOptimizer.Core"] = Array.Empty<string>(),
            ["LogicalOptimizer.Sat"] = new[] { "LogicalOptimizer.Core" },
            ["LogicalOptimizer.Bdd"] = new[] { "LogicalOptimizer.Core" },
            ["LogicalOptimizer.Dnnf"] = new[] { "LogicalOptimizer.Core", "LogicalOptimizer.Sat" },
            ["LogicalOptimizer.Formats"] = new[] { "LogicalOptimizer.Core", "LogicalOptimizer.Sat" },
            ["LogicalOptimizer.Minimization"] = new[] { "LogicalOptimizer.Core", "LogicalOptimizer.Sat" },
            ["LogicalOptimizer"] = new[]
            {
                "LogicalOptimizer.Core", "LogicalOptimizer.Sat", "LogicalOptimizer.Bdd",
                "LogicalOptimizer.Minimization"
            }
        };

        foreach (var assembly in LibraryAssemblies.DistinctBy(a => a.FullName))
        {
            var name = assembly.GetName().Name!;
            var libraryReferences = assembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("LogicalOptimizer", StringComparison.Ordinal))
                .ToList();
            var illegal = libraryReferences.Except(allowed[name]).ToList();
            Assert.True(illegal.Count == 0,
                $"{name} must not reference {string.Join(", ", illegal)}");
        }
    }

    [Fact]
    public void PublicSurface_IsTheDocumentedSet()
    {
        // The NuGet API surface is a contract: any type added here must be a conscious,
        // reviewed decision — extend the list together with the README/API docs
        var expected = new HashSet<string>
        {
            // Core (24)
            "AstNode", "NaryNode", "AndNode", "OrNode", "NotNode", "VariableNode", "ConstantNode",
            "BinaryNode", "ImpNode", "XorNode", "NandNode", "NorNode", "EqvNode",
            "FormulaFactory", "AstFormatter", "AstVisualizer", "TruthTable",
            "OptimizationMetrics", "AstMetrics", "ResourceBudget", "ComputationBudgetExceededException",
            "NodeBudgetExceededException", "NormalFormTooLargeException", "CircuitSerializationException",
            // Sat (14)
            "SatSolver", "SatResult", "MaxSatSolver", "MaxSatResult", "MaxSatStatus", "MaxSatAlgorithm",
            "CnfBuilder", "CardinalityEncoder", "PseudoBooleanEncoder", "CnfEncodingStyle", "TseitinCnf",
            "CardinalityEncoding", "PseudoBooleanEncoding", "EncodingStats",
            // Bdd (1)
            "BinaryDecisionDiagram",
            // Dnnf (2)
            "DnnfCircuit", "KnowledgeCompilation",
            // Formats (9)
            "DimacsParser", "WcnfParser", "OpbParser",
            "CnfProblem", "WeightedCnfProblem", "PseudoBooleanProblem",
            "PseudoBooleanConstraint", "PseudoBooleanComparison", "FormatParseException",
            // Minimization (5)
            "TruthTableMinimizer", "CsvTruthTableParser", "PartialTruthTable",
            "MultiOutputTable", "MultiOutputFunction",
            // Facade (19)
            "BooleanExpressionOptimizer", "OptimizationResult", "OptimizationOptions",
            "CnfMode", "MinimizationStatus", "ComputationStatus",
            "EquivalenceChecker", "EquivalenceCheckResult", "IEquivalenceChecker",
            "HybridEquivalenceChecker", "BddEquivalenceChecker",
            "FormulaAnalysis", "BackboneResult", "Transformations",
            "BooleanExpressionExporter", "CSharpExpressionExporter", "OptimizationQualityAnalyzer",
            "ProjectedModelCountResult", "ProjectedCountStatus"
        };

        var actual = LibraryAssemblies.DistinctBy(a => a.FullName)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsPublic && !t.IsNested)
            .Select(t => t.Name)
            .ToHashSet();

        var unexpected = actual.Except(expected).OrderBy(n => n).ToList();
        var missing = expected.Except(actual).OrderBy(n => n).ToList();
        Assert.True(unexpected.Count == 0,
            $"NEW public types leaked into the API surface: {string.Join(", ", unexpected)}");
        Assert.True(missing.Count == 0,
            $"Documented public types disappeared from the API: {string.Join(", ", missing)}");
    }

    [Fact]
    public void PublicStaticEngineClasses_TakeCancellationOnExpensiveEntryPoints()
    {
        // Scale contract: every public method that can run long must be cancellable
        var expensive = new[]
        {
            (typeof(TruthTableMinimizer), "MinimalSop"),
            (typeof(TruthTableMinimizer), "MinimalPos"),
            (typeof(FormulaAnalysis), "ComputeBackbone"),
            (typeof(FormulaAnalysis), "EnumerateModels"),
            (typeof(FormulaAnalysis), "CountProjectedModels"),
            (typeof(MaxSatSolver), "Solve"),
            (typeof(Transformations), "MinimizeDnfHeuristic"),
            (typeof(Transformations), "ToAlgebraicNormalForm"),
            (typeof(BinaryDecisionDiagram), "BuildWithBestOrder"),
            (typeof(BinaryDecisionDiagram), "BuildWithSiftedOrder"),
            (typeof(KnowledgeCompilation), "CompileToDnnf")
        };

        foreach (var (type, methodName) in expensive)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .ToList();
            Assert.True(methods.Count > 0, $"{type.Name}.{methodName} not found");
            Assert.True(methods.Any(m => m.GetParameters()
                    .Any(p => p.ParameterType == typeof(CancellationToken))),
                $"{type.Name}.{methodName} has no CancellationToken overload");
        }
    }

    [Fact]
    public void ImmutableAstContract_NoPublicSettersOnNodes()
    {
        // AST nodes are shared between rewrites and interned by the factory; a mutable
        // node would corrupt sibling references. Since v2 there are NO exceptions:
        // ForceParentheses is gone and trees are fully immutable. (Reflection instead
        // of ArchUnitNET: init-only setters are misclassified by NotHavePublicSetter
        // in v0.13.)
        var offenders = new List<string>();
        foreach (var type in typeof(AstNode).Assembly.GetTypes()
                     .Where(t => typeof(AstNode).IsAssignableFrom(t)))
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var setter = property.SetMethod;
                if (setter is not { IsPublic: true }) continue;
                var isInitOnly = setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(m => m.Name == "IsExternalInit");
                if (!isInitOnly) offenders.Add($"{type.Name}.{property.Name}");
            }

        Assert.True(offenders.Count == 0,
            $"AST nodes must be immutable; writable setters found: {string.Join(", ", offenders)}");
    }
}
