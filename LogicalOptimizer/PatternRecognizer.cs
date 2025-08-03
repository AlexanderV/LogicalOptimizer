using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicalOptimizer;

/// <summary>
/// Recognizes and replaces advanced logical patterns in AST (XOR, IMP, etc.)
/// </summary>
public class PatternRecognizer
{
    /// <summary>
    /// Replace patterns in an AST with simplified forms
    /// </summary>
    public AstNode ReplacePatterns(AstNode root)
    {
        if (root == null) return null;

        // Process children first (bottom-up)
        var processedRoot = ProcessChildren(root);

        // Try to replace current node
        var result = TryReplaceWithXor(processedRoot);
        if (result != processedRoot) return result;

        result = TryReplaceWithImp(processedRoot);
        if (result != processedRoot) return result;

        return processedRoot;
    }

    private AstNode ProcessChildren(AstNode node)
    {
        switch (node)
        {
            case BinaryNode binary:
                var newLeft = ReplacePatterns(binary.Left);
                var newRight = ReplacePatterns(binary.Right);
                
                if (newLeft != binary.Left || newRight != binary.Right)
                {
                    var newNode = node.Clone();
                    if (newNode is BinaryNode newBinary)
                    {
                        newBinary.Left = newLeft;
                        newBinary.Right = newRight;
                    }
                    return newNode;
                }
                break;
                
            case NotNode not:
                var newChild = ReplacePatterns(not.Operand);
                if (newChild != not.Operand)
                {
                    return new NotNode(newChild);
                }
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
            {
                if (IsXorPattern(leftAnd, rightAnd, out var variable1, out var variable2))
                {
                    return new XorNode(variable1, variable2, true);
                }
            }
        }

        return node;
    }

    private AstNode TryReplaceWithImp(AstNode node)
    {
        // Look for implication pattern: !a | b ≡ a → b
        if (node is OrNode or)
        {
            var leftNot = or.Left as NotNode;
            if (leftNot != null)
            {
                // Pattern: !a | b ≡ a → b
                return new ImpNode(leftNot.Operand, or.Right);
            }
        }

        return node;
    }

    private bool IsXorPattern(AndNode left, AndNode right, out AstNode var1, out AstNode var2)
    {
        var1 = null;
        var2 = null;

        // Extract variables from left AND: should be (var1 & !var2) or (!var1 & var2)
        if (!ExtractXorAndParts(left, out var leftVar1, out var leftVar2, out var leftNeg1, out var leftNeg2))
            return false;

        // Extract variables from right AND: should be opposite negation
        if (!ExtractXorAndParts(right, out var rightVar1, out var rightVar2, out var rightNeg1, out var rightNeg2))
            return false;

        // Variables must be the same (in any order)
        bool sameVars = (AreEquivalentVariables(leftVar1, rightVar1) && AreEquivalentVariables(leftVar2, rightVar2)) ||
                       (AreEquivalentVariables(leftVar1, rightVar2) && AreEquivalentVariables(leftVar2, rightVar1));

        if (!sameVars) return false;

        // Negation patterns must be opposite for XOR
        bool validXorPattern;
        if (AreEquivalentVariables(leftVar1, rightVar1))
        {
            validXorPattern = (leftNeg1 != rightNeg1) && (leftNeg2 != rightNeg2) && (leftNeg1 != leftNeg2);
            var1 = leftVar1;
            var2 = leftVar2;
        }
        else
        {
            validXorPattern = (leftNeg1 != rightNeg2) && (leftNeg2 != rightNeg1) && (leftNeg1 != leftNeg2);
            var1 = leftVar1;
            var2 = leftVar2;
        }

        return validXorPattern;
    }

    private bool ExtractXorAndParts(AndNode and, out AstNode var1, out AstNode var2, out bool neg1, out bool neg2)
    {
        var1 = var2 = null;
        neg1 = neg2 = false;

        // Left part of AND
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

        // Right part of AND
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

        return var1 != null && var2 != null;
    }

    private bool AreEquivalentVariables(AstNode node1, AstNode node2)
    {
        if (node1 is VariableNode var1 && node2 is VariableNode var2)
        {
            return var1.Name == var2.Name;
        }
        return false;
    }

    /// <summary>
    /// Legacy method for backward compatibility with Program.cs
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
