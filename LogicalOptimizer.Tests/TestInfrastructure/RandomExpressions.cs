namespace LogicalOptimizer.Tests;

/// <summary>
///     Seeded random generator of syntactically valid boolean expressions over the
///     grammar accepted by <see cref="Lexer" /> (identifiers, 0/1, !, &amp;, |, parens).
///     Shared by the metamorphic, differential, and fuzzing suites; every caller passes
///     its own <see cref="Random" /> so failures reproduce from the reported seed.
/// </summary>
internal static class RandomExpressions
{
    /// <summary>Random expression AST rendered as text; depth-bounded, may contain constants.</summary>
    public static string Generate(Random rng, int variableCount, int maxDepth, bool allowConstants = false)
    {
        return Node(rng, variableCount, maxDepth, allowConstants);
    }

    private static string Node(Random rng, int variableCount, int depth, bool allowConstants)
    {
        if (depth <= 0 || rng.Next(4) == 0)
        {
            if (allowConstants && rng.Next(10) == 0) return rng.Next(2) == 0 ? "0" : "1";
            var name = $"v{rng.Next(variableCount)}";
            return rng.Next(3) == 0 ? "!" + name : name;
        }

        var left = Node(rng, variableCount, depth - 1, allowConstants);
        var right = Node(rng, variableCount, depth - 1, allowConstants);
        var op = rng.Next(2) == 0 ? "&" : "|";
        var body = $"{left} {op} {right}";
        if (rng.Next(3) == 0) body = $"!({body})";
        return rng.Next(2) == 0 ? $"({body})" : body;
    }

    public static AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize()).Parse();
    }

    /// <summary>Rename every variable via <paramref name="map" />; unmapped names are kept.</summary>
    public static AstNode Rename(AstNode node, IReadOnlyDictionary<string, string> map)
    {
        return node switch
        {
            VariableNode v when map.TryGetValue(v.Name, out var renamed) => new VariableNode(renamed),
            VariableNode v => v,
            ConstantNode c => c,
            NotNode n => new NotNode(Rename(n.Operand, map)),
            AndNode a => new AndNode(a.Operands.Select(o => Rename(o, map)).ToList()),
            OrNode o => new OrNode(o.Operands.Select(op => Rename(op, map)).ToList()),
            _ => throw new InvalidOperationException($"Unexpected node type {node.GetType().Name}")
        };
    }

    /// <summary>Substitute a variable with a constant.</summary>
    public static AstNode Substitute(AstNode node, string variable, bool value)
    {
        return node switch
        {
            VariableNode v when v.Name == variable => new ConstantNode(value),
            VariableNode v => v,
            ConstantNode c => c,
            NotNode n => new NotNode(Substitute(n.Operand, variable, value)),
            AndNode a => new AndNode(a.Operands.Select(o => Substitute(o, variable, value)).ToList()),
            OrNode o => new OrNode(o.Operands.Select(op => Substitute(op, variable, value)).ToList()),
            _ => throw new InvalidOperationException($"Unexpected node type {node.GetType().Name}")
        };
    }

    /// <summary>Number of ON-minterms by brute-force truth-table evaluation.</summary>
    public static int CountModels(AstNode node, IReadOnlyList<string> variables)
    {
        var count = 0;
        var assignment = new Dictionary<string, bool>();
        for (var m = 0; m < 1 << variables.Count; m++)
        {
            for (var j = 0; j < variables.Count; j++)
                assignment[variables[j]] = (m & (1 << j)) != 0;
            if (TruthTable.Evaluate(node, assignment)) count++;
        }

        return count;
    }
}
