using System;
using Xunit;

namespace LogicalOptimizer.Tests
{
    /// <summary>
    /// Tests for ExtendedOptimizationRules - XOR, NAND, NOR optimization rules
    /// </summary>
    public class ExtendedOptimizationRulesTests
    {
        #region XOR Rules Tests

        [Fact]
        public void XorRules_IdempotentLaw_SameOperands_ShouldReturnZero()
        {
            // Arrange
            var a = new VariableNode("a");
            var xorNode = new XorNode(a, a);

            // Act
            var result = ExtendedOptimizationRules.XorRules.IdempotentLaw(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("0", result.ToString());
        }

        [Fact]
        public void XorRules_IdempotentLaw_DifferentOperands_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var xorNode = new XorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.XorRules.IdempotentLaw(xorNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void XorRules_NeutralElement_LeftZero_ShouldReturnRight()
        {
            // Arrange
            var zero = new ConstantNode(false);
            var a = new VariableNode("a");
            var xorNode = new XorNode(zero, a);

            // Act
            var result = ExtendedOptimizationRules.XorRules.NeutralElement(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(a, result);
        }

        [Fact]
        public void XorRules_NeutralElement_RightZero_ShouldReturnLeft()
        {
            // Arrange
            var a = new VariableNode("a");
            var zero = new ConstantNode(false);
            var xorNode = new XorNode(a, zero);

            // Act
            var result = ExtendedOptimizationRules.XorRules.NeutralElement(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(a, result);
        }

        [Fact]
        public void XorRules_NeutralElement_NoZero_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var xorNode = new XorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.XorRules.NeutralElement(xorNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void XorRules_ComplementWithOne_LeftOne_ShouldReturnNotRight()
        {
            // Arrange
            var one = new ConstantNode(true);
            var a = new VariableNode("a");
            var xorNode = new XorNode(one, a);

            // Act
            var result = ExtendedOptimizationRules.XorRules.ComplementWithOne(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void XorRules_ComplementWithOne_RightOne_ShouldReturnNotLeft()
        {
            // Arrange
            var a = new VariableNode("a");
            var one = new ConstantNode(true);
            var xorNode = new XorNode(a, one);

            // Act
            var result = ExtendedOptimizationRules.XorRules.ComplementWithOne(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void XorRules_ComplementWithOne_NoOne_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var xorNode = new XorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.XorRules.ComplementWithOne(xorNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void XorRules_ComplementLaw_LeftNotRight_ShouldReturnOne()
        {
            // Arrange
            var a = new VariableNode("a");
            var notA = new NotNode(a);
            var xorNode = new XorNode(notA, a);

            // Act
            var result = ExtendedOptimizationRules.XorRules.ComplementLaw(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("1", result.ToString());
        }

        [Fact]
        public void XorRules_ComplementLaw_RightNotLeft_ShouldReturnOne()
        {
            // Arrange
            var a = new VariableNode("a");
            var notA = new NotNode(a);
            var xorNode = new XorNode(a, notA);

            // Act
            var result = ExtendedOptimizationRules.XorRules.ComplementLaw(xorNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("1", result.ToString());
        }

        [Fact]
        public void XorRules_ComplementLaw_NoComplement_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var xorNode = new XorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.XorRules.ComplementLaw(xorNode);

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region NAND Rules Tests

        [Fact]
        public void NandRules_IdempotentLaw_SameOperands_ShouldReturnNotOperand()
        {
            // Arrange
            var a = new VariableNode("a");
            var nandNode = new NandNode(a, a);

            // Act
            var result = ExtendedOptimizationRules.NandRules.IdempotentLaw(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void NandRules_IdempotentLaw_DifferentOperands_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var nandNode = new NandNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NandRules.IdempotentLaw(nandNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void NandRules_ZeroAbsorption_LeftZero_ShouldReturnOne()
        {
            // Arrange
            var zero = new ConstantNode(false);
            var a = new VariableNode("a");
            var nandNode = new NandNode(zero, a);

            // Act
            var result = ExtendedOptimizationRules.NandRules.ZeroAbsorption(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("1", result.ToString());
        }

        [Fact]
        public void NandRules_ZeroAbsorption_RightZero_ShouldReturnOne()
        {
            // Arrange
            var a = new VariableNode("a");
            var zero = new ConstantNode(false);
            var nandNode = new NandNode(a, zero);

            // Act
            var result = ExtendedOptimizationRules.NandRules.ZeroAbsorption(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("1", result.ToString());
        }

        [Fact]
        public void NandRules_ZeroAbsorption_NoZero_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var nandNode = new NandNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NandRules.ZeroAbsorption(nandNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void NandRules_OneNeutral_LeftOne_ShouldReturnNotRight()
        {
            // Arrange
            var one = new ConstantNode(true);
            var a = new VariableNode("a");
            var nandNode = new NandNode(one, a);

            // Act
            var result = ExtendedOptimizationRules.NandRules.OneNeutral(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void NandRules_OneNeutral_RightOne_ShouldReturnNotLeft()
        {
            // Arrange
            var a = new VariableNode("a");
            var one = new ConstantNode(true);
            var nandNode = new NandNode(a, one);

            // Act
            var result = ExtendedOptimizationRules.NandRules.OneNeutral(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void NandRules_OneNeutral_NoOne_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var nandNode = new NandNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NandRules.OneNeutral(nandNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void NandRules_ToBasicOperators_ShouldReturnNotAnd()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var nandNode = new NandNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NandRules.ToBasicOperators(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.IsType<AndNode>(notNode.Operand);
            var andNode = (AndNode)notNode.Operand;
            Assert.Equal(a, andNode.Operands[0]);
            Assert.Equal(b, andNode.Operands[1]);
        }

        #endregion

        #region NOR Rules Tests

        [Fact]
        public void NorRules_IdempotentLaw_SameOperands_ShouldReturnNotOperand()
        {
            // Arrange
            var a = new VariableNode("a");
            var norNode = new NorNode(a, a);

            // Act
            var result = ExtendedOptimizationRules.NorRules.IdempotentLaw(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void NorRules_IdempotentLaw_DifferentOperands_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var norNode = new NorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NorRules.IdempotentLaw(norNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void NorRules_OneAbsorption_LeftOne_ShouldReturnZero()
        {
            // Arrange
            var one = new ConstantNode(true);
            var a = new VariableNode("a");
            var norNode = new NorNode(one, a);

            // Act
            var result = ExtendedOptimizationRules.NorRules.OneAbsorption(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("0", result.ToString());
        }

        [Fact]
        public void NorRules_OneAbsorption_RightOne_ShouldReturnZero()
        {
            // Arrange
            var a = new VariableNode("a");
            var one = new ConstantNode(true);
            var norNode = new NorNode(a, one);

            // Act
            var result = ExtendedOptimizationRules.NorRules.OneAbsorption(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("0", result.ToString());
        }

        [Fact]
        public void NorRules_OneAbsorption_NoOne_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var norNode = new NorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NorRules.OneAbsorption(norNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void NorRules_ZeroNeutral_LeftZero_ShouldReturnNotRight()
        {
            // Arrange
            var zero = new ConstantNode(false);
            var a = new VariableNode("a");
            var norNode = new NorNode(zero, a);

            // Act
            var result = ExtendedOptimizationRules.NorRules.ZeroNeutral(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void NorRules_ZeroNeutral_RightZero_ShouldReturnNotLeft()
        {
            // Arrange
            var a = new VariableNode("a");
            var zero = new ConstantNode(false);
            var norNode = new NorNode(a, zero);

            // Act
            var result = ExtendedOptimizationRules.NorRules.ZeroNeutral(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.Equal(a, notNode.Operand);
        }

        [Fact]
        public void NorRules_ZeroNeutral_NoZero_ShouldReturnNull()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var norNode = new NorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NorRules.ZeroNeutral(norNode);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void NorRules_ToBasicOperators_ShouldReturnNotOr()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            var norNode = new NorNode(a, b);

            // Act
            var result = ExtendedOptimizationRules.NorRules.ToBasicOperators(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = (NotNode)result;
            Assert.IsType<OrNode>(notNode.Operand);
            var orNode = (OrNode)notNode.Operand;
            Assert.Equal(a, orNode.Operands[0]);
            Assert.Equal(b, orNode.Operands[1]);
        }

        #endregion

        #region Semantic soundness of the whole rule library

        /// <summary>
        ///     Operand grid: constants, both polarities of two variables, and a composite plus its
        ///     negation. Rich enough that every rule below finds a firing case, small enough that a
        ///     full 2-variable truth table is the oracle.
        /// </summary>
        private static IEnumerable<AstNode> Operands()
        {
            var a = new VariableNode("a");
            var b = new VariableNode("b");
            yield return ConstantNode.False;
            yield return ConstantNode.True;
            yield return a;
            yield return new NotNode(a);
            yield return b;
            yield return new NotNode(b);
            yield return new AndNode(a, b);
            yield return new NotNode(new AndNode(a, b));
        }

        /// <summary>
        ///     Compares two ASTs on every assignment over {a, b} — an oracle independent of the rule
        ///     under test (the shape assertions above only re-state the implementation's formula).
        /// </summary>
        private static void AssertPointwiseEqual(string what, AstNode before, AstNode after)
        {
            var assignment = new Dictionary<string, bool>();
            for (var m = 0; m < 4; m++)
            {
                assignment["a"] = (m & 1) != 0;
                assignment["b"] = (m & 2) != 0;
                Assert.True(
                    TruthTable.Evaluate(before, assignment) == TruthTable.Evaluate(after, assignment),
                    $"{what}: '{before}' was rewritten to '{after}', which differs at " +
                    $"a={assignment["a"]}, b={assignment["b"]}");
            }
        }

        /// <summary>Applies one rule across the whole operand grid; returns how often it fired.</summary>
        private static int Sweep<TNode>(string name, Func<AstNode, AstNode, TNode> build,
            Func<TNode, AstNode?> rule) where TNode : AstNode
        {
            var fired = 0;
            foreach (var left in Operands())
                foreach (var right in Operands())
                {
                    var node = build(left, right);
                    var rewritten = rule(node);
                    if (rewritten == null) continue;
                    fired++;
                    AssertPointwiseEqual(name, node, rewritten);
                }

            return fired;
        }

        [Fact]
        public void EveryRule_WhenItFires_PreservesSemantics()
        {
            // The per-rule tests above pin WHICH node each rule returns, but they assert the shape
            // the implementation happens to build — a mirror of the code, not a check on it. A rule
            // returning the wrong constant, the wrong operand or the wrong polarity is only caught by
            // an independent oracle, so every rule is swept over the operand grid and every rewrite
            // it performs must agree with the input on all four assignments.
            var fired = new Dictionary<string, int>
            {
                ["Xor.IdempotentLaw"] = Sweep("Xor.IdempotentLaw",
                    (l, r) => new XorNode(l, r), ExtendedOptimizationRules.XorRules.IdempotentLaw),
                ["Xor.NeutralElement"] = Sweep("Xor.NeutralElement",
                    (l, r) => new XorNode(l, r), ExtendedOptimizationRules.XorRules.NeutralElement),
                ["Xor.ComplementWithOne"] = Sweep("Xor.ComplementWithOne",
                    (l, r) => new XorNode(l, r), ExtendedOptimizationRules.XorRules.ComplementWithOne),
                ["Xor.ComplementLaw"] = Sweep("Xor.ComplementLaw",
                    (l, r) => new XorNode(l, r), ExtendedOptimizationRules.XorRules.ComplementLaw),

                ["Nand.IdempotentLaw"] = Sweep("Nand.IdempotentLaw",
                    (l, r) => new NandNode(l, r), ExtendedOptimizationRules.NandRules.IdempotentLaw),
                ["Nand.ZeroAbsorption"] = Sweep("Nand.ZeroAbsorption",
                    (l, r) => new NandNode(l, r), ExtendedOptimizationRules.NandRules.ZeroAbsorption),
                ["Nand.OneNeutral"] = Sweep("Nand.OneNeutral",
                    (l, r) => new NandNode(l, r), ExtendedOptimizationRules.NandRules.OneNeutral),
                ["Nand.ToBasicOperators"] = Sweep("Nand.ToBasicOperators",
                    (l, r) => new NandNode(l, r), ExtendedOptimizationRules.NandRules.ToBasicOperators),

                ["Nor.IdempotentLaw"] = Sweep("Nor.IdempotentLaw",
                    (l, r) => new NorNode(l, r), ExtendedOptimizationRules.NorRules.IdempotentLaw),
                ["Nor.OneAbsorption"] = Sweep("Nor.OneAbsorption",
                    (l, r) => new NorNode(l, r), ExtendedOptimizationRules.NorRules.OneAbsorption),
                ["Nor.ZeroNeutral"] = Sweep("Nor.ZeroNeutral",
                    (l, r) => new NorNode(l, r), ExtendedOptimizationRules.NorRules.ZeroNeutral),
                ["Nor.ToBasicOperators"] = Sweep("Nor.ToBasicOperators",
                    (l, r) => new NorNode(l, r), ExtendedOptimizationRules.NorRules.ToBasicOperators),

                ["Eqv.ReflexivityLaw"] = Sweep("Eqv.ReflexivityLaw",
                    (l, r) => new EqvNode(l, r), ExtendedOptimizationRules.EqvRules.ReflexivityLaw),
                ["Eqv.IdentityWithOne"] = Sweep("Eqv.IdentityWithOne",
                    (l, r) => new EqvNode(l, r), ExtendedOptimizationRules.EqvRules.IdentityWithOne),
                ["Eqv.IdentityWithZero"] = Sweep("Eqv.IdentityWithZero",
                    (l, r) => new EqvNode(l, r), ExtendedOptimizationRules.EqvRules.IdentityWithZero),
                ["Eqv.ComplementLaw"] = Sweep("Eqv.ComplementLaw",
                    (l, r) => new EqvNode(l, r), ExtendedOptimizationRules.EqvRules.ComplementLaw),
                ["Eqv.ToBasicOperators"] = Sweep("Eqv.ToBasicOperators",
                    (l, r) => new EqvNode(l, r), ExtendedOptimizationRules.EqvRules.ToBasicOperators)
            };

            // Guards the sweep itself: a rule that never matched contributes no evidence, so a
            // narrowed guard condition must fail here instead of silently leaving the rule unchecked.
            foreach (var (name, count) in fired)
                Assert.True(count > 0, $"{name} never fired over the operand grid — the sweep " +
                                       "proves nothing about it");
        }

        #endregion

        #region Functional Completeness Tests

        [Fact]
        public void FunctionalCompleteness_ThroughNandAndNor_ComputeNotAndOr()
        {
            // The point of a functionally complete basis is the SEMANTICS of the substitution, which
            // the shape assertions below cannot see: NAND(NAND(a,a), NAND(b,b)) and NAND(a,b) have
            // different shapes but only one of them is `a | b`. Both bases are checked against the
            // basic operators they claim to reproduce, on every assignment.
            var a = new VariableNode("a");
            var b = new VariableNode("b");

            AssertPointwiseEqual("ThroughNand.Not",
                new NotNode(a), ExtendedOptimizationRules.FunctionalCompleteness.ThroughNand.Not(a));
            AssertPointwiseEqual("ThroughNand.And",
                new AndNode(a, b), ExtendedOptimizationRules.FunctionalCompleteness.ThroughNand.And(a, b));
            AssertPointwiseEqual("ThroughNand.Or",
                new OrNode(a, b), ExtendedOptimizationRules.FunctionalCompleteness.ThroughNand.Or(a, b));

            AssertPointwiseEqual("ThroughNor.Not",
                new NotNode(a), ExtendedOptimizationRules.FunctionalCompleteness.ThroughNor.Not(a));
            AssertPointwiseEqual("ThroughNor.Or",
                new OrNode(a, b), ExtendedOptimizationRules.FunctionalCompleteness.ThroughNor.Or(a, b));
            AssertPointwiseEqual("ThroughNor.And",
                new AndNode(a, b), ExtendedOptimizationRules.FunctionalCompleteness.ThroughNor.And(a, b));
        }

        [Fact]
        public void FunctionalCompleteness_ThroughNand_Not_ShouldReturnNandWithSameOperands()
        {
            // Arrange
            var a = new VariableNode("a");

            // Act
            var result = ExtendedOptimizationRules.FunctionalCompleteness.ThroughNand.Not(a);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NandNode>(result);
            Assert.Equal(a, result.Left);
            Assert.Equal(a, result.Right);
        }

        [Fact]
        public void FunctionalCompleteness_ThroughNand_And_ShouldReturnNotNand()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");

            // Act
            var result = ExtendedOptimizationRules.FunctionalCompleteness.ThroughNand.And(a, b);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = result;
            Assert.IsType<NandNode>(notNode.Operand);
            var nandNode = (NandNode)notNode.Operand;
            Assert.Equal(a, nandNode.Left);
            Assert.Equal(b, nandNode.Right);
        }

        [Fact]
        public void FunctionalCompleteness_ThroughNand_Or_ShouldReturnNandOfNots()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");

            // Act
            var result = ExtendedOptimizationRules.FunctionalCompleteness.ThroughNand.Or(a, b);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NandNode>(result);

            // Left should be NAND(a, a)
            Assert.IsType<NandNode>(result.Left);
            var leftNand = (NandNode)result.Left;
            Assert.Equal(a, leftNand.Left);
            Assert.Equal(a, leftNand.Right);

            // Right should be NAND(b, b)
            Assert.IsType<NandNode>(result.Right);
            var rightNand = (NandNode)result.Right;
            Assert.Equal(b, rightNand.Left);
            Assert.Equal(b, rightNand.Right);
        }

        [Fact]
        public void FunctionalCompleteness_ThroughNor_Not_ShouldReturnNorWithSameOperands()
        {
            // Arrange
            var a = new VariableNode("a");

            // Act
            var result = ExtendedOptimizationRules.FunctionalCompleteness.ThroughNor.Not(a);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NorNode>(result);
            Assert.Equal(a, result.Left);
            Assert.Equal(a, result.Right);
        }

        [Fact]
        public void FunctionalCompleteness_ThroughNor_Or_ShouldReturnNotNor()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");

            // Act
            var result = ExtendedOptimizationRules.FunctionalCompleteness.ThroughNor.Or(a, b);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NotNode>(result);
            var notNode = result;
            Assert.IsType<NorNode>(notNode.Operand);
            var norNode = (NorNode)notNode.Operand;
            Assert.Equal(a, norNode.Left);
            Assert.Equal(b, norNode.Right);
        }

        [Fact]
        public void FunctionalCompleteness_ThroughNor_And_ShouldReturnNorOfNots()
        {
            // Arrange
            var a = new VariableNode("a");
            var b = new VariableNode("b");

            // Act
            var result = ExtendedOptimizationRules.FunctionalCompleteness.ThroughNor.And(a, b);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<NorNode>(result);

            // Left should be NOR(a, a)
            Assert.IsType<NorNode>(result.Left);
            var leftNor = (NorNode)result.Left;
            Assert.Equal(a, leftNor.Left);
            Assert.Equal(a, leftNor.Right);

            // Right should be NOR(b, b)
            Assert.IsType<NorNode>(result.Right);
            var rightNor = (NorNode)result.Right;
            Assert.Equal(b, rightNor.Left);
            Assert.Equal(b, rightNor.Right);
        }

        #endregion
    }
}
