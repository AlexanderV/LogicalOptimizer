using System.Text;

namespace LogicalOptimizer;

/// <summary>
///     Exporter for boolean expressions to various standard formats
/// </summary>
public static class BooleanExpressionExporter
{
    /// <summary>
    ///     Export to DIMACS format (for SAT solvers)
    /// </summary>
    public static string ToDimacs(string expression, Dictionary<string, int>? variableMapping = null)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Convert to CNF using optimizer
        var optimizer = new BooleanExpressionOptimizer();
        var result = optimizer.OptimizeExpression(expression);
        var cnfExpression = result.CNF;

        // Parse CNF to get structure
        var cnfTokens = new Lexer(cnfExpression).Tokenize();
        var cnfParser = new Parser(cnfTokens);
        var cnfAst = cnfParser.Parse();

        // Get variables and create mapping
        var variables = ast.GetVariables().Where(v => v != "0" && v != "1").OrderBy(v => v).ToList();
        var varMap = variableMapping ?? variables.Select((v, i) => new {Var = v, Index = i + 1})
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
            ExtractLiterals(node, varMap, literals);
            clauses.Add(string.Join(" ", literals));
        }
    }

    private static void ExtractLiterals(AstNode node, Dictionary<string, int> varMap, List<string> literals)
    {
        switch (node)
        {
            case OrNode orNode:
                ExtractLiterals(orNode.Left, varMap, literals);
                ExtractLiterals(orNode.Right, varMap, literals);
                break;

            case NotNode notNode when notNode.Operand is VariableNode varNode:
                if (varMap.TryGetValue(varNode.Name, out var varIndex))
                    literals.Add($"-{varIndex}");
                break;

            case VariableNode varNode:
                if (varNode.Name == "1")
                {
                    // Tautology - add all variables to disjunction
                    literals.AddRange(varMap.Values.Select(v => v.ToString()));
                    literals.AddRange(varMap.Values.Select(v => $"-{v}"));
                }
                else if (varNode.Name == "0")
                {
                    // Contradiction - empty clause (unsatisfiable)
                    literals.Clear();
                }
                else if (varMap.TryGetValue(varNode.Name, out var varIdx))
                {
                    literals.Add(varIdx.ToString());
                }

                break;
        }
    }

    /// <summary>
    ///     Export to BLIF format (Berkeley Logic Interchange Format)
    /// </summary>
    public static string ToBlif(string expression, string? modelName = null)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        var variables = ast.GetVariables().Where(v => v != "0" && v != "1").OrderBy(v => v).ToList();
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
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        var variables = ast.GetVariables().Where(v => v != "0" && v != "1").OrderBy(v => v).ToList();
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
            case VariableNode varNode:
                return varNode.Name == "1" ? "1'b1" : varNode.Name == "0" ? "1'b0" : varNode.Name;

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
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        return ConvertToMathNotation(ast);
    }

    private static string ConvertToMathNotation(AstNode node)
    {
        switch (node)
        {
            case VariableNode varNode:
                return varNode.Name;

            case NotNode notNode:
                var operand = ConvertToMathNotation(notNode.Operand);
                return notNode.Operand is VariableNode ? $"¬{operand}" : $"¬({operand})";

            case AndNode andNode:
                var left = ConvertToMathNotation(andNode.Left);
                var right = ConvertToMathNotation(andNode.Right);

                if (andNode.Left is OrNode)
                    left = $"({left})";
                if (andNode.Right is OrNode)
                    right = $"({right})";

                return $"{left} ∧ {right}";

            case OrNode orNode:
                var leftOr = ConvertToMathNotation(orNode.Left);
                var rightOr = ConvertToMathNotation(orNode.Right);

                var result = $"{leftOr} ∨ {rightOr}";

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
        sb.AppendLine(string.Join(",", truthTable.Variables.Concat(new[] {"Result"})));

        // Data
        for (var i = 0; i < truthTable.Rows.Count; i++)
        {
            var row = truthTable.Rows[i];
            var values = truthTable.Variables.Select(v => row[v] ? "1" : "0")
                .Concat(new[] {truthTable.Results[i] ? "1" : "0"});
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Export to LaTeX format for mathematical typesetting
    /// </summary>
    public static string ToLatex(string expression)
    {
        var lexer = new Lexer(expression);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        return ConvertToLatex(ast);
    }

    private static string ConvertToLatex(AstNode node)
    {
        switch (node)
        {
            case VariableNode varNode:
                return varNode.Name;

            case NotNode notNode:
                var operand = ConvertToLatex(notNode.Operand);
                return notNode.Operand is VariableNode ? $"\\neg {operand}" : $"\\neg ({operand})";

            case AndNode andNode:
                var left = ConvertToLatex(andNode.Left);
                var right = ConvertToLatex(andNode.Right);

                if (andNode.Left is OrNode)
                    left = $"({left})";
                if (andNode.Right is OrNode)
                    right = $"({right})";

                return $"{left} \\land {right}";

            case OrNode orNode:
                var leftOr = ConvertToLatex(orNode.Left);
                var rightOr = ConvertToLatex(orNode.Right);

                var result = $"{leftOr} \\lor {rightOr}";

                if (orNode.ForceParentheses)
                    result = $"({result})";

                return result;

            default:
                throw new NotSupportedException($"Unsupported node type: {node.GetType()}");
        }
    }
}