using System;
using System.Collections.Generic;
using System.Linq;
using LogicalOptimizer.Optimizers;

namespace LogicalOptimizer;

/// <summary>
/// Detection and conversion of advanced logical patterns (XOR, IMP, EQV) in multi-term
/// OR expressions. The pattern-matching primitives live in <see cref="PatternRecognizer" />;
/// this class contributes the pair-scan strategy over flattened OR terms.
/// </summary>
internal class AdvancedPatternDetector
{
    private readonly PatternRecognizer _recognizer = new();

    /// <summary>
    /// Convert expression by replacing patterns with advanced forms (XOR, IMP, EQV) using AST
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

            return convertedAst.ToString();
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
    /// Detect equivalence pattern in AST
    /// </summary>
    public string DetectEquivalencePattern(AstNode node)
    {
        var result = DetectEquivalencePatternInAst(node);
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
    /// Detect and convert advanced forms in AST node
    /// </summary>
    public AstNode? DetectAdvancedForms(AstNode node)
    {
        var result = DetectAndReplacePatterns(node);

        // If pattern was found and replaced, return new node
        if (result != node) return result;

        // Try specific EQV detection
        var eqvResult = DetectEquivalencePatternInAst(node);
        if (eqvResult != node) return eqvResult;

        return result;
    }

    /// <summary>
    /// Check if node matches EQV pattern
    /// </summary>
    public bool IsEqvPattern(AstNode node)
    {
        if (node is OrNode orNode)
        {
            return TryFindDirectEqvPattern(orNode) != null;
        }
        return false;
    }

    /// <summary>
    ///     Check if two AND nodes form an EQV pattern. Single source of truth:
    ///     <see cref="PatternRecognizer.IsEqvPattern(AndNode, AndNode, out AstNode, out AstNode)" /> —
    ///     this overload exists only as a facade convenience and must stay a delegation.
    /// </summary>
    public bool IsEqvPattern(AndNode left, AndNode right, out AstNode var1, out AstNode var2)
    {
        return _recognizer.IsEqvPattern(left, right, out var1, out var2);
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
    /// Unified pattern detection for XOR, IMP, and EQV patterns in OR expressions
    /// </summary>
    private AstNode? DetectAllPatternsInAst(OrNode orNode)
    {
        return ExtractPairwisePatterns(orNode,
            TryFindDirectXorPattern, TryFindDirectImpPattern, TryFindDirectEqvPattern);
    }

    /// <summary>
    /// Detect XOR pattern in AST and return XOR node if found
    /// </summary>
    private AstNode? DetectXorPatternInAst(AstNode node)
    {
        return node is OrNode orNode ? ExtractPairwisePatterns(orNode, TryFindDirectXorPattern) : null;
    }

    /// <summary>
    /// Generic pair-scan: repeatedly combine pairs of OR-terms that match one of the
    /// given patterns, then reassemble the remaining terms with OR.
    /// Returns null when nothing matched.
    /// </summary>
    private static AstNode? ExtractPairwisePatterns(OrNode orNode, params Func<OrNode, AstNode?>[] matchers)
    {
        // Direct two-term pattern on the node itself first
        foreach (var matcher in matchers)
        {
            var direct = matcher(orNode);
            if (direct != null) return direct;
        }

        var remaining = AstUtilities.FlattenOr(orNode);
        if (remaining.Count < 2) return null;

        // Terms already converted to advanced forms count as found patterns
        var patterns = new List<AstNode>();
        for (var i = remaining.Count - 1; i >= 0; i--)
            if (remaining[i] is ImpNode or XorNode or EqvNode)
            {
                patterns.Add(remaining[i]);
                remaining.RemoveAt(i);
            }

        var foundAny = patterns.Count > 0;
        var foundThisRound = true;
        while (remaining.Count >= 2 && foundThisRound)
        {
            foundThisRound = false;

            for (var i = 0; i < remaining.Count - 1 && !foundThisRound; i++)
                for (var j = i + 1; j < remaining.Count && !foundThisRound; j++)
                {
                    var pair = new OrNode(remaining[i], remaining[j]);

                    foreach (var matcher in matchers)
                    {
                        var match = matcher(pair);
                        if (match == null) continue;

                        patterns.Add(match);
                        remaining.RemoveAt(j); // higher index first
                        remaining.RemoveAt(i);
                        foundThisRound = true;
                        foundAny = true;
                        break;
                    }
                }
        }

        if (!foundAny) return null;

        var allNodes = patterns.Concat(remaining).ToList();
        return allNodes.Count == 1 ? allNodes[0] : allNodes.Aggregate((a, b) => new OrNode(a, b));
    }

    /// <summary>
    /// Detect equivalence pattern in AST and return equivalence node if found
    /// </summary>
    public AstNode? DetectEquivalencePatternInAst(AstNode node)
    {
        // Handle OrNode - check for direct EQV pattern
        if (node is OrNode orNode)
        {
            // Try to find EQV pattern in direct children first
            var directEqv = TryFindDirectEqvPattern(orNode);
            if (directEqv != null) return directEqv;

            // Recursively process children
            var leftProcessed = DetectEquivalencePatternInAst(orNode.Left);
            var rightProcessed = DetectEquivalencePatternInAst(orNode.Right);

            // If any child was transformed, create new OR node
            if (leftProcessed != orNode.Left || rightProcessed != orNode.Right)
            {
                return new OrNode(leftProcessed!, rightProcessed!);
            }
        }
        // Handle AndNode - recursively process children
        else if (node is AndNode andNode)
        {
            var leftProcessed = DetectEquivalencePatternInAst(andNode.Left);
            var rightProcessed = DetectEquivalencePatternInAst(andNode.Right);

            // If any child was transformed, create new AND node
            if (leftProcessed != andNode.Left || rightProcessed != andNode.Right)
            {
                return new AndNode(leftProcessed!, rightProcessed!);
            }
        }
        // Handle NotNode - recursively process child
        else if (node is NotNode notNode)
        {
            var childProcessed = DetectEquivalencePatternInAst(notNode.Operand);
            if (childProcessed != notNode.Operand)
            {
                return new NotNode(childProcessed!);
            }
        }

        // If no pattern found, return original node
        return node;
    }

    /// <summary>
    /// Detect implication pattern in AST and return implication node if found
    /// </summary>
    private AstNode? DetectImplicationPatternInAst(AstNode node)
    {
        return node is OrNode orNode ? ExtractPairwisePatterns(orNode, TryFindDirectImpPattern) : null;
    }

    /// <summary>
    /// Try to find direct XOR pattern in a simple OR node: (a & !b) | (!a & b) → a XOR b
    /// </summary>
    private AstNode? TryFindDirectXorPattern(OrNode orNode)
    {
        if (orNode.Left is AndNode leftAnd && orNode.Right is AndNode rightAnd &&
            _recognizer.IsXorPattern(leftAnd, rightAnd, out var var1, out var var2))
            return new XorNode(var1, var2);

        return null;
    }

    /// <summary>
    /// Try to find direct IMP pattern in a simple OR node: !a | b → a → b
    /// </summary>
    private AstNode? TryFindDirectImpPattern(OrNode orNode)
    {
        return _recognizer.TryMatchStrictImp(orNode);
    }

    /// <summary>
    /// Try to find direct EQV pattern in a simple OR node: (a & b) | (!a & !b) → a ↔ b
    /// </summary>
    public AstNode? TryFindDirectEqvPattern(OrNode orNode)
    {
        if (orNode.Left is AndNode leftAnd && orNode.Right is AndNode rightAnd &&
            _recognizer.IsEqvPattern(leftAnd, rightAnd, out var var1, out var var2))
            return new EqvNode(var1, var2);

        return null;
    }
}
