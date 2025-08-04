using System;
using System.Diagnostics;

namespace LogicalOptimizer.Tests;

/// <summary>
/// Simple performance profiling test to measure optimization bottlenecks
/// </summary>
public class PerformanceProfileTest
{
    public void ProfileComplexExpression()
    {
        var expression = "((a & b & c) | (a & b & !c)) & ((d & e) | (!d & !e))";
        Console.WriteLine($"Profiling: {expression}");
        
        // Parse
        var sw = Stopwatch.StartNew();
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        Console.WriteLine($"Parse time: {sw.ElapsedMilliseconds} ms");
        
        var initialNodes = AstMetrics.CountNodes(ast);
        Console.WriteLine($"Initial nodes: {initialNodes}");
        
        // Profile each optimizer
        ProfileSingleOptimizer("DeMorgan", new Optimizers.DeMorganOptimizer(), ast);
        ProfileSingleOptimizer("Constants", new Optimizers.ConstantsOptimizer(), ast);
        ProfileSingleOptimizer("Absorption", new Optimizers.AbsorptionOptimizer(), ast);
        ProfileSingleOptimizer("Complement", new Optimizers.ComplementOptimizer(), ast);
        ProfileSingleOptimizer("Associativity", new Optimizers.AssociativityOptimizer(), ast);
        ProfileSingleOptimizer("Consensus", new Optimizers.ConsensusOptimizer(), ast);
        ProfileSingleOptimizer("Redundancy", new Optimizers.RedundancyOptimizer(), ast);
        ProfileSingleOptimizer("Commutativity", new Optimizers.CommutativityOptimizer(), ast);
        ProfileSingleOptimizer("Factorization", new Optimizers.FactorizationOptimizer(), ast);
        
        // Full optimization
        sw.Restart();
        var optimizer = new ExpressionOptimizer();
        var metrics = new OptimizationMetrics();
        var optimized = optimizer.Optimize(ast, metrics);
        Console.WriteLine($"Full optimization: {sw.ElapsedMilliseconds} ms, final nodes: {AstMetrics.CountNodes(optimized)}");
        Console.WriteLine($"Iterations: {metrics.Iterations}");
    }
    
    private void ProfileSingleOptimizer(string name, Optimizers.IOptimizer optimizer, AstNode ast)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = optimizer.Optimize(ast, null);
            var nodes = AstMetrics.CountNodes(result);
            Console.WriteLine($"{name}: {sw.ElapsedMilliseconds} ms, nodes: {nodes}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name}: FAILED {sw.ElapsedMilliseconds} ms - {ex.Message}");
        }
    }
}
