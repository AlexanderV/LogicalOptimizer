using System.Diagnostics;

namespace LogicalOptimizer;

public class ExpressionOptimizer
{
    private readonly int _maxIterations = PerformanceValidator.MAX_OPTIMIZATION_ITERATIONS;

    public AstNode Optimize(AstNode node, OptimizationMetrics? metrics = null)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var stopwatch = Stopwatch.StartNew();
        var originalNodeCount = AstMetrics.CountNodes(node);

        // Validate constraints
        PerformanceValidator.ValidateAst(node);

        if (metrics != null) metrics.OriginalNodes = originalNodeCount;

        AstNode optimized;
        var iterations = 0;

        do
        {
            // Check execution time
            if (stopwatch.Elapsed.TotalSeconds > PerformanceValidator.MAX_PROCESSING_TIME_SECONDS)
                PerformanceValidator.ValidateProcessingTime(stopwatch.Elapsed);

            optimized = node;
            node = ApplyOptimizations(node, metrics);
            iterations++;

            // Check iteration count
            PerformanceValidator.ValidateIterations(iterations);

            // Debug information (can be enabled when needed)
            // Console.WriteLine($"Iteration {iterations}: {node}");
        } while (!AreEqual(optimized, node) && iterations < _maxIterations);

        stopwatch.Stop();

        if (metrics != null)
        {
            metrics.OptimizedNodes = AstMetrics.CountNodes(node);
            metrics.Iterations = iterations;
            metrics.ElapsedTime = stopwatch.Elapsed;
        }

        return node;
    }

    private AstNode ApplyOptimizations(AstNode node, OptimizationMetrics? metrics = null)
    {
        node = ApplyDeMorganLaws(node);
        node = SimplifyDoubleNegation(node);
        node = SimplifyConstants(node);
        node = ApplyAbsorptionLaws(node);
        node = ApplyComplementLaws(node);
        node = ApplyAssociativityLaws(node);
        node = ApplyConsensusRule(node, metrics);
        node = SimplifyRedundantTerms(node);
        node = ApplySmartCommutivity(node); // New rule
        node = ApplyFactorization(node, metrics);
        node = SimplifyConsensusRedundancy(node, metrics);
        node = SimplifyComplexExpressions(node);
        // NOTE: NormalizeExpression might affect ForceParentheses, which could break some tests
        // node = NormalizeExpression(node);

        return node;
    }

    private AstNode ApplyDeMorganLaws(AstNode node)
    {
        if (node is NotNode notNode)
        {
            if (notNode.Operand is AndNode andNode)
                return new OrNode(
                    ApplyDeMorganLaws(new NotNode(andNode.Left)),
                    ApplyDeMorganLaws(new NotNode(andNode.Right))
                );

            if (notNode.Operand is OrNode orNode)
                return new AndNode(
                    ApplyDeMorganLaws(new NotNode(orNode.Left)),
                    ApplyDeMorganLaws(new NotNode(orNode.Right))
                );

            return new NotNode(ApplyDeMorganLaws(notNode.Operand));
        }

        if (node is BinaryNode binaryNode)
        {
            var left = ApplyDeMorganLaws(binaryNode.Left);
            var right = ApplyDeMorganLaws(binaryNode.Right);

            if (node is AndNode originalAnd)
            {
                var result = new AndNode(left, right);
                result.ForceParentheses = originalAnd.ForceParentheses;
                return result;
            }

            if (node is OrNode originalOr)
            {
                var result = new OrNode(left, right);
                result.ForceParentheses = originalOr.ForceParentheses;
                return result;
            }
        }

        return node;
    }

    private AstNode SimplifyDoubleNegation(AstNode node)
    {
        if (node is NotNode notNode && notNode.Operand is NotNode innerNot)
            return SimplifyDoubleNegation(innerNot.Operand);

        if (node is BinaryNode binaryNode)
        {
            var left = SimplifyDoubleNegation(binaryNode.Left);
            var right = SimplifyDoubleNegation(binaryNode.Right);

            if (node is AndNode originalAnd)
            {
                var result = new AndNode(left, right);
                result.ForceParentheses = originalAnd.ForceParentheses;
                return result;
            }

            if (node is OrNode originalOr)
            {
                var result = new OrNode(left, right);
                result.ForceParentheses = originalOr.ForceParentheses;
                return result;
            }
        }

        if (node is NotNode singleNot)
            return new NotNode(SimplifyDoubleNegation(singleNot.Operand));

        return node;
    }

    private AstNode SimplifyConstants(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = SimplifyConstants(andNode.Left);
            var right = SimplifyConstants(andNode.Right);

            if (IsFalse(left) || IsFalse(right)) return CreateFalse();
            if (IsTrue(left)) return right;
            if (IsTrue(right)) return left;
            if (AreComplementary(left, right)) return CreateFalse();

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is OrNode orNode)
        {
            var left = SimplifyConstants(orNode.Left);
            var right = SimplifyConstants(orNode.Right);

            if (IsTrue(left) || IsTrue(right)) return CreateTrue();
            if (IsFalse(left)) return right;
            if (IsFalse(right)) return left;
            if (AreComplementary(left, right)) return CreateTrue();

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
        {
            var operand = SimplifyConstants(notNode.Operand);
            if (IsTrue(operand)) return CreateFalse();
            if (IsFalse(operand)) return CreateTrue();
            return new NotNode(operand);
        }

        return node;
    }

    private AstNode ApplyAbsorptionLaws(AstNode node)
    {
        if (node is AndNode andNode)
        {
            // Standard absorption: A & (A | B) → A
            if (andNode.Right is OrNode rightOr && AreEqual(andNode.Left, rightOr.Left))
                return ApplyAbsorptionLaws(andNode.Left);
            if (andNode.Right is OrNode rightOr2 && AreEqual(andNode.Left, rightOr2.Right))
                return ApplyAbsorptionLaws(andNode.Left);
            if (andNode.Left is OrNode leftOr && AreEqual(andNode.Right, leftOr.Left))
                return ApplyAbsorptionLaws(andNode.Right);
            if (andNode.Left is OrNode leftOr2 && AreEqual(andNode.Right, leftOr2.Right))
                return ApplyAbsorptionLaws(andNode.Right);

            // Extended absorption: A & (!A | B) → A & B
            if (andNode.Right is OrNode rightOrExt)
            {
                if (rightOrExt.Left is NotNode leftNot && AreEqual(andNode.Left, leftNot.Operand))
                {
                    // A & (!A | B) → A & B
                    var result = new AndNode(andNode.Left, ApplyAbsorptionLaws(rightOrExt.Right));
                    result.ForceParentheses = andNode.ForceParentheses;
                    return result;
                }

                if (rightOrExt.Right is NotNode rightNot && AreEqual(andNode.Left, rightNot.Operand))
                {
                    // A & (B | !A) → A & B  
                    var result = new AndNode(andNode.Left, ApplyAbsorptionLaws(rightOrExt.Left));
                    result.ForceParentheses = andNode.ForceParentheses;
                    return result;
                }
            }

            if (AreEqual(andNode.Left, andNode.Right))
                return ApplyAbsorptionLaws(andNode.Left);

            var finalResult = new AndNode(ApplyAbsorptionLaws(andNode.Left), ApplyAbsorptionLaws(andNode.Right));
            finalResult.ForceParentheses = andNode.ForceParentheses;
            return finalResult;
        }

        if (node is OrNode orNode)
        {
            // Standard absorption: A | (A & B) → A
            if (orNode.Right is AndNode rightAnd && AreEqual(orNode.Left, rightAnd.Left))
                return ApplyAbsorptionLaws(orNode.Left);
            if (orNode.Right is AndNode rightAnd2 && AreEqual(orNode.Left, rightAnd2.Right))
                return ApplyAbsorptionLaws(orNode.Left);
            if (orNode.Left is AndNode leftAnd && AreEqual(orNode.Right, leftAnd.Left))
                return ApplyAbsorptionLaws(orNode.Right);
            if (orNode.Left is AndNode leftAnd2 && AreEqual(orNode.Right, leftAnd2.Right))
                return ApplyAbsorptionLaws(orNode.Right);

            // Extended absorption: A | (!A & B) → A | B
            if (orNode.Right is AndNode rightAndExt)
            {
                if (rightAndExt.Left is NotNode leftNot && AreEqual(orNode.Left, leftNot.Operand))
                {
                    // A | (!A & B) → A | B
                    var result = new OrNode(orNode.Left, ApplyAbsorptionLaws(rightAndExt.Right));
                    result.ForceParentheses = orNode.ForceParentheses;
                    return result;
                }

                if (rightAndExt.Right is NotNode rightNot && AreEqual(orNode.Left, rightNot.Operand))
                {
                    // A | (B & !A) → A | B
                    var result = new OrNode(orNode.Left, ApplyAbsorptionLaws(rightAndExt.Left));
                    result.ForceParentheses = orNode.ForceParentheses;
                    return result;
                }
            }

            if (AreEqual(orNode.Left, orNode.Right))
                return ApplyAbsorptionLaws(orNode.Left);

            var finalResult = new OrNode(ApplyAbsorptionLaws(orNode.Left), ApplyAbsorptionLaws(orNode.Right));
            finalResult.ForceParentheses = orNode.ForceParentheses;
            return finalResult;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyAbsorptionLaws(notNode.Operand));

        return node;
    }

    private AstNode ApplyComplementLaws(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = ApplyComplementLaws(andNode.Left);
            var right = ApplyComplementLaws(andNode.Right);

            // Check direct complements
            if (AreComplementary(left, right))
                return CreateFalse();

            // Extended check for AND chains
            var terms = FlattenAnd(new AndNode(left, right));

            // Check all pairs of terms for complementarity
            for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
                if (AreComplementary(terms[i], terms[j]))
                    return CreateFalse(); // A & !A = 0

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is OrNode orNode)
        {
            var left = ApplyComplementLaws(orNode.Left);
            var right = ApplyComplementLaws(orNode.Right);

            // Check direct complements
            if (AreComplementary(left, right))
                return CreateTrue();

            // Extended check for OR chains
            var terms = FlattenOr(new OrNode(left, right));

            // Check all pairs of terms for complementarity
            for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
                if (AreComplementary(terms[i], terms[j]))
                    return CreateTrue(); // A | !A = 1

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyComplementLaws(notNode.Operand));

        return node;
    }

    private AstNode ApplyAssociativityLaws(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);
            terms = terms.Select(ApplyAssociativityLaws).Distinct(new NodeComparer()).ToList();

            if (terms.Count == 1) return terms[0];
            if (terms.Count == 0) return CreateTrue();

            var result = terms.Aggregate((a, b) => new AndNode(a, b));
            // NOTE: preserve ForceParentheses
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            terms = terms.Select(ApplyAssociativityLaws).Distinct(new NodeComparer()).ToList();

            if (terms.Count == 1) return terms[0];
            if (terms.Count == 0) return CreateFalse();

            var result = terms.Aggregate((a, b) => new OrNode(a, b));
            // NOTE: preserve ForceParentheses
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyAssociativityLaws(notNode.Operand));

        return node;
    }

    private AstNode ApplyConsensusRule(AstNode node, OptimizationMetrics? metrics = null)
    {
        var original = node;

        if (node is OrNode orNode)
        {
            // Consensus rule for OR: (A & B) | (!A & C) → (A & B) | (!A & C) | (B & C)
            var terms = FlattenOr(orNode);
            var consensusTerms = new List<AstNode>();

            // Look for pairs of terms to apply consensus
            for (var i = 0; i < terms.Count; i++)
            for (var j = i + 1; j < terms.Count; j++)
            {
                var consensus = FindConsensus(terms[i], terms[j]);
                if (consensus != null && !ContainsTerm(terms, consensus)) consensusTerms.Add(consensus);
            }

            // If consensus terms found, record metrics
            if (consensusTerms.Count > 0 && metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("Consensus", 0);
                metrics.RuleApplicationCount["Consensus"] += consensusTerms.Count;
                metrics.AppliedRules += consensusTerms.Count;
            }

            // Add consensus terms to original ones
            var allTerms = new List<AstNode>(terms);
            allTerms.AddRange(consensusTerms);

            // Recursively apply to each term
            allTerms = allTerms.Select(t => ApplyConsensusRule(t, metrics)).ToList();

            // Remove absorbed terms
            allTerms = RemoveAbsorbedTerms(allTerms);

            if (allTerms.Count == 0) return CreateFalse();
            if (allTerms.Count == 1) return allTerms[0];

            var result = allTerms.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var left = ApplyConsensusRule(andNode.Left, metrics);
            var right = ApplyConsensusRule(andNode.Right, metrics);
            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplyConsensusRule(notNode.Operand, metrics));

        return node;
    }

    private AstNode? FindConsensus(AstNode term1, AstNode term2)
    {
        // Consensus for AND-terms: (A & B) + (!A & C) → (B & C)
        if (term1 is AndNode and1 && term2 is AndNode and2)
        {
            var factors1 = FlattenAnd(and1);
            var factors2 = FlattenAnd(and2);

            // Look for complementary pair
            for (var i = 0; i < factors1.Count; i++)
            for (var j = 0; j < factors2.Count; j++)
                if (AreComplementary(factors1[i], factors2[j]))
                {
                    // Create consensus from remaining factors
                    var remaining1 = factors1.Where((_, idx) => idx != i).ToList();
                    var remaining2 = factors2.Where((_, idx) => idx != j).ToList();

                    var allRemaining = remaining1.Concat(remaining2).Distinct(new NodeComparer()).ToList();

                    if (allRemaining.Count == 0) return CreateTrue();
                    if (allRemaining.Count == 1) return allRemaining[0];
                    return allRemaining.Aggregate((a, b) => new AndNode(a, b));
                }
        }

        // Simple variables: A + !A → 1 (tautology)
        if (AreComplementary(term1, term2)) return CreateTrue();

        return null;
    }

    private bool ContainsTerm(List<AstNode> terms, AstNode term)
    {
        return terms.Any(t => AreEqual(t, term));
    }

    private List<AstNode> RemoveAbsorbedTerms(List<AstNode> terms)
    {
        var result = new List<AstNode>();

        foreach (var term in terms)
        {
            var isAbsorbed = false;

            foreach (var other in terms)
                if (!AreEqual(term, other) && Absorbs(other, term))
                {
                    isAbsorbed = true;
                    break;
                }

            if (!isAbsorbed) result.Add(term);
        }

        return result;
    }

    private AstNode ApplySmartCommutivity(AstNode node)
    {
        // Smart term rearrangement for better factorization
        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);

            // Sort terms for better factorization
            var sortedTerms = terms
                .Select(ApplySmartCommutivity)
                .OrderBy(GetComplexityScore)
                .ThenBy(t => t.ToString())
                .ToList();

            if (sortedTerms.Count == 1) return sortedTerms[0];
            if (sortedTerms.Count == 0) return CreateFalse();

            var result = sortedTerms.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);

            // Sort terms for better factorization  
            var sortedTerms = terms
                .Select(ApplySmartCommutivity)
                .OrderBy(GetComplexityScore)
                .ThenBy(t => t.ToString())
                .ToList();

            if (sortedTerms.Count == 1) return sortedTerms[0];
            if (sortedTerms.Count == 0) return CreateTrue();

            var result = sortedTerms.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(ApplySmartCommutivity(notNode.Operand));

        return node;
    }

    private int GetComplexityScore(AstNode node)
    {
        // Simple variables have the lowest score
        if (node is VariableNode) return 1;

        // Negations are slightly more complex
        if (node is NotNode) return 2;

        // Binary operations are more complex
        if (node is AndNode andNode)
            return 3 + GetComplexityScore(andNode.Left) + GetComplexityScore(andNode.Right);

        if (node is OrNode orNode)
            return 3 + GetComplexityScore(orNode.Left) + GetComplexityScore(orNode.Right);

        return 100; // Unknown nodes go to the end
    }

    private AstNode SimplifyRedundantTerms(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);
            var simplified = new List<AstNode>();

            foreach (var term in terms)
            {
                var simplifiedTerm = SimplifyRedundantTerms(term);

                var absorbed = false;
                for (var i = 0; i < simplified.Count; i++)
                {
                    if (Absorbs(simplified[i], simplifiedTerm))
                    {
                        absorbed = true;
                        break;
                    }

                    if (Absorbs(simplifiedTerm, simplified[i]))
                    {
                        simplified[i] = simplifiedTerm;
                        absorbed = true;
                        break;
                    }
                }

                if (!absorbed)
                    simplified.Add(simplifiedTerm);
            }

            if (simplified.Count == 0) return CreateTrue();
            if (simplified.Count == 1) return simplified[0];

            var result = simplified.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            var simplified = new List<AstNode>();

            foreach (var term in terms)
            {
                var simplifiedTerm = SimplifyRedundantTerms(term);

                var absorbed = false;
                for (var i = 0; i < simplified.Count; i++)
                {
                    if (Absorbs(simplified[i], simplifiedTerm))
                    {
                        absorbed = true;
                        break;
                    }

                    if (Absorbs(simplifiedTerm, simplified[i]))
                    {
                        simplified[i] = simplifiedTerm;
                        absorbed = true;
                        break;
                    }
                }

                if (!absorbed)
                    simplified.Add(simplifiedTerm);
            }

            if (simplified.Count == 0) return CreateFalse();
            if (simplified.Count == 1) return simplified[0];

            var result = simplified.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyRedundantTerms(notNode.Operand));

        return node;
    }

    private AstNode ApplyFactorization(AstNode node, OptimizationMetrics? metrics = null)
    {
        var original = node;
        var result = ApplyFactorizationInternal(node);

        if (metrics != null && !AreEqual(original, result))
        {
            metrics.RuleApplicationCount.TryAdd("Factorization", 0);
            metrics.RuleApplicationCount["Factorization"]++;
            metrics.AppliedRules++;
        }

        return result;
    }

    private AstNode ApplyFactorizationInternal(AstNode node)
    {
        if (node is OrNode orNode)
        {
            var left = ApplyFactorization(orNode.Left);
            var right = ApplyFactorization(orNode.Right);

            // Simple factorization: a & b | a & c = a & (b | c)
            var terms = FlattenOr(new OrNode(left, right));
            var factorized = FactorizeTerms(terms);
            if (factorized != null) return ApplyFactorization(factorized);

            var result = new OrNode(left, right);
            result.ForceParentheses = orNode.ForceParentheses;
            return result;
        }

        if (node is AndNode andNode)
        {
            var left = ApplyFactorization(andNode.Left);
            var right = ApplyFactorization(andNode.Right);

            // Reverse factorization: (a | b) & (a | c) = a | (b & c)
            if (left is OrNode leftOr && right is OrNode rightOr)
            {
                // Check that all 4 variables are comparable
                if (AreEqual(leftOr.Left, rightOr.Left))
                {
                    var common = leftOr.Left;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Right, rightOr.Right, true);
                    return new OrNode(common, ApplyFactorization(remaining));
                }

                if (AreEqual(leftOr.Left, rightOr.Right))
                {
                    var common = leftOr.Left;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Right, rightOr.Left, true);
                    return new OrNode(common, ApplyFactorization(remaining));
                }

                if (AreEqual(leftOr.Right, rightOr.Left))
                {
                    var common = leftOr.Right;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Left, rightOr.Right, true);
                    return new OrNode(common, ApplyFactorization(remaining));
                }

                if (AreEqual(leftOr.Right, rightOr.Right))
                {
                    var common = leftOr.Right;
                    // Remaining part: create AND with forced parentheses
                    var remaining = new AndNode(leftOr.Left, rightOr.Left, true);
                    return new OrNode(common, ApplyFactorization(remaining));
                }
            }

            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode) return new NotNode(ApplyFactorization(notNode.Operand));

        return node;
    }

    private AstNode SimplifyConsensusRedundancy(AstNode node, OptimizationMetrics? metrics = null)
    {
        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            var simplified = new List<AstNode>();
            var rulesApplied = 0;

            foreach (var term in terms)
            {
                var isRedundant = false;

                // Check if this term is redundant consensus
                foreach (var other in terms)
                    if (!AreEqual(term, other) && IsRedundantConsensus(term, other, terms))
                    {
                        isRedundant = true;
                        rulesApplied++;
                        break;
                    }

                if (!isRedundant) simplified.Add(term);
            }

            if (rulesApplied > 0 && metrics != null)
            {
                metrics.RuleApplicationCount.TryAdd("ConsensusSimplification", 0);
                metrics.RuleApplicationCount["ConsensusSimplification"] += rulesApplied;
                metrics.AppliedRules += rulesApplied;
            }

            if (simplified.Count == 0) return CreateFalse();
            if (simplified.Count == 1) return simplified[0];

            var result = simplified.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is AndNode andNode)
        {
            var left = SimplifyConsensusRedundancy(andNode.Left, metrics);
            var right = SimplifyConsensusRedundancy(andNode.Right, metrics);
            var result = new AndNode(left, right);
            result.ForceParentheses = andNode.ForceParentheses;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyConsensusRedundancy(notNode.Operand, metrics));

        return node;
    }

    private bool IsRedundantConsensus(AstNode term, AstNode other, List<AstNode> allTerms)
    {
        // Term is redundant if it's consensus of two other terms in the list
        if (!(term is AndNode termAnd) || !(other is AndNode))
            return false;

        var termFactors = FlattenAnd(termAnd);

        // Look for two terms whose consensus gives current term
        for (var i = 0; i < allTerms.Count; i++)
        for (var j = i + 1; j < allTerms.Count; j++)
        {
            if (AreEqual(allTerms[i], term) || AreEqual(allTerms[j], term))
                continue;

            var consensus = FindConsensus(allTerms[i], allTerms[j]);
            if (consensus != null && AreEqual(consensus, term)) return true;
        }

        return false;
    }

    private AstNode SimplifyComplexExpressions(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var left = SimplifyComplexExpressions(andNode.Left);
            var right = SimplifyComplexExpressions(andNode.Right);

            if (left is AndNode leftAndNode && right is AndNode rightAndNode)
                if (AreEqual(leftAndNode.Left, rightAndNode.Left))
                {
                    var result = new AndNode(leftAndNode.Left,
                        SimplifyComplexExpressions(new AndNode(leftAndNode.Right, rightAndNode.Right)));
                    result.ForceParentheses = andNode.ForceParentheses;
                    return result;
                }

            var resultFinal = new AndNode(left, right);
            resultFinal.ForceParentheses = andNode.ForceParentheses;
            return resultFinal;
        }

        if (node is OrNode orNode)
        {
            var left = SimplifyComplexExpressions(orNode.Left);
            var right = SimplifyComplexExpressions(orNode.Right);

            if (left is OrNode leftOrNode && right is OrNode rightOrNode)
                if (AreEqual(leftOrNode.Left, rightOrNode.Left))
                {
                    var result = new OrNode(leftOrNode.Left,
                        SimplifyComplexExpressions(new OrNode(leftOrNode.Right, rightOrNode.Right)));
                    result.ForceParentheses = orNode.ForceParentheses;
                    return result;
                }

            var resultFinal = new OrNode(left, right);
            resultFinal.ForceParentheses = orNode.ForceParentheses;
            return resultFinal;
        }

        if (node is NotNode notNode)
            return new NotNode(SimplifyComplexExpressions(notNode.Operand));

        return node;
    }

    private AstNode NormalizeExpression(AstNode node)
    {
        if (node is AndNode andNode)
        {
            var terms = FlattenAnd(andNode);
            terms = terms.Select(NormalizeExpression).OrderBy(t => t.ToString()).ToList();

            if (terms.Count == 0) return CreateTrue();
            if (terms.Count == 1) return terms[0];

            // NOTE: preserve ForceParentheses for consistency
            var result = terms.Aggregate((a, b) => new AndNode(a, b));
            if (result is AndNode resultAnd && andNode.ForceParentheses) resultAnd.ForceParentheses = true;
            return result;
        }

        if (node is OrNode orNode)
        {
            var terms = FlattenOr(orNode);
            terms = terms.Select(NormalizeExpression).OrderBy(t => t.ToString()).ToList();

            if (terms.Count == 0) return CreateFalse();
            if (terms.Count == 1) return terms[0];

            // NOTE: preserve ForceParentheses for consistency
            var result = terms.Aggregate((a, b) => new OrNode(a, b));
            if (result is OrNode resultOr && orNode.ForceParentheses) resultOr.ForceParentheses = true;
            return result;
        }

        if (node is NotNode notNode)
            return new NotNode(NormalizeExpression(notNode.Operand));

        return node;
    }

    // Helper methods
    private bool IsTrue(AstNode node)
    {
        return node is VariableNode var && (var.Name == "1" || var.Name == "true");
    }

    private bool IsFalse(AstNode node)
    {
        return node is VariableNode var && (var.Name == "0" || var.Name == "false");
    }

    private AstNode CreateTrue()
    {
        return new VariableNode("1");
    }

    private AstNode CreateFalse()
    {
        return new VariableNode("0");
    }

    private bool AreComplementary(AstNode node1, AstNode node2)
    {
        if (node1 is NotNode notNode1)
            return AreEqual(notNode1.Operand, node2);
        if (node2 is NotNode notNode2)
            return AreEqual(node1, notNode2.Operand);
        return false;
    }

    private List<AstNode> FlattenAnd(AndNode node)
    {
        var result = new List<AstNode>();
        if (node.Left is AndNode leftAnd)
            result.AddRange(FlattenAnd(leftAnd));
        else
            result.Add(node.Left);

        if (node.Right is AndNode rightAnd)
            result.AddRange(FlattenAnd(rightAnd));
        else
            result.Add(node.Right);

        return result;
    }

    private List<AstNode> FlattenOr(OrNode node)
    {
        var result = new List<AstNode>();
        if (node.Left is OrNode leftOr)
            result.AddRange(FlattenOr(leftOr));
        else
            result.Add(node.Left);

        if (node.Right is OrNode rightOr)
            result.AddRange(FlattenOr(rightOr));
        else
            result.Add(node.Right);

        return result;
    }

    private bool IsConsensusRedundant(AstNode term1, AstNode term2, AstNode term3)
    {
        return false; // Simplified implementation
    }

    private bool Absorbs(AstNode absorber, AstNode absorbed)
    {
        if (AreEqual(absorber, absorbed)) return true;

        if (absorbed is AndNode absorbedAndNode)
            return AreEqual(absorber, absorbedAndNode.Left) || AreEqual(absorber, absorbedAndNode.Right);

        return false;
    }

    private AstNode? FactorizeTerms(List<AstNode> terms)
    {
        if (terms.Count < 2) return null;

        // Look for AND-terms that have common factors
        for (var i = 0; i < terms.Count; i++)
        {
            var currentTerm = terms[i];

            if (currentTerm is AndNode currentAnd)
            {
                var factors = FlattenAnd(currentAnd);

                foreach (var factor in factors)
                {
                    // Check if this factor is contained in all terms
                    var containsInAll = terms.All(term => ContainsFactor(term, factor));

                    if (containsInAll)
                    {
                        // Factor out common factor
                        var remainingTerms = terms
                            .Select(term => RemoveFactor(term, factor))
                            .Where(remaining => !IsTrue(remaining))
                            .ToList();

                        if (remainingTerms.Count == 0) return factor;

                        var remaining = remainingTerms.Count == 1
                            ? remainingTerms[0]
                            : remainingTerms.Aggregate((a, b) => new OrNode(a, b));

                        // Force parentheses: create AND with forced parentheses for OR part
                        AstNode result;
                        if (remaining is OrNode remainingOr)
                            // Don't create new OR with forced parentheses - they will be added automatically
                            result = new AndNode(factor, remaining);
                        else
                            result = new AndNode(factor, remaining);

                        return result;
                    }
                }
            }
        }

        return null;
    }

    private bool ContainsFactor(AstNode term, AstNode factor)
    {
        if (AreEqual(term, factor))
            return true;

        if (term is AndNode andTerm)
        {
            var factors = FlattenAnd(andTerm);
            return factors.Any(f => AreEqual(f, factor));
        }

        return false;
    }

    private AstNode RemoveFactor(AstNode term, AstNode factor)
    {
        if (AreEqual(term, factor))
            return CreateTrue(); // a / a = 1

        if (term is AndNode andTerm)
        {
            var factors = FlattenAnd(andTerm)
                .Where(f => !AreEqual(f, factor))
                .ToList();

            if (factors.Count == 0)
                return CreateTrue();
            if (factors.Count == 1)
                return factors[0];

            return factors.Aggregate((a, b) => new AndNode(a, b));
        }

        return term; // No factor found, return original term
    }

    private bool AreEqual(AstNode node1, AstNode node2)
    {
        return node1.Equals(node2);
    }

    // Comparer for AST nodes
    private class NodeComparer : IEqualityComparer<AstNode>
    {
        public bool Equals(AstNode? x, AstNode? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(AstNode? obj)
        {
            return obj?.GetHashCode() ?? 0;
        }
    }
}