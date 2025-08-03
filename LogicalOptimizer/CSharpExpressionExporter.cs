using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicalOptimizer;

/// <summary>
/// Exports boolean expressions as compilable C# code
/// </summary>
public class CSharpExpressionExporter
{
    /// <summary>
    /// Convert AST to C# boolean expression
    /// </summary>
    public static string ToExpression(AstNode node)
    {
        return node switch
        {
            VariableNode varNode => varNode.Name switch
            {
                "1" => "true",
                "0" => "false",
                _ => varNode.Name
            },
            NotNode notNode => $"!({ToExpression(notNode.Operand)})",
            AndNode andNode => $"({ToExpression(andNode.Left)} && {ToExpression(andNode.Right)})",
            OrNode orNode => $"({ToExpression(orNode.Left)} || {ToExpression(orNode.Right)})",
            XorNode xorNode => $"({ToExpression(xorNode.Left)} ^ {ToExpression(xorNode.Right)})",
            ImpNode impNode => $"(!{ToExpression(impNode.Left)} || {ToExpression(impNode.Right)})",
            _ => throw new ArgumentException($"Unknown node type: {node.GetType()}")
        };
    }

    /// <summary>
    /// Generate complete C# method for expression evaluation
    /// </summary>
    public static string GenerateMethod(AstNode node, string methodName = "EvaluateExpression")
    {
        var variables = node.GetVariables().OrderBy(v => v).ToList();
        var expression = ToExpression(node);
        
        var parametersBuilder = new StringBuilder();
        for (int i = 0; i < variables.Count; i++)
        {
            if (i > 0) parametersBuilder.Append(", ");
            parametersBuilder.Append($"bool {variables[i]}");
        }
        
        return $@"public static bool {methodName}({parametersBuilder})
{{
    return {expression};
}}";
    }

    /// <summary>
    /// Generate complete C# class with evaluation method
    /// </summary>
    public static string GenerateClass(AstNode node, string className = "BooleanEvaluator", string methodName = "Evaluate")
    {
        var method = GenerateMethod(node, methodName);
        
        return $@"using System;

public static class {className}
{{
    {method}
}}";
    }

    /// <summary>
    /// Generate C# lambda expression
    /// </summary>
    public static string GenerateLambda(AstNode node)
    {
        var variables = node.GetVariables().OrderBy(v => v).ToList();
        var expression = ToExpression(node);
        
        var parametersBuilder = new StringBuilder();
        for (int i = 0; i < variables.Count; i++)
        {
            if (i > 0) parametersBuilder.Append(", ");
            parametersBuilder.Append(variables[i]);
        }
        
        return $"({parametersBuilder}) => {expression}";
    }
}
