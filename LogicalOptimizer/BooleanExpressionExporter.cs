using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Exporter for boolean expressions to various standard formats
/// </summary>
public static class BooleanExpressionExporter
{
    /// <summary>
    ///     Export to DIMACS format (for SAT solvers). Uses the equivalent CNF when the
    ///     distribution stays within budget; falls back to the linear-size equisatisfiable
    ///     Tseitin CNF for expressions where distribution would blow up.
    /// </summary>
    public static string ToDimacs(string expression, Dictionary<string, int>? variableMapping = null)
    {
        var ast = ParseExpression(expression);
        AstNode cnfAst;
        try
        {
            cnfAst = new NormalFormConverter().ConvertToCNF(ast);
        }
        catch (NormalFormTooLargeException)
        {
            var tseitin = TseitinConverter.Convert(ast);
            return $"c Boolean expression: {expression}\nc Equisatisfiable Tseitin CNF\n{tseitin.ToDimacs()}";
        }

        // Get variables and create mapping
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        var varMap = variableMapping ?? variables.Select((v, i) => new { Var = v, Index = i + 1 })
            .ToDictionary(x => x.Var, x => x.Index);

        var clauses = new List<string>();
        ExtractClauses(cnfAst, varMap, clauses);

        var sb = new StringBuilder();
        sb.AppendLine($"c Boolean expression: {expression}");
        sb.AppendLine($"c Variables: {string.Join(", ", variables)}");
        sb.AppendLine($"p cnf {variables.Count} {clauses.Count}");

        foreach (var clause in clauses) sb.AppendLine($"{clause} 0");

        return sb.ToString();
    }

    private static void ExtractClauses(AstNode node, Dictionary<string, int> varMap, List<string> clauses)
    {
        if (node is AndNode andNode)
        {
            // CNF: conjunction of disjunctions
            foreach (var operand in andNode.Operands)
                ExtractClauses(operand, varMap, clauses);
        }
        else
        {
            // This should be a disjunction or literal
            var literals = new List<string>();
            var isTautology = CollectLiterals(node, varMap, literals);
            // A clause containing constant 1 is always satisfied and can be dropped;
            // an empty clause (constant 0) stays: it marks the formula unsatisfiable
            if (!isTautology)
                clauses.Add(string.Join(" ", literals));
        }
    }

    /// <summary>Collect DIMACS literals of one clause; returns true if the clause is a tautology.</summary>
    private static bool CollectLiterals(AstNode node, Dictionary<string, int> varMap, List<string> literals)
    {
        switch (node)
        {
            case OrNode orNode:
                {
                    // Non-short-circuit: every operand must contribute its literals
                    var isTautology = false;
                    foreach (var operand in orNode.Operands)
                        isTautology |= CollectLiterals(operand, varMap, literals);
                    return isTautology;
                }

            case NotNode { Operand: ConstantNode negatedConstant }:
                return !negatedConstant.Value; // !0 = 1 makes the clause true; !1 contributes nothing

            case NotNode { Operand: VariableNode negated }:
                literals.Add($"-{varMap[negated.Name]}");
                return false;

            case ConstantNode constant:
                return constant.Value; // 1 makes the clause true; 0 contributes nothing

            case VariableNode varNode:
                literals.Add(varMap[varNode.Name].ToString());
                return false;

            default:
                throw new NotSupportedException($"Unexpected node in CNF clause: {node.GetType()}");
        }
    }

    private static AstNode ParseExpression(string expression)
    {
        var tokens = new Lexer(expression).Tokenize();
        return new Parser(tokens).Parse();
    }

    /// <summary>
    ///     Export to BLIF format (Berkeley Logic Interchange Format)
    /// </summary>
    public static string ToBlif(string expression, string? modelName = null)
    {
        var ast = ParseExpression(expression);
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        var sb = new StringBuilder();

        sb.AppendLine($".model {modelName ?? "boolean_expr"}");
        sb.AppendLine($".inputs {string.Join(" ", variables)}");
        sb.AppendLine(".outputs out");
        sb.AppendLine();

        var gateCounter = 0;
        var outputGate = ConvertToBlifGates(ast, sb, ref gateCounter);

        sb.AppendLine($".names {outputGate} out");
        sb.AppendLine("1 1");
        sb.AppendLine(".end");

        return sb.ToString();
    }

    private static string ConvertToBlifGates(AstNode node, StringBuilder sb, ref int gateCounter)
    {
        switch (node)
        {
            case VariableNode varNode:
                return varNode.Name;

            case NotNode notNode:
                var inputGate = ConvertToBlifGates(notNode.Operand, sb, ref gateCounter);
                var notGate = $"n{gateCounter++}";
                sb.AppendLine($".names {inputGate} {notGate}");
                sb.AppendLine("0 1");
                return notGate;

            case AndNode andNode:
                {
                    // N-ary AND maps to a single multi-input BLIF gate
                    var inputGates = new List<string>(andNode.Operands.Count);
                    foreach (var operand in andNode.Operands)
                        inputGates.Add(ConvertToBlifGates(operand, sb, ref gateCounter));
                    var andGate = $"a{gateCounter++}";
                    sb.AppendLine($".names {string.Join(" ", inputGates)} {andGate}");
                    sb.AppendLine(new string('1', inputGates.Count) + " 1");
                    return andGate;
                }

            case OrNode orNode:
                {
                    // N-ary OR: one cover row per input
                    var inputGates = new List<string>(orNode.Operands.Count);
                    foreach (var operand in orNode.Operands)
                        inputGates.Add(ConvertToBlifGates(operand, sb, ref gateCounter));
                    var orGate = $"o{gateCounter++}";
                    sb.AppendLine($".names {string.Join(" ", inputGates)} {orGate}");
                    for (var i = 0; i < inputGates.Count; i++)
                    {
                        var row = new string('-', inputGates.Count).ToCharArray();
                        row[i] = '1';
                        sb.AppendLine(new string(row) + " 1");
                    }

                    return orGate;
                }

            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
    }

    /// <summary>
    ///     Export to Verilog HDL format
    /// </summary>
    public static string ToVerilog(string expression, string? moduleName = null)
    {
        var ast = ParseExpression(expression);
        var variables = ast.GetVariables().OrderBy(v => v).ToList();
        var sb = new StringBuilder();

        var module = moduleName ?? "boolean_expr";
        sb.AppendLine($"module {module}(");
        sb.AppendLine($"    input {string.Join(", ", variables)},");
        sb.AppendLine("    output out");
        sb.AppendLine(");");
        sb.AppendLine();

        var gateCounter = 0;
        var assignments = new List<string>();
        var outputWire = ConvertToVerilogLogic(ast, assignments, ref gateCounter);

        foreach (var assignment in assignments) sb.AppendLine($"    {assignment}");

        sb.AppendLine($"    assign out = {outputWire};");
        sb.AppendLine();
        sb.AppendLine("endmodule");

        return sb.ToString();
    }

    private static string ConvertToVerilogLogic(AstNode node, List<string> assignments, ref int gateCounter)
    {
        switch (node)
        {
            case ConstantNode constant:
                return constant.Value ? "1'b1" : "1'b0";

            case VariableNode varNode:
                return varNode.Name;

            case NotNode notNode:
                var inputWire = ConvertToVerilogLogic(notNode.Operand, assignments, ref gateCounter);
                var notWire = $"w{gateCounter++}";
                assignments.Add($"wire {notWire};");
                assignments.Add($"assign {notWire} = ~{inputWire};");
                return notWire;

            case AndNode andNode:
                {
                    // N-ary AND becomes a single multi-input assignment
                    var inputWires = new List<string>(andNode.Operands.Count);
                    foreach (var operand in andNode.Operands)
                        inputWires.Add(ConvertToVerilogLogic(operand, assignments, ref gateCounter));
                    var andWire = $"w{gateCounter++}";
                    assignments.Add($"wire {andWire};");
                    assignments.Add($"assign {andWire} = {string.Join(" & ", inputWires)};");
                    return andWire;
                }

            case OrNode orNode:
                {
                    var inputWires = new List<string>(orNode.Operands.Count);
                    foreach (var operand in orNode.Operands)
                        inputWires.Add(ConvertToVerilogLogic(operand, assignments, ref gateCounter));
                    var orWire = $"w{gateCounter++}";
                    assignments.Add($"wire {orWire};");
                    assignments.Add($"assign {orWire} = {string.Join(" | ", inputWires)};");
                    return orWire;
                }

            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
    }

    /// <summary>
    ///     Export to logical equations format (mathematical notation)
    /// </summary>
    public static string ToMathematicalNotation(string expression)
    {
        return MapOperators(AstFormatter.Format(ParseExpression(expression)), "¬", "∧", "∨");
    }

    /// <summary>
    ///     Parenthesization is <see cref="AstFormatter" />'s job; text notations differ
    ///     only in operator tokens, which are substituted afterwards (variable names
    ///     cannot contain the operator characters).
    /// </summary>
    private static string MapOperators(string formatted, string notOp, string andOp, string orOp)
    {
        return formatted.Replace("!", notOp).Replace("&", andOp).Replace("|", orOp);
    }

    /// <summary>
    ///     Export truth table to CSV format
    /// </summary>
    public static string TruthTableToCsv(string expression)
    {
        var truthTable = TruthTable.Generate(expression);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine(string.Join(",", truthTable.Variables.Concat(new[] { "Result" })));

        // Data
        for (var i = 0; i < truthTable.Rows.Count; i++)
        {
            var row = truthTable.Rows[i];
            var values = truthTable.Variables.Select(v => row[v] ? "1" : "0")
                .Concat(new[] { truthTable.Results[i] ? "1" : "0" });
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Export to LaTeX format for mathematical typesetting
    /// </summary>
    public static string ToLatex(string expression)
    {
        return MapOperators(AstFormatter.Format(ParseExpression(expression)), "\\neg ", "\\land", "\\lor");
    }
}
