using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Tseitin transformation: converts any expression into an equisatisfiable CNF whose
///     size is linear in the AST size. Introduces one auxiliary variable per logic gate
///     (named _t1, _t2, ...); the result is satisfiable exactly when the input is, and every
///     satisfying assignment restricted to the input variables satisfies the input formula.
/// </summary>
public static class TseitinConverter
{
    public static TseitinCnf Convert(string expression)
    {
        return Convert(new Parser(new Lexer(expression).Tokenize()).Parse());
    }

    public static TseitinCnf Convert(AstNode ast)
    {
        return Convert(ast, CnfEncodingStyle.Tseitin);
    }

    /// <summary>
    ///     Convert with a chosen gate-encoding style. Plaisted–Greenbaum emits only the
    ///     implication direction each gate's polarity requires (up to 2x fewer clauses),
    ///     preserving both equisatisfiability and the model-projection property; use
    ///     Tseitin when downstream code flips gate literals arbitrarily.
    /// </summary>
    public static TseitinCnf Convert(AstNode ast, CnfEncodingStyle style)
    {
        var builder = new Builder(ast, style);
        return builder.Build();
    }

    private sealed class Builder
    {
        [Flags]
        private enum Polarity
        {
            None = 0,
            Positive = 1,
            Negative = 2,
            Both = Positive | Negative
        }

        private readonly AstNode _root;
        private readonly CnfEncodingStyle _style;
        private readonly List<string> _inputVariables;
        private readonly Dictionary<string, int> _variableIndex = new();
        private readonly Dictionary<AstNode, int> _nodeLiteral = new();
        private readonly Dictionary<AstNode, Polarity> _emitted = new();
        private readonly List<int[]> _clauses = new();
        private int _nextIndex;
        private int _auxCount;
        private int _constantTrueLiteral;

        public Builder(AstNode root, CnfEncodingStyle style)
        {
            _root = root;
            _style = style;
            _inputVariables = root.GetVariables().OrderBy(v => v).ToList();
            foreach (var variable in _inputVariables)
                _variableIndex[variable] = ++_nextIndex;
        }

        public TseitinCnf Build()
        {
            var rootLiteral = Encode(_root, Polarity.Positive);
            _clauses.Add(new[] { rootLiteral });
            return new TseitinCnf(_inputVariables, _auxCount, _clauses);
        }

        /// <summary>
        ///     Encode a node occurring with the given polarity. Tseitin ignores polarity
        ///     (always Both); Plaisted–Greenbaum emits only the needed direction, adding
        ///     the other one later if the same shared subtree reappears with it. Derived
        ///     operators delegate to synthetic AND/OR/XOR gates — structural equality of
        ///     AST nodes makes the gate cache deduplicate them.
        /// </summary>
        private int Encode(AstNode node, Polarity polarity)
        {
            if (_style == CnfEncodingStyle.Tseitin) polarity = Polarity.Both;

            return node switch
            {
                VariableNode variable => _variableIndex[variable.Name],
                ConstantNode constant => constant.Value ? ConstantTrue() : -ConstantTrue(),
                NotNode not => -Encode(not.Operand, Flip(polarity)),
                NandNode nand => -EncodeGate(new AndNode(nand.Left, nand.Right), Flip(polarity)),
                NorNode nor => -EncodeGate(new OrNode(nor.Left, nor.Right), Flip(polarity)),
                EqvNode eqv => -EncodeGate(new XorNode(eqv.Left, eqv.Right), Flip(polarity)),
                ImpNode imp => EncodeGate(new OrNode(new NotNode(imp.Left), imp.Right), polarity),
                AndNode or OrNode or XorNode => EncodeGate(node, polarity),
                _ => throw new NotSupportedException($"Unsupported node type: {node.GetType()}")
            };
        }

        private int EncodeGate(AstNode node, Polarity polarity)
        {
            if (_nodeLiteral.TryGetValue(node, out var cached))
            {
                var missing = polarity & ~_emitted.GetValueOrDefault(node, Polarity.None);
                if (missing != Polarity.None) EmitGate(node, cached, missing);
                return cached;
            }

            var gate = NewAuxVariable();
            _nodeLiteral[node] = gate;
            _emitted[node] = Polarity.None;
            EmitGate(node, gate, polarity);
            return gate;
        }

        private void EmitGate(AstNode node, int gate, Polarity polarity)
        {
            switch (node)
            {
                case AndNode and:
                    {
                        // XOR children need both polarities; AND/OR pass their own through
                        var a = Encode(and.Left, polarity);
                        var b = Encode(and.Right, polarity);
                        if ((polarity & Polarity.Positive) != 0)
                        {
                            // g -> a & b
                            _clauses.Add(new[] { -gate, a });
                            _clauses.Add(new[] { -gate, b });
                        }

                        if ((polarity & Polarity.Negative) != 0)
                            // a & b -> g
                            _clauses.Add(new[] { gate, -a, -b });
                        break;
                    }
                case OrNode or:
                    {
                        var a = Encode(or.Left, polarity);
                        var b = Encode(or.Right, polarity);
                        if ((polarity & Polarity.Positive) != 0)
                            // g -> a | b
                            _clauses.Add(new[] { -gate, a, b });
                        if ((polarity & Polarity.Negative) != 0)
                        {
                            // a | b -> g
                            _clauses.Add(new[] { gate, -a });
                            _clauses.Add(new[] { gate, -b });
                        }

                        break;
                    }
                case XorNode xor:
                    {
                        // Each child feeds clauses of both signs — always needs Both
                        var a = Encode(xor.Left, Polarity.Both);
                        var b = Encode(xor.Right, Polarity.Both);
                        if ((polarity & Polarity.Positive) != 0)
                        {
                            _clauses.Add(new[] { -gate, -a, -b });
                            _clauses.Add(new[] { -gate, a, b });
                        }

                        if ((polarity & Polarity.Negative) != 0)
                        {
                            _clauses.Add(new[] { gate, a, -b });
                            _clauses.Add(new[] { gate, -a, b });
                        }

                        break;
                    }
                default:
                    throw new NotSupportedException($"Unsupported gate type: {node.GetType()}");
            }

            _emitted[node] = _emitted.GetValueOrDefault(node, Polarity.None) | polarity;
        }

        private static Polarity Flip(Polarity polarity)
        {
            var flipped = Polarity.None;
            if ((polarity & Polarity.Positive) != 0) flipped |= Polarity.Negative;
            if ((polarity & Polarity.Negative) != 0) flipped |= Polarity.Positive;
            return flipped;
        }

        /// <summary>Auxiliary variable pinned to true; constants become ±literal of it.</summary>
        private int ConstantTrue()
        {
            if (_constantTrueLiteral == 0)
            {
                _constantTrueLiteral = NewAuxVariable();
                _clauses.Add(new[] { _constantTrueLiteral });
            }

            return _constantTrueLiteral;
        }

        private int NewAuxVariable()
        {
            _auxCount++;
            return ++_nextIndex;
        }
    }
}

/// <summary>Gate-encoding style for <see cref="TseitinConverter" />.</summary>
public enum CnfEncodingStyle
{
    /// <summary>Full biconditional per gate: model-complete, safe for any downstream use.</summary>
    Tseitin,

    /// <summary>
    ///     Plaisted–Greenbaum polarity encoding: only the implication direction each gate's
    ///     occurrence polarity requires. Equisatisfiable, and models still project onto the
    ///     inputs correctly; gate variables merely lose their "definition" reading.
    /// </summary>
    PlaistedGreenbaum
}

/// <summary>
///     Equisatisfiable CNF produced by <see cref="TseitinConverter" />. Variable indices are
///     1-based DIMACS style: input variables first (sorted by name), auxiliary gate variables after.
/// </summary>
public sealed class TseitinCnf
{
    internal TseitinCnf(List<string> inputVariables, int auxiliaryVariableCount, List<int[]> clauses)
    {
        InputVariables = inputVariables;
        AuxiliaryVariableCount = auxiliaryVariableCount;
        Clauses = clauses;
    }

    public IReadOnlyList<string> InputVariables { get; }
    public int AuxiliaryVariableCount { get; }
    public int TotalVariableCount => InputVariables.Count + AuxiliaryVariableCount;
    public IReadOnlyList<int[]> Clauses { get; }

    /// <summary>Name of a 1-based DIMACS variable index: input name or _tN for auxiliaries.</summary>
    public string VariableName(int index)
    {
        if (index < 1 || index > TotalVariableCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index <= InputVariables.Count
            ? InputVariables[index - 1]
            : $"_t{index - InputVariables.Count}";
    }

    public string ToDimacs()
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= InputVariables.Count; i++)
            sb.AppendLine($"c {i} = {InputVariables[i - 1]}");
        if (AuxiliaryVariableCount > 0)
            sb.AppendLine($"c {InputVariables.Count + 1}..{TotalVariableCount} = Tseitin auxiliary variables");
        sb.AppendLine($"p cnf {TotalVariableCount} {Clauses.Count}");
        foreach (var clause in Clauses)
            sb.AppendLine(string.Join(" ", clause) + " 0");
        return sb.ToString();
    }

    /// <summary>CNF as an AST over input and auxiliary (_tN) variables.</summary>
    public AstNode ToAst()
    {
        AstNode? conjunction = null;
        foreach (var clause in Clauses)
        {
            AstNode? disjunction = null;
            foreach (var literal in clause)
            {
                AstNode node = new VariableNode(VariableName(Math.Abs(literal)));
                if (literal < 0) node = new NotNode(node);
                disjunction = disjunction == null ? node : new OrNode(disjunction, node);
            }

            conjunction = conjunction == null ? disjunction : new AndNode(conjunction, disjunction!);
        }

        return conjunction ?? ConstantNode.True;
    }

    public override string ToString()
    {
        return ToAst().ToString();
    }
}
