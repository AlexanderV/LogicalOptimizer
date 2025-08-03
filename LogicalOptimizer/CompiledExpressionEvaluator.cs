using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LogicalOptimizer;

/// <summary>
///     Compiles and executes C# boolean expressions dynamically
/// </summary>
public class CompiledExpressionEvaluator
{
    private readonly Func<Dictionary<string, bool>, bool> _compiledFunction;
    private readonly List<string> _variables;

    public CompiledExpressionEvaluator(AstNode node)
    {
        _variables = node.GetVariables().OrderBy(v => v).ToList();
        _compiledFunction = CompileExpression(node);
    }

    /// <summary>
    ///     Evaluate expression with given variable values
    /// </summary>
    public bool Evaluate(Dictionary<string, bool> variableValues)
    {
        return _compiledFunction(variableValues);
    }

    /// <summary>
    ///     Get variables used in expression
    /// </summary>
    public List<string> GetVariables()
    {
        return _variables.ToList();
    }

    /// <summary>
    ///     Compile AST node to executable function
    /// </summary>
    private Func<Dictionary<string, bool>, bool> CompileExpression(AstNode node)
    {
        try
        {
            // Generate C# source code
            var className = "DynamicEvaluator";
            var methodName = "Evaluate";

            // Create parameters for method
            var parameters = string.Join(", ", _variables.Select(v => $"bool {v}"));
            var expression = CSharpExpressionExporter.ToExpression(node);

            var sourceCode = $@"
using System;
using System.Collections.Generic;

public static class {className}
{{
    public static bool {methodName}({parameters})
    {{
        return {expression};
    }}
    
    public static bool EvaluateWithDictionary(Dictionary<string, bool> variables)
    {{
        {string.Join("\n        ", _variables.Select(v => $"var {v} = variables[\"{v}\"];"))}
        return {methodName}({string.Join(", ", _variables)});
    }}
}}";

            // Compile the source code
            var assembly = CompileSourceCode(sourceCode);

            // Get the compiled method
            var type = assembly.GetType(className);
            var method = type.GetMethod("EvaluateWithDictionary");

            // Return delegate
            return variables => (bool) method.Invoke(null, new object[] {variables});
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to compile expression: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Compile C# source code to assembly
    /// </summary>
    private Assembly CompileSourceCode(string sourceCode)
    {
        // Parse the source code
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // Define the compilation
        var assemblyName = $"DynamicAssembly_{Guid.NewGuid():N}";
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] {syntaxTree},
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Compile to memory stream
        using var memoryStream = new MemoryStream();
        var emitResult = compilation.Emit(memoryStream);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Compilation failed:\n{errors}\n\nSource code:\n{sourceCode}");
        }

        // Load the compiled assembly
        memoryStream.Seek(0, SeekOrigin.Begin);
        return AssemblyLoadContext.Default.LoadFromStream(memoryStream);
    }
}