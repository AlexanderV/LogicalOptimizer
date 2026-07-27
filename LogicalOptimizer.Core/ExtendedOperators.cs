namespace LogicalOptimizer;

/// <summary>
///     Tree node for exclusive OR (XOR) operation
/// </summary>
public sealed class XorNode : BinaryNode
{
    /// <summary>Creates an exclusive-or of the two operands.</summary>
    public XorNode(AstNode left, AstNode right) : base(left, right)
    {
    }

    /// <inheritdoc />
    public override string Operator => "XOR";
}

/// <summary>
///     Tree node for NAND (NOT-AND) operation
/// </summary>
public sealed class NandNode : BinaryNode
{
    /// <summary>Creates a NAND of the two operands.</summary>
    public NandNode(AstNode left, AstNode right) : base(left, right)
    {
    }

    /// <inheritdoc />
    public override string Operator => "~&";
}

/// <summary>
///     Tree node for NOR (NOT-OR) operation
/// </summary>
public sealed class NorNode : BinaryNode
{
    /// <summary>Creates a NOR of the two operands.</summary>
    public NorNode(AstNode left, AstNode right) : base(left, right)
    {
    }

    /// <inheritdoc />
    public override string Operator => "~|";
}

/// <summary>
///     Tree node for equivalence (biconditional) operation
/// </summary>
public sealed class EqvNode : BinaryNode
{
    /// <summary>Creates an equivalence (biconditional) of the two operands.</summary>
    public EqvNode(AstNode left, AstNode right) : base(left, right)
    {
    }

    /// <inheritdoc />
    public override string Operator => "↔";
}

/// <summary>
///     Additional optimization rules for extended operators
/// </summary>
internal static class ExtendedOptimizationRules
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
                return ConstantNode.False;
            return null;
        }

        // A ^ 0 = A
        public static AstNode? NeutralElement(XorNode node)
        {
            if (node.Left is ConstantNode { Value: false })
                return node.Right;
            if (node.Right is ConstantNode { Value: false })
                return node.Left;
            return null;
        }

        // A ^ 1 = !A
        public static AstNode? ComplementWithOne(XorNode node)
        {
            if (node.Left is ConstantNode { Value: true })
                return new NotNode(node.Right);
            if (node.Right is ConstantNode { Value: true })
                return new NotNode(node.Left);
            return null;
        }

        // A ^ !A = 1
        public static AstNode? ComplementLaw(XorNode node)
        {
            if (node.Left is NotNode notLeft && notLeft.Operand.Equals(node.Right))
                return ConstantNode.True;
            if (node.Right is NotNode notRight && notRight.Operand.Equals(node.Left))
                return ConstantNode.True;
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
            if (node.Left is ConstantNode { Value: false } || node.Right is ConstantNode { Value: false })
                return ConstantNode.True;
            return null;
        }

        // A ~& 1 = !A
        public static AstNode? OneNeutral(NandNode node)
        {
            if (node.Left is ConstantNode { Value: true })
                return new NotNode(node.Right);
            if (node.Right is ConstantNode { Value: true })
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
            if (node.Left is ConstantNode { Value: true } || node.Right is ConstantNode { Value: true })
                return ConstantNode.False;
            return null;
        }

        // A ~| 0 = !A
        public static AstNode? ZeroNeutral(NorNode node)
        {
            if (node.Left is ConstantNode { Value: false })
                return new NotNode(node.Right);
            if (node.Right is ConstantNode { Value: false })
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
    ///     EQV (equivalence/biconditional) optimization rules
    /// </summary>
    public static class EqvRules
    {
        // A ↔ A = 1 (reflexivity)
        public static AstNode? ReflexivityLaw(EqvNode node)
        {
            if (node.Left.Equals(node.Right))
                return ConstantNode.True;
            return null;
        }

        // A ↔ 1 = A
        public static AstNode? IdentityWithOne(EqvNode node)
        {
            if (node.Left is ConstantNode { Value: true })
                return node.Right;
            if (node.Right is ConstantNode { Value: true })
                return node.Left;
            return null;
        }

        // A ↔ 0 = !A
        public static AstNode? IdentityWithZero(EqvNode node)
        {
            if (node.Left is ConstantNode { Value: false })
                return new NotNode(node.Right);
            if (node.Right is ConstantNode { Value: false })
                return new NotNode(node.Left);
            return null;
        }

        // A ↔ !A = 0
        public static AstNode? ComplementLaw(EqvNode node)
        {
            if (node.Left is NotNode notLeft && notLeft.Operand.Equals(node.Right))
                return ConstantNode.False;
            if (node.Right is NotNode notRight && notRight.Operand.Equals(node.Left))
                return ConstantNode.False;
            return null;
        }

        // Convert EQV to basic operators: A ↔ B = (A & B) | (!A & !B)
        public static AstNode? ToBasicOperators(EqvNode node)
        {
            var leftAndRight = new AndNode(node.Left, node.Right);
            var notLeftAndNotRight = new AndNode(new NotNode(node.Left), new NotNode(node.Right));
            return new OrNode(leftAndRight, notLeftAndNotRight);
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
