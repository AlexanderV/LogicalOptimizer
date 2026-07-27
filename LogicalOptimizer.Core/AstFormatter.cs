using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Single precedence-based renderer for expression trees. Every node's
///     <c>ToString</c> delegates here, so parenthesization rules live in exactly one
///     place: a child is wrapped in parentheses only when its precedence is lower than
///     its parent requires. N-ary connectives render flat (<c>a &amp; b &amp; c</c>),
///     an OR under an AND is parenthesized, derived binary operators (XOR/NAND/NOR/
///     EQV/IMP) parenthesize any compound child because they have no precedence in the
///     input grammar, and NOT renders as <c>!x</c> for atomic operands and
///     <c>!(...)</c> for compound ones.
/// </summary>
public static class AstFormatter
{
    // Precedence levels: atoms bind tightest, derived operators have no grammar
    // precedence at all (they are display-only) and therefore never win against
    // a compound child.
    private const int AtomPrecedence = 4;
    private const int NotPrecedence = 3;
    private const int AndPrecedence = 2;
    private const int OrPrecedence = 1;
    private const int DerivedPrecedence = 0;

    /// <summary>Render a node to its canonical textual form.</summary>
    public static string Format(AstNode node)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        var sb = new StringBuilder();
        Append(sb, node);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, AstNode node)
    {
        switch (node)
        {
            case ConstantNode constant:
                sb.Append(constant.Value ? '1' : '0');
                break;
            case VariableNode variable:
                sb.Append(variable.Name);
                break;
            case NotNode not:
                sb.Append('!');
                AppendChild(sb, not.Operand, NotPrecedence);
                break;
            case NaryNode nary:
                var precedence = nary is AndNode ? AndPrecedence : OrPrecedence;
                for (var i = 0; i < nary.Operands.Count; i++)
                {
                    if (i > 0) sb.Append(' ').Append(nary.Operator).Append(' ');
                    AppendChild(sb, nary.Operands[i], precedence);
                }

                break;
            case BinaryNode derived:
                // Derived operators always parenthesize compound children
                AppendChild(sb, derived.Left, NotPrecedence);
                sb.Append(' ').Append(derived.Operator).Append(' ');
                AppendChild(sb, derived.Right, NotPrecedence);
                break;
            default:
                // Unknown node types (external AstNode subclasses) render themselves
                sb.Append(node);
                break;
        }
    }

    private static void AppendChild(StringBuilder sb, AstNode child, int minPrecedence)
    {
        if (Precedence(child) < minPrecedence)
        {
            sb.Append('(');
            Append(sb, child);
            sb.Append(')');
        }
        else
        {
            Append(sb, child);
        }
    }

    private static int Precedence(AstNode node)
    {
        return node switch
        {
            AndNode => AndPrecedence,
            OrNode => OrPrecedence,
            BinaryNode => DerivedPrecedence,
            NotNode => NotPrecedence,
            _ => AtomPrecedence
        };
    }
}
