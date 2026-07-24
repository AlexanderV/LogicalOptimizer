namespace LogicalOptimizer;

/// <summary>
///     LogicNG-style formula construction: n-ary And/Or with automatic flattening,
///     duplicate-operand removal, constant folding, complement folding (x and !x
///     collapse the connective) and structural interning — equal formulas built through
///     the factory are the SAME instance, so reference equality works as structural
///     equality and repeated subformulas share memory. This is the v2.0 foundation:
///     construction-time canonicalization, today on top of the binary AST (n-ary input
///     is right-folded into the existing node types after normalization).
/// </summary>
public sealed class FormulaFactory
{
    private readonly Dictionary<AstNode, AstNode> _interned = new();

    /// <summary>Constant true (interned).</summary>
    public AstNode True => ConstantNode.True;

    /// <summary>Constant false (interned).</summary>
    public AstNode False => ConstantNode.False;

    /// <summary>Interned variable.</summary>
    public AstNode Variable(string name)
    {
        return Intern(new VariableNode(name));
    }

    /// <summary>Negation with double-negation and constant folding.</summary>
    public AstNode Not(AstNode operand)
    {
        return operand switch
        {
            ConstantNode constant => constant.Value ? False : True,
            NotNode not => not.Operand,
            _ => Intern(new NotNode(operand))
        };
    }

    /// <summary>
    ///     N-ary conjunction: nested ANDs are flattened, duplicates removed, constants
    ///     folded (0 annihilates, 1 disappears), complementary operands collapse to 0.
    /// </summary>
    public AstNode And(params AstNode[] operands)
    {
        return Nary(operands, isAnd: true);
    }

    public AstNode And(IEnumerable<AstNode> operands)
    {
        return Nary(operands.ToArray(), isAnd: true);
    }

    /// <summary>
    ///     N-ary disjunction: nested ORs are flattened, duplicates removed, constants
    ///     folded (1 annihilates, 0 disappears), complementary operands collapse to 1.
    /// </summary>
    public AstNode Or(params AstNode[] operands)
    {
        return Nary(operands, isAnd: false);
    }

    public AstNode Or(IEnumerable<AstNode> operands)
    {
        return Nary(operands.ToArray(), isAnd: false);
    }

    /// <summary>Implication a → b constructed as !a | b.</summary>
    public AstNode Implication(AstNode left, AstNode right)
    {
        return Or(Not(left), right);
    }

    /// <summary>Equivalence a ↔ b constructed as (a &amp; b) | (!a &amp; !b).</summary>
    public AstNode Equivalence(AstNode left, AstNode right)
    {
        return Or(And(left, right), And(Not(left), Not(right)));
    }

    /// <summary>Exclusive or constructed as (a &amp; !b) | (!a &amp; b).</summary>
    public AstNode Xor(AstNode left, AstNode right)
    {
        return Or(And(left, Not(right)), And(Not(left), right));
    }

    /// <summary>Parse text through the standard grammar and rebuild via the factory.</summary>
    public AstNode Parse(string expression)
    {
        return Import(new Parser(new Lexer(expression).Tokenize()).Parse());
    }

    /// <summary>
    ///     Rebuild an existing tree through the factory: the result carries every
    ///     construction-time canonicalization and is interned.
    /// </summary>
    public AstNode Import(AstNode node)
    {
        return node switch
        {
            ConstantNode constant => constant.Value ? True : False,
            VariableNode variable => Variable(variable.Name),
            NotNode not => Not(Import(not.Operand)),
            AndNode and => And(Import(and.Left), Import(and.Right)),
            OrNode or => Or(Import(or.Left), Import(or.Right)),
            XorNode xor => Xor(Import(xor.Left), Import(xor.Right)),
            EqvNode eqv => Equivalence(Import(eqv.Left), Import(eqv.Right)),
            ImpNode imp => Implication(Import(imp.Left), Import(imp.Right)),
            NandNode nand => Not(And(Import(nand.Left), Import(nand.Right))),
            NorNode nor => Not(Or(Import(nor.Left), Import(nor.Right))),
            _ => throw new NotSupportedException($"Unsupported node type: {node.GetType()}")
        };
    }

    private AstNode Nary(AstNode[] operands, bool isAnd)
    {
        var annihilator = isAnd ? False : True;
        var identity = isAnd ? True : False;

        // Flatten nested same-connective nodes, drop identities, stop at annihilators
        var flattened = new List<AstNode>();
        var seen = new HashSet<AstNode>();
        var stack = new Stack<AstNode>(operands.Reverse());
        while (stack.Count > 0)
        {
            var operand = stack.Pop();
            switch (operand)
            {
                case AndNode nestedAnd when isAnd:
                    stack.Push(nestedAnd.Right);
                    stack.Push(nestedAnd.Left);
                    continue;
                case OrNode nestedOr when !isAnd:
                    stack.Push(nestedOr.Right);
                    stack.Push(nestedOr.Left);
                    continue;
            }

            if (operand.Equals(annihilator)) return annihilator;
            if (operand.Equals(identity)) continue;
            if (seen.Add(operand)) flattened.Add(operand);
        }

        if (flattened.Count == 0) return identity;
        if (flattened.Count == 1) return flattened[0];

        // Complement folding: x together with !x collapses the whole connective
        foreach (var operand in flattened)
            if (operand is NotNode not && seen.Contains(not.Operand))
                return annihilator;

        // Right-fold into the binary representation; interning keeps shared suffixes shared
        var result = flattened[^1];
        for (var i = flattened.Count - 2; i >= 0; i--)
            result = Intern(isAnd
                ? new AndNode(flattened[i], result)
                : new OrNode(flattened[i], result));
        return result;
    }

    private AstNode Intern(AstNode node)
    {
        if (_interned.TryGetValue(node, out var existing)) return existing;
        _interned[node] = node;
        return node;
    }
}
