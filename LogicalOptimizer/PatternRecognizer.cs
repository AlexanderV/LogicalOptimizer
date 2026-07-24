using LogicalOptimizer.Optimizers;

namespace LogicalOptimizer;

/// <summary>
///     Recognizes and replaces advanced logical patterns in AST (XOR, IMP, EQV, etc.)
/// </summary>
internal class PatternRecognizer
{
    /// <summary>
    ///     Replace patterns in an AST with simplified forms
    /// </summary>
    public AstNode ReplacePatterns(AstNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // Process children first (bottom-up)
        var processedRoot = ProcessChildren(root);

        // Try to replace current node
        var result = TryReplaceWithXor(processedRoot);
        if (result != processedRoot) return result;

        result = TryReplaceWithImp(processedRoot);
        if (result != processedRoot) return result;

        var eqvResult = TryReplaceWithEqv(processedRoot);
        if (eqvResult != null) return eqvResult;

        return processedRoot;
    }

    private AstNode ProcessChildren(AstNode node)
    {
        switch (node)
        {
            case BinaryNode binary:
                var newLeft = ReplacePatterns(binary.Left);
                var newRight = ReplacePatterns(binary.Right);

                if (!ReferenceEquals(newLeft, binary.Left) || !ReferenceEquals(newRight, binary.Right))
                    return AstUtilities.Rebuild(binary, newLeft, newRight);

                break;

            case NotNode not:
                var newChild = ReplacePatterns(not.Operand);
                if (newChild != not.Operand) return new NotNode(newChild);
                break;
        }

        return node;
    }

    private AstNode TryReplaceWithXor(AstNode node)
    {
        // Look for XOR pattern: (a & !b) | (!a & b)
        if (node is OrNode or)
        {
            var leftAnd = or.Left as AndNode;
            var rightAnd = or.Right as AndNode;

            if (leftAnd != null && rightAnd != null)
                if (IsXorPattern(leftAnd, rightAnd, out var variable1, out var variable2))
                    return new XorNode(variable1, variable2, true);
        }

        return node;
    }

    private AstNode TryReplaceWithImp(AstNode node)
    {
        // Look for implication pattern: !a | b ≡ a → b or b | !a ≡ a → b
        if (node is OrNode or)
        {
            var leftNot = or.Left as NotNode;
            var rightNot = or.Right as NotNode;

            if (leftNot != null)
                // Pattern: !a | b ≡ a → b
                return new ImpNode(leftNot.Operand, or.Right);

            if (rightNot != null)
                // Pattern: b | !a ≡ a → b
                return new ImpNode(rightNot.Operand, or.Left);
        }

        return node;
    }

    /// <summary>
    ///     Strict implication match: only !var | var forms (both orders).
    ///     Unlike <see cref="TryReplaceWithImp" />, arbitrary subexpressions do not match.
    /// </summary>
    public ImpNode? TryMatchStrictImp(OrNode or)
    {
        if (or.Left is NotNode { Operand: VariableNode premise } && or.Right is VariableNode conclusion)
            return new ImpNode(premise, conclusion);

        if (or.Right is NotNode { Operand: VariableNode premise2 } && or.Left is VariableNode conclusion2)
            return new ImpNode(premise2, conclusion2);

        return null;
    }

    public AstNode? TryReplaceWithEqv(AstNode node)
    {
        // Look for equivalence pattern: (a & b) | (!a & !b) ≡ a ↔ b
        if (node is OrNode or)
        {
            var leftAnd = or.Left as AndNode;
            var rightAnd = or.Right as AndNode;

            if (leftAnd != null && rightAnd != null)
                if (IsEqvPattern(leftAnd, rightAnd, out var variable1, out var variable2))
                    return new EqvNode(variable1, variable2);
        }

        return null; // Return null instead of original node if no pattern found
    }

    public bool IsXorPattern(AndNode left, AndNode right, out AstNode var1, out AstNode var2)
    {
        var1 = new VariableNode("temp"); // Temporary initialization to avoid nullable warnings
        var2 = new VariableNode("temp");

        // Extract variables from left AND: should be (var1 & !var2) or (!var1 & var2)
        if (!ExtractAndParts(left, out var leftVar1, out var leftVar2, out var leftNeg1, out var leftNeg2))
            return false;

        // Extract variables from right AND: should be opposite negation
        if (!ExtractAndParts(right, out var rightVar1, out var rightVar2, out var rightNeg1, out var rightNeg2))
            return false;

        // Variables must be the same (in any order)
        var sameVars = (AreEquivalentVariables(leftVar1, rightVar1) && AreEquivalentVariables(leftVar2, rightVar2)) ||
                       (AreEquivalentVariables(leftVar1, rightVar2) && AreEquivalentVariables(leftVar2, rightVar1));

        if (!sameVars) return false;

        // Negation patterns must be opposite for XOR
        bool validXorPattern;
        if (AreEquivalentVariables(leftVar1, rightVar1))
        {
            validXorPattern = leftNeg1 != rightNeg1 && leftNeg2 != rightNeg2 && leftNeg1 != leftNeg2;
            var1 = leftVar1;
            var2 = leftVar2;
        }
        else
        {
            validXorPattern = leftNeg1 != rightNeg2 && leftNeg2 != rightNeg1 && leftNeg1 != leftNeg2;
            var1 = leftVar1;
            var2 = leftVar2;
        }

        return validXorPattern;
    }

    public bool IsEqvPattern(AndNode left, AndNode right, out AstNode var1, out AstNode var2)
    {
        var1 = null!; // Initialize to null instead of temp
        var2 = null!;

        // Extract variables from left AND: should be (var1 & var2) or (!var1 & !var2)
        if (!ExtractAndParts(left, out var leftVar1, out var leftVar2, out var leftNeg1, out var leftNeg2))
            return false;

        // Extract variables from right AND: should be opposite negation
        if (!ExtractAndParts(right, out var rightVar1, out var rightVar2, out var rightNeg1, out var rightNeg2))
            return false;

        // Variables must be the same (in any order)
        var sameVars = (AreEquivalentVariables(leftVar1, rightVar1) && AreEquivalentVariables(leftVar2, rightVar2)) ||
                       (AreEquivalentVariables(leftVar1, rightVar2) && AreEquivalentVariables(leftVar2, rightVar1));

        if (!sameVars) return false;

        // Negation patterns must be opposite for EQV: (a & b) | (!a & !b)
        bool validEqvPattern;
        if (AreEquivalentVariables(leftVar1, rightVar1))
        {
            validEqvPattern = leftNeg1 == leftNeg2 && rightNeg1 == rightNeg2 && leftNeg1 != rightNeg1;
            var1 = leftVar1;
            var2 = leftVar2;
        }
        else
        {
            validEqvPattern = leftNeg1 == leftNeg2 && rightNeg1 == rightNeg2 && leftNeg1 != rightNeg2;
            var1 = leftVar1;
            var2 = leftVar2;
        }

        return validEqvPattern;
    }

    /// <summary>
    ///     Split a two-factor AND into its operands, stripping negations.
    ///     Shared by the XOR and EQV pattern checks.
    /// </summary>
    private static bool ExtractAndParts(AndNode and, out AstNode var1, out AstNode var2, out bool neg1, out bool neg2)
    {
        if (and.Left is NotNode leftNot)
        {
            var1 = leftNot.Operand;
            neg1 = true;
        }
        else
        {
            var1 = and.Left;
            neg1 = false;
        }

        if (and.Right is NotNode rightNot)
        {
            var2 = rightNot.Operand;
            neg2 = true;
        }
        else
        {
            var2 = and.Right;
            neg2 = false;
        }

        return true;
    }

    /// <summary>
    /// Check if node matches EQV pattern
    /// </summary>
    public bool IsEqvPattern(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var leftAnd = orNode.Left as AndNode;
            var rightAnd = orNode.Right as AndNode;

            if (leftAnd != null && rightAnd != null)
                return IsEqvPattern(leftAnd, rightAnd, out _, out _);
        }
        return false;
    }

    /// <summary>
    /// Extract EQV parts from AND node (simplified version for tests)
    /// </summary>
    public bool ExtractEqvAndParts(AstNode node, out AstNode left, out AstNode right)
    {
        left = null!;
        right = null!;

        if (node is AndNode andNode)
        {
            left = andNode.Left;
            right = andNode.Right;
            return true;
        }

        return false;
    }

    private bool AreEquivalentVariables(AstNode node1, AstNode node2)
    {
        if (node1 is VariableNode var1 && node2 is VariableNode var2) return var1.Name == var2.Name;
        return false;
    }

    /// <summary>
    ///     Legacy method for backward compatibility with Program.cs
    /// </summary>
    public string GenerateAdvancedLogicalForms(string expression)
    {
        try
        {
            var lexer = new Lexer(expression);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();

            var processedAst = ReplacePatterns(ast);
            return processedAst.ToString();
        }
        catch
        {
            return expression; // Return original if processing fails
        }
    }
}
