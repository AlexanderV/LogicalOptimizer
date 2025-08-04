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
            Assert.IsType<VariableNode>(result);
            Assert.Equal("0", ((VariableNode)result).Name);
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
            var zero = new VariableNode("0");
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
            var zero = new VariableNode("0");
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
            var one = new VariableNode("1");
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
            var one = new VariableNode("1");
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
            Assert.IsType<VariableNode>(result);
            Assert.Equal("1", ((VariableNode)result).Name);
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
            Assert.IsType<VariableNode>(result);
            Assert.Equal("1", ((VariableNode)result).Name);
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
            var zero = new VariableNode("0");
            var a = new VariableNode("a");
            var nandNode = new NandNode(zero, a);

            // Act
            var result = ExtendedOptimizationRules.NandRules.ZeroAbsorption(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<VariableNode>(result);
            Assert.Equal("1", ((VariableNode)result).Name);
        }

        [Fact]
        public void NandRules_ZeroAbsorption_RightZero_ShouldReturnOne()
        {
            // Arrange
            var a = new VariableNode("a");
            var zero = new VariableNode("0");
            var nandNode = new NandNode(a, zero);

            // Act
            var result = ExtendedOptimizationRules.NandRules.ZeroAbsorption(nandNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<VariableNode>(result);
            Assert.Equal("1", ((VariableNode)result).Name);
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
            var one = new VariableNode("1");
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
            var one = new VariableNode("1");
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
            Assert.Equal(a, andNode.Left);
            Assert.Equal(b, andNode.Right);
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
            var one = new VariableNode("1");
            var a = new VariableNode("a");
            var norNode = new NorNode(one, a);

            // Act
            var result = ExtendedOptimizationRules.NorRules.OneAbsorption(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<VariableNode>(result);
            Assert.Equal("0", ((VariableNode)result).Name);
        }

        [Fact]
        public void NorRules_OneAbsorption_RightOne_ShouldReturnZero()
        {
            // Arrange
            var a = new VariableNode("a");
            var one = new VariableNode("1");
            var norNode = new NorNode(a, one);

            // Act
            var result = ExtendedOptimizationRules.NorRules.OneAbsorption(norNode);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<VariableNode>(result);
            Assert.Equal("0", ((VariableNode)result).Name);
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
            var zero = new VariableNode("0");
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
            var zero = new VariableNode("0");
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
            Assert.Equal(a, orNode.Left);
            Assert.Equal(b, orNode.Right);
        }

        #endregion

        #region Functional Completeness Tests

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
