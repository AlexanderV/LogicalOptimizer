using System.Collections.Concurrent;

namespace LogicalOptimizer;

/// <summary>
///     LogicNG-style formula construction: n-ary And/Or with automatic flattening,
///     duplicate-operand removal, constant folding, complement folding (x and !x
///     collapse the connective), canonical operand ordering and structural interning —
///     equal formulas built through the factory are the SAME instance, so reference
///     equality works as structural equality and repeated subformulas share memory.
///     Because operands are sorted with a stable canonical key at construction time,
///     structural (order-sensitive) equality is effectively commutative for
///     factory-built trees. The intern table is a <see cref="ConcurrentDictionary{TKey,TValue}" />,
///     so a factory instance may be shared across threads.
/// </summary>
public sealed class FormulaFactory
{
    private readonly ConcurrentDictionary<AstNode, AstNode> _interned = new();

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
    ///     folded (0 annihilates, 1 disappears), complementary operands collapse to 0,
    ///     and the surviving operands are sorted into canonical order.
    /// </summary>
    public AstNode And(params AstNode[] operands)
    {
        return Nary(operands, isAnd: true);
    }

    /// <inheritdoc cref="And(AstNode[])" />
    public AstNode And(IEnumerable<AstNode> operands)
    {
        return Nary(operands.ToArray(), isAnd: true);
    }

    /// <summary>
    ///     N-ary disjunction: nested ORs are flattened, duplicates removed, constants
    ///     folded (1 annihilates, 0 disappears), complementary operands collapse to 1,
    ///     and the surviving operands are sorted into canonical order.
    /// </summary>
    public AstNode Or(params AstNode[] operands)
    {
        return Nary(operands, isAnd: false);
    }

    /// <inheritdoc cref="Or(AstNode[])" />
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

    /// <summary>
    ///     Parse text through the standard grammar, building canonical nodes via this factory.
    ///     Throws <see cref="FormulaParseException" /> (a subclass of <see cref="ArgumentException" />)
    ///     on invalid input; use <see cref="TryParse" /> to avoid the exception.
    /// </summary>
    public AstNode Parse(string expression)
    {
        return new Parser(new Lexer(expression).Tokenize(), this, expression).Parse();
    }

    /// <summary>
    ///     Attempts to parse <paramref name="expression" />. On success returns <c>true</c> with the
    ///     canonical tree in <paramref name="formula" />; on invalid input returns <c>false</c> with a
    ///     structured <paramref name="diagnostic" /> (position, length, expected tokens, machine-readable
    ///     code and a caret snippet) instead of throwing. A <c>null</c> expression is still a contract
    ///     violation and throws <see cref="System.ArgumentNullException" />.
    /// </summary>
    public bool TryParse(string expression, out AstNode? formula, out ParseDiagnostic? diagnostic)
    {
        try
        {
            formula = new Parser(new Lexer(expression).Tokenize(), this, expression).Parse();
            diagnostic = null;
            return true;
        }
        catch (FormulaParseException ex)
        {
            formula = null;
            diagnostic = ex.Diagnostic;
            return false;
        }
    }

    /// <summary>
    ///     Rebuild an existing tree through the factory: the result carries every
    ///     construction-time canonicalization and is interned. Derived operators
    ///     (Xor/Eqv/Imp/Nand/Nor) are decomposed into And/Or/Not.
    /// </summary>
    public AstNode Import(AstNode node)
    {
        return node switch
        {
            ConstantNode constant => constant.Value ? True : False,
            VariableNode variable => Variable(variable.Name),
            NotNode not => Not(Import(not.Operand)),
            AndNode and => And(and.Operands.Select(Import)),
            OrNode or => Or(or.Operands.Select(Import)),
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
        var stack = new Stack<AstNode>();
        for (var i = operands.Length - 1; i >= 0; i--) stack.Push(operands[i]);
        while (stack.Count > 0)
        {
            var operand = stack.Pop();
            switch (operand)
            {
                case AndNode nestedAnd when isAnd:
                    for (var i = nestedAnd.Operands.Count - 1; i >= 0; i--) stack.Push(nestedAnd.Operands[i]);
                    continue;
                case OrNode nestedOr when !isAnd:
                    for (var i = nestedOr.Operands.Count - 1; i >= 0; i--) stack.Push(nestedOr.Operands[i]);
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

        var sorted = SortCanonically(flattened);
        return Intern(isAnd ? new AndNode(sorted) : new OrNode(sorted));
    }

    /// <summary>
    ///     Canonical operand order: simple operands first (complexity score), ties broken
    ///     by a stable structural key compared ordinally. The sort itself is stable, so
    ///     the result is fully deterministic.
    /// </summary>
    private static IReadOnlyList<AstNode> SortCanonically(List<AstNode> operands)
    {
        return operands
            .OrderBy(GetComplexityScore)
            .ThenBy(GetComparisonKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static int GetComplexityScore(AstNode node)
    {
        return node switch
        {
            // Simple variables have the lowest score, negations are slightly more complex
            VariableNode => 1,
            NotNode => 2,
            // Each implicit binary connective contributes 3, matching the binary-tree score
            NaryNode nary => 3 * (nary.Operands.Count - 1) + nary.Operands.Sum(GetComplexityScore),
            _ => 100 // Unknown nodes go to the end
        };
    }

    private static string GetComparisonKey(AstNode node)
    {
        return node switch
        {
            VariableNode variable => "1_" + variable.Name,
            NotNode not => "2_" + GetComparisonKey(not.Operand),
            AndNode and => CreateNaryKey("3", and.Operands),
            OrNode or => CreateNaryKey("4", or.Operands),
            _ => "9_" + node.GetType().Name
        };
    }

    private static string CreateNaryKey(string prefix, IReadOnlyList<AstNode> operands)
    {
        // Operand keys are sorted so the key is insensitive to operand order,
        // exactly like the historical binary key
        var keys = new string[operands.Count];
        for (var i = 0; i < operands.Count; i++) keys[i] = GetComparisonKey(operands[i]);
        Array.Sort(keys, StringComparer.Ordinal);
        return prefix + "_" + string.Join("_", keys);
    }

    private AstNode Intern(AstNode node)
    {
        return _interned.GetOrAdd(node, node);
    }
}
