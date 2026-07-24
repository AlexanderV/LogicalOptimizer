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
        catch (InvalidOperationException)
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
            ExtractClauses(andNode.Left, varMap, clauses);
            ExtractClauses(andNode.Right, varMap, clauses);
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
                // Non-short-circuit: both sides must contribute their literals
                return CollectLiterals(orNode.Left, varMap, literals) |
                       CollectLiterals(orNode.Right, varMap, literals);

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
                var leftGate = ConvertToBlifGates(andNode.Left, sb, ref gateCounter);
                var rightGate = ConvertToBlifGates(andNode.Right, sb, ref gateCounter);
                var andGate = $"a{gateCounter++}";
                sb.AppendLine($".names {leftGate} {rightGate} {andGate}");
                sb.AppendLine("11 1");
                return andGate;

            case OrNode orNode:
                var leftOrGate = ConvertToBlifGates(orNode.Left, sb, ref gateCounter);
                var rightOrGate = ConvertToBlifGates(orNode.Right, sb, ref gateCounter);
                var orGate = $"o{gateCounter++}";
                sb.AppendLine($".names {leftOrGate} {rightOrGate} {orGate}");
                sb.AppendLine("1- 1");
                sb.AppendLine("-1 1");
                return orGate;

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
                var leftWire = ConvertToVerilogLogic(andNode.Left, assignments, ref gateCounter);
                var rightWire = ConvertToVerilogLogic(andNode.Right, assignments, ref gateCounter);
                var andWire = $"w{gateCounter++}";
                assignments.Add($"wire {andWire};");
                assignments.Add($"assign {andWire} = {leftWire} & {rightWire};");
                return andWire;

            case OrNode orNode:
                var leftOrWire = ConvertToVerilogLogic(orNode.Left, assignments, ref gateCounter);
                var rightOrWire = ConvertToVerilogLogic(orNode.Right, assignments, ref gateCounter);
                var orWire = $"w{gateCounter++}";
                assignments.Add($"wire {orWire};");
                assignments.Add($"assign {orWire} = {leftOrWire} | {rightOrWire};");
                return orWire;

            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
    }

    /// <summary>
    ///     Export to logical equations format (mathematical notation)
    /// </summary>
    public static string ToMathematicalNotation(string expression)
    {
        return FormatNode(ParseExpression(expression), "¬", "∧", "∨");
    }

    /// <summary>
    ///     Shared tree walk for text notations that differ only in operator tokens
    /// </summary>
    private static string FormatNode(AstNode node, string notOp, string andOp, string orOp)
    {
        switch (node)
        {
            case ConstantNode constant:
                return constant.Value ? "1" : "0";

            case VariableNode varNode:
                return varNode.Name;

            case NotNode notNode:
                var operand = FormatNode(notNode.Operand, notOp, andOp, orOp);
                return notNode.Operand is VariableNode ? $"{notOp}{operand}" : $"{notOp}({operand})";

            case AndNode andNode:
                var left = FormatNode(andNode.Left, notOp, andOp, orOp);
                var right = FormatNode(andNode.Right, notOp, andOp, orOp);

                if (andNode.Left is OrNode)
                    left = $"({left})";
                if (andNode.Right is OrNode)
                    right = $"({right})";

                return $"{left} {andOp} {right}";

            case OrNode orNode:
                var leftOr = FormatNode(orNode.Left, notOp, andOp, orOp);
                var rightOr = FormatNode(orNode.Right, notOp, andOp, orOp);

                var result = $"{leftOr} {orOp} {rightOr}";

                if (orNode.ForceParentheses)
                    result = $"({result})";

                return result;

            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
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
        return FormatNode(ParseExpression(expression), "\\neg ", "\\land", "\\lor");
    }
}
