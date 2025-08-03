namespace LogicalOptimizer;

/// <summary>
///     Tree node for exclusive OR (XOR) operation
/// </summary>
public class XorNode : BinaryNode
{
    public XorNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right)
    {
        ForceParentheses = forceParens;
    }

    public bool ForceParentheses { get; set; }
    public override string Operator => "^";

    public override AstNode Clone()
    {
        return new XorNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }

    public override string ToString()
    {
        var leftStr = Left.ToString();
        var rightStr = Right.ToString();

        var result = $"{leftStr} ^ {rightStr}";

        if (ForceParentheses)
            result = $"({result})";

        return result;
    }
}

/// <summary>
///     Tree node for NAND (NOT-AND) operation
/// </summary>
public class NandNode : BinaryNode
{
    public NandNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right)
    {
        ForceParentheses = forceParens;
    }

    public bool ForceParentheses { get; set; }
    public override string Operator => "~&";

    public override AstNode Clone()
    {
        return new NandNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }

    public override string ToString()
    {
        var leftStr = Left.ToString();
        var rightStr = Right.ToString();

        var result = $"{leftStr} ~& {rightStr}";

        if (ForceParentheses)
            result = $"({result})";

        return result;
    }
}

/// <summary>
///     Tree node for NOR (NOT-OR) operation
/// </summary>
public class NorNode : BinaryNode
{
    public NorNode(AstNode left, AstNode right, bool forceParens = false) : base(left, right)
    {
        ForceParentheses = forceParens;
    }

    public bool ForceParentheses { get; set; }
    public override string Operator => "~|";

    public override AstNode Clone()
    {
        return new NorNode(Left.Clone(), Right.Clone(), ForceParentheses);
    }

    public override string ToString()
    {
        var leftStr = Left.ToString();
        var rightStr = Right.ToString();

        var result = $"{leftStr} ~| {rightStr}";

        if (ForceParentheses)
            result = $"({result})";

        return result;
    }
}

/// <summary>
///     Additional optimization rules for extended operators
/// </summary>
public static class ExtendedOptimizationRules
{
    /// <summary>
    ///     XOR optimization rules
    /// </summary>
    public static class XorRules
    {
        // A ^ A = 0
        public static AstNode? IdempotentLaw(XorNode node)
        {
            if (node.Left.Equals(node.Right))
                return new VariableNode("0");
            return null;
        }

        // A ^ 0 = A
        public static AstNode? NeutralElement(XorNode node)
        {
            if (node.Left is VariableNode {Name: "0"})
                return node.Right;
            if (node.Right is VariableNode {Name: "0"})
                return node.Left;
            return null;
        }

        // A ^ 1 = !A
        public static AstNode? ComplementWithOne(XorNode node)
        {
            if (node.Left is VariableNode {Name: "1"})
                return new NotNode(node.Right);
            if (node.Right is VariableNode {Name: "1"})
                return new NotNode(node.Left);
            return null;
        }

        // A ^ !A = 1
        public static AstNode? ComplementLaw(XorNode node)
        {
            if (node.Left is NotNode notLeft && notLeft.Operand.Equals(node.Right))
                return new VariableNode("1");
            if (node.Right is NotNode notRight && notRight.Operand.Equals(node.Left))
                return new VariableNode("1");
            return null;
        }
    }

    /// <summary>
    ///     NAND optimization rules
    /// </summary>
    public static class NandRules
    {
        // A ~& A = !A
        public static AstNode? IdempotentLaw(NandNode node)
        {
            if (node.Left.Equals(node.Right))
                return new NotNode(node.Left);
            return null;
        }

        // A ~& 0 = 1
        public static AstNode? ZeroAbsorption(NandNode node)
        {
            if (node.Left is VariableNode {Name: "0"} || node.Right is VariableNode {Name: "0"})
                return new VariableNode("1");
            return null;
        }

        // A ~& 1 = !A
        public static AstNode? OneNeutral(NandNode node)
        {
            if (node.Left is VariableNode {Name: "1"})
                return new NotNode(node.Right);
            if (node.Right is VariableNode {Name: "1"})
                return new NotNode(node.Left);
            return null;
        }

        // Convert NAND to basic operators: A ~& B = !(A & B)
        public static AstNode? ToBasicOperators(NandNode node)
        {
            return new NotNode(new AndNode(node.Left, node.Right));
        }
    }

    /// <summary>
    ///     NOR optimization rules
    /// </summary>
    public static class NorRules
    {
        // A ~| A = !A
        public static AstNode? IdempotentLaw(NorNode node)
        {
            if (node.Left.Equals(node.Right))
                return new NotNode(node.Left);
            return null;
        }

        // A ~| 1 = 0
        public static AstNode? OneAbsorption(NorNode node)
        {
            if (node.Left is VariableNode {Name: "1"} || node.Right is VariableNode {Name: "1"})
                return new VariableNode("0");
            return null;
        }

        // A ~| 0 = !A
        public static AstNode? ZeroNeutral(NorNode node)
        {
            if (node.Left is VariableNode {Name: "0"})
                return new NotNode(node.Right);
            if (node.Right is VariableNode {Name: "0"})
                return new NotNode(node.Left);
            return null;
        }

        // Convert NOR to basic operators: A ~| B = !(A | B)
        public static AstNode? ToBasicOperators(NorNode node)
        {
            return new NotNode(new OrNode(node.Left, node.Right));
        }
    }

    /// <summary>
    ///     Functional completeness - any boolean function can be expressed through NAND or NOR
    /// </summary>
    public static class FunctionalCompleteness
    {
        // Expression of basic operators through NAND
        public static class ThroughNand
        {
            // !A = A ~& A
            public static NandNode Not(AstNode a)
            {
                return new NandNode(a, a);
            }

            // A & B = !((A ~& B))
            public static NotNode And(AstNode a, AstNode b)
            {
                return new NotNode(new NandNode(a, b));
            }

            // A | B = (A ~& A) ~& (B ~& B)
            public static NandNode Or(AstNode a, AstNode b)
            {
                return new NandNode(Not(a), Not(b));
            }
        }

        // Expression of basic operators through NOR
        public static class ThroughNor
        {
            // !A = A ~| A
            public static NorNode Not(AstNode a)
            {
                return new NorNode(a, a);
            }

            // A | B = !((A ~| B))
            public static NotNode Or(AstNode a, AstNode b)
            {
                return new NotNode(new NorNode(a, b));
            }

            // A & B = (A ~| A) ~| (B ~| B)
            public static NorNode And(AstNode a, AstNode b)
            {
                return new NorNode(Not(a), Not(b));
            }
        }
    }
}