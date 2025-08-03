using System.Text;

namespace LogicalOptimizer;

public static class AstVisualizer
{
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

    public static string GetCompactVisualization(AstNode node)
    {
        return $"AST: {node}\nTree:\n{VisualizeTree(node)}";
    }

    private static string GetNodeDescription(AstNode node)
    {
        return node switch
        {
            VariableNode var => $"Variable: '{var.Name}'",
            NotNode => "NOT (!)",
            AndNode and => $"AND (&) {(and.ForceParentheses ? "[ForceParens]" : "")}",
            OrNode or => $"OR (|) {(or.ForceParentheses ? "[ForceParens]" : "")}",
            _ => node.GetType().Name
        };
    }

    private static List<AstNode> GetChildren(AstNode node)
    {
        return node switch
        {
            BinaryNode binary => new List<AstNode> {binary.Left, binary.Right},
            NotNode not => new List<AstNode> {not.Operand},
            _ => new List<AstNode>()
        };
    }
}