using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogicalOptimizer;

/// <summary>
/// Handles detection and conversion of advanced logical patterns (XOR, IMP)
/// </summary>
public class AdvancedPatternDetector
{
    /// <summary>
    /// Convert expression by replacing patterns with advanced forms (XOR, IMP) using AST
    /// </summary>
    public string ConvertToAdvancedForms(string expr)
    {
        try
        {
            // Parse expression to AST
            var lexer = new Lexer(expr);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();

            // Try to convert the AST to advanced forms
            var convertedAst = ConvertAstToAdvancedForms(ast);

            // Convert back to string and simplify
            var result = convertedAst.ToString();
            return SimplifyStringRepresentation(result);
        }
        catch
        {
            return expr; // Return original if parsing fails
        }
    }

    /// <summary>
    /// Detect XOR pattern in AST
    /// </summary>
    public string DetectXorPattern(AstNode node)
    {
        var result = DetectXorPatternInAst(node);
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Detect implication pattern in AST
    /// </summary>
    public string DetectImplicationPattern(AstNode node)
    {
        var result = DetectImplicationPatternInAst(node);
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Convert AST node to advanced forms (XOR, IMP) recursively
    /// </summary>
    private AstNode ConvertAstToAdvancedForms(AstNode node)
    {
        // First try to detect patterns in the current node before recursing
        var patternResult = DetectAndReplacePatterns(node);
        if (patternResult != node) return patternResult; // Found a pattern, return it

        // If no pattern found, recursively convert children
        var convertedNode = node switch
        {
            OrNode orNode => new OrNode(
                ConvertAstToAdvancedForms(orNode.Left),
                ConvertAstToAdvancedForms(orNode.Right),
                orNode.ForceParentheses
            ),
            AndNode andNode => new AndNode(
                ConvertAstToAdvancedForms(andNode.Left),
                ConvertAstToAdvancedForms(andNode.Right),
                andNode.ForceParentheses
            ),
            NotNode notNode => new NotNode(
                ConvertAstToAdvancedForms(notNode.Operand)
            ),
            _ => node
        };

        // Try to detect patterns in the converted node one more time
        return DetectAndReplacePatterns(convertedNode);
    }

    /// <summary>
    /// Detect and replace patterns (XOR, IMP) in AST node using unified scanning
    /// </summary>
    private AstNode DetectAndReplacePatterns(AstNode node)
    {
        if (node is not OrNode orNode) return node;

        // Use unified pattern detection that finds both XOR and IMP patterns in one scan
        var result = DetectAllPatternsInAst(orNode);
        return result ?? node;
    }

    /// <summary>
    /// Unified pattern detection for both XOR and IMP patterns in OR expressions
    /// </summary>
    private AstNode? DetectAllPatternsInAst(OrNode orNode)
    {
        // Try direct patterns first (two-term OR)
        var directXor = TryFindDirectXorPattern(orNode);
        if (directXor != null) return directXor;

        var directImp = TryFindDirectImpPattern(orNode);
        if (directImp != null) return directImp;

        // For complex OR expressions with multiple terms, find all patterns
        var orTerms = CollectOrTerms(orNode);
        if (orTerms.Count < 2) return null;

        // First pass: check each individual OR term for IMP patterns
        var processedTerms = new List<AstNode>();
        foreach (var term in orTerms)
        {
            if (term is OrNode termOr)
            {
                var impResult = TryFindDirectImpPattern(termOr);
                if (impResult != null)
                    processedTerms.Add(impResult);
                else
                    processedTerms.Add(term);
            }
            else
            {
                processedTerms.Add(term);
            }
        }

        // Second pass: find XOR and IMP patterns between terms
        var patternNodes = new List<AstNode>();
        var remainingTerms = new List<AstNode>(processedTerms);

        // Collect already converted IMP nodes
        for (var i = remainingTerms.Count - 1; i >= 0; i--)
        {
            if (remainingTerms[i] is ImpNode)
            {
                patternNodes.Add(remainingTerms[i]);
                remainingTerms.RemoveAt(i);
            }
        }

        // Continue searching for both XOR and IMP patterns
        var foundAnyPattern = patternNodes.Count > 0;
        while (remainingTerms.Count >= 2)
        {
            var foundPatternInThisIteration = false;

            for (var i = 0; i < remainingTerms.Count - 1 && !foundPatternInThisIteration; i++)
            {
                for (var j = i + 1; j < remainingTerms.Count && !foundPatternInThisIteration; j++)
                {
                    var term1 = remainingTerms[i];
                    var term2 = remainingTerms[j];

                    // Create a temporary OR node to test patterns
                    var tempOr = new OrNode(term1, term2);

                    // Try XOR pattern first (it's more specific)
                    var xorResult = TryFindDirectXorPattern(tempOr);
                    if (xorResult != null)
                    {
                        patternNodes.Add(xorResult);

                        // Remove the two terms that formed the XOR
                        remainingTerms.RemoveAt(j); // Remove j first (higher index)
                        remainingTerms.RemoveAt(i); // Then remove i

                        foundPatternInThisIteration = true;
                        foundAnyPattern = true;
                        continue;
                    }

                    // Try IMP pattern
                    var impResult = TryFindDirectImpPattern(tempOr);
                    if (impResult != null)
                    {
                        patternNodes.Add(impResult);

                        // Remove the two terms that formed the IMP
                        remainingTerms.RemoveAt(j); // Remove j first (higher index)
                        remainingTerms.RemoveAt(i); // Then remove i

                        foundPatternInThisIteration = true;
                        foundAnyPattern = true;
                    }
                }
            }

            if (!foundPatternInThisIteration) break; // No more patterns found
        }

        if (!foundAnyPattern) return null;

        // Combine all pattern nodes and remaining terms
        var allNodes = new List<AstNode>();
        allNodes.AddRange(patternNodes);
        allNodes.AddRange(remainingTerms);

        if (allNodes.Count == 1) return allNodes[0];

        // Combine all nodes with OR
        var result = allNodes[0];
        for (var i = 1; i < allNodes.Count; i++) result = new OrNode(result, allNodes[i]);
        return result;
    }

    /// <summary>
    /// Detect XOR pattern in AST and return XOR node if found
    /// </summary>
    private AstNode? DetectXorPatternInAst(AstNode node)
    {
        if (node is not OrNode orNode) return null;

        // Try to find XOR pattern in direct children first
        var directXor = TryFindDirectXorPattern(orNode);
        if (directXor != null) return directXor;

        // For complex OR expressions with multiple terms, iteratively find and replace XOR patterns
        var orTerms = CollectOrTerms(orNode);
        if (orTerms.Count < 2) return null;

        // Find all XOR patterns and collect them
        var xorNodes = new List<AstNode>();
        var remainingTerms = new List<AstNode>(orTerms);

        // Continue searching for XOR patterns until no more found
        var foundAnyXor = false;
        while (remainingTerms.Count >= 2)
        {
            var foundXorInThisIteration = false;

            for (var i = 0; i < remainingTerms.Count - 1 && !foundXorInThisIteration; i++)
            {
                for (var j = i + 1; j < remainingTerms.Count && !foundXorInThisIteration; j++)
                {
                    var term1 = remainingTerms[i];
                    var term2 = remainingTerms[j];

                    // Create a temporary OR node to test XOR pattern
                    var tempOr = new OrNode(term1, term2);
                    var xorResult = TryFindDirectXorPattern(tempOr);

                    if (xorResult != null)
                    {
                        // Found XOR pattern!
                        xorNodes.Add(xorResult);

                        // Remove the two terms that formed the XOR
                        remainingTerms.RemoveAt(j); // Remove j first (higher index)
                        remainingTerms.RemoveAt(i); // Then remove i

                        foundXorInThisIteration = true;
                        foundAnyXor = true;
                    }
                }
            }

            if (!foundXorInThisIteration) break; // No more XOR patterns found
        }

        if (!foundAnyXor) return null;

        // Combine all XOR nodes and remaining terms
        var allNodes = new List<AstNode>();
        allNodes.AddRange(xorNodes);
        allNodes.AddRange(remainingTerms);

        if (allNodes.Count == 1) return allNodes[0];

        // Combine all nodes with OR
        var result = allNodes[0];
        for (var i = 1; i < allNodes.Count; i++) result = new OrNode(result, allNodes[i]);
        return result;
    }

    /// <summary>
    /// Detect implication pattern in AST and return implication node if found
    /// </summary>
    private AstNode? DetectImplicationPatternInAst(AstNode node)
    {
        if (node is not OrNode orNode) return null;

        // Try to find IMP pattern in direct children first
        var directImp = TryFindDirectImpPattern(orNode);
        if (directImp != null) return directImp;

        // For complex OR expressions with multiple terms, find and replace IMP patterns
        var orTerms = CollectOrTerms(orNode);
        if (orTerms.Count < 2) return null;

        // First, check each individual term to see if it's already an IMP pattern
        var processedTerms = new List<AstNode>();
        foreach (var term in orTerms)
        {
            if (term is OrNode termOr)
            {
                var impResult = TryFindDirectImpPattern(termOr);
                if (impResult != null)
                    processedTerms.Add(impResult);
                else
                    processedTerms.Add(term);
            }
            else
            {
                processedTerms.Add(term);
            }
        }

        // Now find IMP patterns between remaining non-IMP terms
        var impNodes = new List<AstNode>();
        var remainingTerms = new List<AstNode>(processedTerms);

        // Collect already converted IMP nodes
        for (var i = remainingTerms.Count - 1; i >= 0; i--)
        {
            if (remainingTerms[i] is ImpNode)
            {
                impNodes.Add(remainingTerms[i]);
                remainingTerms.RemoveAt(i);
            }
        }

        // Continue searching for IMP patterns until no more found
        var foundAnyImp = impNodes.Count > 0;
        while (remainingTerms.Count >= 2)
        {
            var foundImpInThisIteration = false;

            for (var i = 0; i < remainingTerms.Count - 1 && !foundImpInThisIteration; i++)
            {
                for (var j = i + 1; j < remainingTerms.Count && !foundImpInThisIteration; j++)
                {
                    var term1 = remainingTerms[i];
                    var term2 = remainingTerms[j];

                    // Create a temporary OR node to test IMP pattern
                    var tempOr = new OrNode(term1, term2);
                    var impResult = TryFindDirectImpPattern(tempOr);

                    if (impResult != null)
                    {
                        // Found IMP pattern!
                        impNodes.Add(impResult);

                        // Remove the two terms that formed the IMP
                        remainingTerms.RemoveAt(j); // Remove j first (higher index)
                        remainingTerms.RemoveAt(i); // Then remove i

                        foundImpInThisIteration = true;
                        foundAnyImp = true;
                    }
                }
            }

            if (!foundImpInThisIteration) break; // No more IMP patterns found
        }

        if (!foundAnyImp) return null;

        // Combine all IMP nodes and remaining terms
        var allNodes = new List<AstNode>();
        allNodes.AddRange(impNodes);
        allNodes.AddRange(remainingTerms);

        if (allNodes.Count == 1) return allNodes[0];

        // Combine all nodes with OR
        var result = allNodes[0];
        for (var i = 1; i < allNodes.Count; i++) result = new OrNode(result, allNodes[i]);
        return result;
    }

    /// <summary>
    /// Try to find direct XOR pattern in a simple OR node: (a & !b) | (!a & b)
    /// </summary>
    private AstNode? TryFindDirectXorPattern(OrNode orNode)
    {
        // Pattern: (a & !b) | (!a & b) → a XOR b
        if (orNode.Left is AndNode leftAnd && orNode.Right is AndNode rightAnd)
        {
            var leftVars = ExtractAndTermVariables(leftAnd);
            var rightVars = ExtractAndTermVariables(rightAnd);

            if (leftVars.Count == 2 && rightVars.Count == 2)
            {
                var (var1Left, neg1Left) = leftVars[0];
                var (var2Left, neg2Left) = leftVars[1];
                var (var1Right, neg1Right) = rightVars[0];
                var (var2Right, neg2Right) = rightVars[1];

                // Check if it's XOR pattern: a & !b | !a & b
                if (IsXorPattern(var1Left, neg1Left, var2Left, neg2Left, var1Right, neg1Right, var2Right, neg2Right))
                {
                    var varA = neg1Left ? var2Left : var1Left;
                    var varB = neg1Left ? var1Left : var2Left;
                    return new XorNode(
                        new VariableNode(varA),
                        new VariableNode(varB)
                    );
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Try to find direct IMP pattern in a simple OR node: !a | b → a → b
    /// </summary>
    private AstNode? TryFindDirectImpPattern(OrNode orNode)
    {
        var leftTerm = orNode.Left;
        var rightTerm = orNode.Right;

        // Pattern 1: !a | b → a → b
        if (leftTerm is NotNode notLeft && rightTerm is VariableNode varRight)
        {
            if (notLeft.Operand is VariableNode varLeftInner)
                return new ImpNode(varLeftInner, varRight);
        }

        // Pattern 2: b | !a → a → b  
        if (rightTerm is NotNode notRight && leftTerm is VariableNode varLeft)
        {
            if (notRight.Operand is VariableNode varRightInner)
                return new ImpNode(varRightInner, varLeft);
        }

        return null;
    }

    /// <summary>
    /// Collect all terms from a nested OR expression into a flat list
    /// </summary>
    private List<AstNode> CollectOrTerms(OrNode orNode)
    {
        var terms = new List<AstNode>();
        CollectOrTermsRecursive(orNode, terms);
        return terms;
    }

    /// <summary>
    /// Recursively collect OR terms
    /// </summary>
    private void CollectOrTermsRecursive(AstNode node, List<AstNode> terms)
    {
        if (node is OrNode orNode)
        {
            CollectOrTermsRecursive(orNode.Left, terms);
            CollectOrTermsRecursive(orNode.Right, terms);
        }
        else
        {
            terms.Add(node);
        }
    }

    /// <summary>
    /// Extract variables and their negation status from AND terms
    /// </summary>
    private List<(string variable, bool isNegated)> ExtractAndTermVariables(AndNode andNode)
    {
        var variables = new List<(string, bool)>();
        ExtractAndTermVariablesRecursive(andNode, variables);
        return variables;
    }

    /// <summary>
    /// Recursively extract variables from AND terms
    /// </summary>
    private void ExtractAndTermVariablesRecursive(AstNode node, List<(string, bool)> variables)
    {
        switch (node)
        {
            case AndNode andNode:
                ExtractAndTermVariablesRecursive(andNode.Left, variables);
                ExtractAndTermVariablesRecursive(andNode.Right, variables);
                break;
            case NotNode notNode when notNode.Operand is VariableNode varNode:
                variables.Add((varNode.Name, true));
                break;
            case VariableNode varNode:
                variables.Add((varNode.Name, false));
                break;
        }
    }

    /// <summary>
    /// Check if the variables form a XOR pattern
    /// </summary>
    private bool IsXorPattern(string var1Left, bool neg1Left, string var2Left, bool neg2Left,
        string var1Right, bool neg1Right, string var2Right, bool neg2Right)
    {
        // XOR pattern: (a & !b) | (!a & b)
        // Left side: one var normal, one negated
        // Right side: opposite pattern with same variables

        if (neg1Left == neg2Left || neg1Right == neg2Right) return false; // Both same polarity

        // Extract the positive and negative variables from each side
        var leftPos = neg1Left ? var2Left : var1Left;
        var leftNeg = neg1Left ? var1Left : var2Left;
        var rightPos = neg1Right ? var2Right : var1Right;
        var rightNeg = neg1Right ? var1Right : var2Right;

        // Check if patterns are opposite: left positive = right negative, left negative = right positive
        return leftPos == rightNeg && leftNeg == rightPos;
    }

    /// <summary>
    /// Simplify string representation by removing redundant parentheses and spaces
    /// </summary>
    private string SimplifyStringRepresentation(string result)
    {
        // Remove extra spaces around operators
        result = Regex.Replace(result, @"\s+", " ");
        result = Regex.Replace(result, @"\s*([&|])\s*", " $1 ");
        
        // Trim
        return result.Trim();
    }
}
