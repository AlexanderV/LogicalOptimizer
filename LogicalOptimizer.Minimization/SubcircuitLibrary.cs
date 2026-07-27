namespace LogicalOptimizer;

/// <summary>
///     Precomputed optimal small subcircuits (the Simplifier-2025 idea at expression
///     level): every one of the 256 three-variable functions gets a provably minimal
///     implementation, built once with the exact minimizer and cached. A bottom-up pass
///     then replaces any AND/OR/NOT subtree over ≤3 distinct variables whenever the
///     library entry is strictly cheaper — local exact optimality even where the global
///     search is out of reach. Subtrees containing extended operators (XOR/IMP/...) are
///     traversed but never table-rewritten: collapsing them to &amp;/|/! would destroy the
///     display forms users asked for.
/// </summary>
internal static class SubcircuitLibrary
{
    private static readonly string[] Slots = { "x0", "x1", "x2" };

    private static readonly Lazy<AstNode[]> Table = new(BuildTable, true);

    /// <summary>Minimal implementations of all 256 functions over (x0, x1, x2).</summary>
    private static AstNode[] BuildTable()
    {
        var table = new AstNode[256];
        var variables = Slots.ToList();
        for (var bits = 0; bits < 256; bits++)
        {
            var onSet = new HashSet<int>();
            for (var m = 0; m < 8; m++)
                if ((bits & (1 << m)) != 0)
                    onSet.Add(m);

            var (sop, _) = TruthTableMinimizer.MinimalSopWithStatus(variables, onSet,
                coverStepLimit: PerformanceValidator.GUARANTEE_COVER_STEP_LIMIT);
            var pos = TruthTableMinimizer.MinimalPos(variables, onSet);

            table[bits] = Cost(pos).CompareTo(Cost(sop)) < 0 ? pos : sop;
        }

        return table;
    }

    private static (int Literals, int Nodes) Cost(AstNode node)
    {
        return (AstMetrics.CountLiterals(node), AstMetrics.CountNodes(node));
    }

    /// <summary>
    ///     Provably minimal equivalent for a subtree with ≤3 distinct variables. False
    ///     when the subtree has more variables or contains non-core operators.
    /// </summary>
    public static bool TryRewrite(AstNode subtree, out AstNode minimal)
    {
        minimal = subtree;
        if (!IsCore(subtree)) return false;

        var variables = subtree.GetVariables().OrderBy(v => v).ToList();
        if (variables.Count > 3) return false;

        var bits = 0;
        var assignment = new Dictionary<string, bool>();
        for (var m = 0; m < 8; m++)
        {
            for (var j = 0; j < Slots.Length; j++)
                if (j < variables.Count)
                    assignment[variables[j]] = (m & (1 << j)) != 0;
            if (TruthTable.Evaluate(subtree, assignment)) bits |= 1 << m;
        }

        var template = Table.Value[bits];
        var map = new Dictionary<string, string>();
        for (var j = 0; j < variables.Count; j++) map[Slots[j]] = variables[j];
        // Unused slots stay as-is; minimal implementations of ≤2-variable functions
        // never mention the free slot, so the map is total in practice
        minimal = Instantiate(template, map);
        return true;
    }

    /// <summary>
    ///     Bottom-up local rewriting: children first, then this subtree if the library
    ///     entry is strictly cheaper (literals, then nodes). Sound by construction —
    ///     every replacement is an exact-minimizer result for the subtree's own function.
    /// </summary>
    public static AstNode RewriteSubcircuits(AstNode root)
    {
        var rewritten = root switch
        {
            NotNode not => new NotNode(RewriteSubcircuits(not.Operand)),
            AndNode and => new AndNode(RewriteOperands(and.Operands)),
            OrNode or => new OrNode(RewriteOperands(or.Operands)),
            XorNode xor => new XorNode(RewriteSubcircuits(xor.Left), RewriteSubcircuits(xor.Right)),
            EqvNode eqv => new EqvNode(RewriteSubcircuits(eqv.Left), RewriteSubcircuits(eqv.Right)),
            ImpNode imp => new ImpNode(RewriteSubcircuits(imp.Left), RewriteSubcircuits(imp.Right)),
            NandNode nand => new NandNode(RewriteSubcircuits(nand.Left), RewriteSubcircuits(nand.Right)),
            NorNode nor => new NorNode(RewriteSubcircuits(nor.Left), RewriteSubcircuits(nor.Right)),
            _ => root
        };

        if (TryRewrite(rewritten, out var minimal))
            return Cost(minimal).CompareTo(Cost(rewritten)) < 0 ? minimal : rewritten;

        // Too many variables for a whole-node rewrite. The v2 n-ary AST flattens what
        // used to be nested binary chains, so a small subcircuit may now live as a
        // GROUP of operands inside a wider connective (e.g. the 3-variable consensus
        // core of "x&y | !x&z | y&z | p&q") — collapse such groups locally.
        return rewritten is NaryNode nary ? RewriteOperandGroups(nary) : rewritten;
    }

    /// <summary>
    ///     Greedily groups core operands of an n-ary node by combined variable support
    ///     (≤3 variables per group) and replaces every multi-operand group whose library
    ///     entry is strictly cheaper. Operand order may change; semantics cannot (AND/OR
    ///     are associative and commutative, and every replacement is exact).
    /// </summary>
    private static AstNode RewriteOperandGroups(NaryNode node)
    {
        var groupVariables = new List<HashSet<string>>();
        var groupOperands = new List<List<AstNode>>();
        var ungrouped = new List<AstNode>();

        foreach (var operand in node.Operands)
        {
            var variables = operand.GetVariables();
            if (!IsCore(operand) || variables.Count > 3)
            {
                ungrouped.Add(operand);
                continue;
            }

            var target = -1;
            for (var g = 0; g < groupVariables.Count && target < 0; g++)
                if (variables.Count(v => !groupVariables[g].Contains(v)) + groupVariables[g].Count <= 3)
                    target = g;

            if (target < 0)
            {
                groupVariables.Add(new HashSet<string>(variables));
                groupOperands.Add(new List<AstNode> { operand });
            }
            else
            {
                groupVariables[target].UnionWith(variables);
                groupOperands[target].Add(operand);
            }
        }

        var changed = false;
        var result = new List<AstNode>(ungrouped);
        foreach (var group in groupOperands)
        {
            if (group.Count < 2)
            {
                result.AddRange(group);
                continue;
            }

            AstNode subcircuit = node is AndNode ? new AndNode(group) : new OrNode(group);
            if (TryRewrite(subcircuit, out var groupMinimal) &&
                Cost(groupMinimal).CompareTo(Cost(subcircuit)) < 0)
            {
                result.Add(groupMinimal);
                changed = true;
            }
            else
            {
                result.AddRange(group);
            }
        }

        if (!changed) return node;
        if (result.Count == 1) return result[0];
        return node is AndNode ? new AndNode(result) : new OrNode(result);
    }

    private static AstNode[] RewriteOperands(IReadOnlyList<AstNode> operands)
    {
        var rewritten = new AstNode[operands.Count];
        for (var i = 0; i < operands.Count; i++)
            rewritten[i] = RewriteSubcircuits(operands[i]);
        return rewritten;
    }

    private static bool IsCore(AstNode node)
    {
        return node switch
        {
            VariableNode or ConstantNode => true,
            NotNode not => IsCore(not.Operand),
            NaryNode nary => nary.Operands.All(IsCore),
            _ => false
        };
    }

    private static AstNode Instantiate(AstNode template, IReadOnlyDictionary<string, string> map)
    {
        return template switch
        {
            VariableNode variable => new VariableNode(map.GetValueOrDefault(variable.Name, variable.Name)),
            ConstantNode constant => constant,
            NotNode not => new NotNode(Instantiate(not.Operand, map)),
            AndNode and => new AndNode(InstantiateOperands(and.Operands, map)),
            OrNode or => new OrNode(InstantiateOperands(or.Operands, map)),
            _ => throw new InvalidOperationException($"Unexpected template node {template.GetType().Name}")
        };
    }

    private static AstNode[] InstantiateOperands(IReadOnlyList<AstNode> operands,
        IReadOnlyDictionary<string, string> map)
    {
        var instantiated = new AstNode[operands.Count];
        for (var i = 0; i < operands.Count; i++)
            instantiated[i] = Instantiate(operands[i], map);
        return instantiated;
    }
}
