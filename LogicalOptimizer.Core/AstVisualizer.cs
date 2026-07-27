using System.Text;

namespace LogicalOptimizer;

/// <summary>Renders an AST as a human-readable tree (box-drawing characters) for diagnostics.</summary>
public static class AstVisualizer
{
    /// <summary>ASCII-art tree of the node and its descendants, one node per line.</summary>
    public static string VisualizeTree(AstNode node, string prefix = "", bool isLast = true)
    {
        var sb = new StringBuilder();

        // Add current node
        sb.AppendLine($"{prefix}{(isLast ? "└─ " : "├─ ")}{GetNodeDescription(node)}");

        // Add children
        var children = GetChildren(node);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var isLastChild = i == children.Count - 1;
            var newPrefix = prefix + (isLast ? "   " : "│  ");
            sb.Append(VisualizeTree(child, newPrefix, isLastChild));
        }

        return sb.ToString();
    }

    /// <summary>The expression string followed by its <see cref="VisualizeTree" /> rendering.</summary>
    public static string GetCompactVisualization(AstNode node)
    {
        return $"AST: {node}\nTree:\n{VisualizeTree(node)}";
    }

    private static string GetNodeDescription(AstNode node)
    {
        return node switch
        {
            ConstantNode constant => $"Constant: {(constant.Value ? "1" : "0")}",
            VariableNode var => $"Variable: '{var.Name}'",
            NotNode => "NOT (!)",
            AndNode => "AND (&)",
            OrNode => "OR (|)",
            _ => node.GetType().Name
        };
    }

    private static List<AstNode> GetChildren(AstNode node)
    {
        return node switch
        {
            NaryNode nary => nary.Operands.ToList(),
            BinaryNode binary => new List<AstNode> { binary.Left, binary.Right },
            NotNode not => new List<AstNode> { not.Operand },
            _ => new List<AstNode>()
        };
    }
}
