using System.Numerics;

namespace LogicalOptimizer;

/// <summary>
///     Converts a boolean expression to its Algebraic Normal Form (ANF, a.k.a. the
///     Zhegalkin / Reed–Muller polynomial): a XOR of AND-monomials over the variables,
///     which is the unique canonical representation over GF(2).
///     <para>
///         The monomial coefficients are obtained by the fast Möbius transform over the
///         function's truth table (2^n work, capped at <see cref="TruthTable.MaxVariables" />
///         like every other truth-table consumer). The transform is its own inverse over
///         GF(2), so applying it again to the coefficients recovers the truth table.
///     </para>
///     <para>
///         Monomials are built through <see cref="FormulaFactory" /> so each AND is
///         canonical and interned; the empty monomial is the constant <c>1</c>. The XOR
///         spine is built with real <see cref="XorNode" />s rather than
///         <see cref="FormulaFactory.Xor" /> (which decomposes into OR/AND/NOT and would
///         destroy the normal form). No present monomial means the constant <c>0</c>.
///     </para>
/// </summary>
internal static class AlgebraicNormalFormConverter
{
    public static AstNode Convert(AstNode formula, CancellationToken cancellationToken)
    {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        cancellationToken.ThrowIfCancellationRequested();

        var variables = formula.GetVariables().OrderBy(v => v, StringComparer.Ordinal).ToList();
        var n = variables.Count;
        if (n > TruthTable.MaxVariables)
            throw new ArgumentException(
                $"Algebraic normal form would require 2^{n} table rows; maximum supported is {TruthTable.MaxVariables} variables");

        var size = 1 << n;

        // Truth-table values in a LSB-first convention: bit k of x is variables[k].
        var coefficients = new byte[size];
        var assignment = new Dictionary<string, bool>(n);
        for (var x = 0; x < size; x++)
        {
            if ((x & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (var k = 0; k < n; k++)
                assignment[variables[k]] = ((x >> k) & 1) != 0;
            coefficients[x] = TruthTable.Evaluate(formula, assignment) ? (byte)1 : (byte)0;
        }

        // Fast Möbius transform over GF(2): coefficients[m] = XOR over x ⊆ m of f(x).
        // This is an involution, so re-applying it to the result restores the table.
        for (var i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bit = 1 << i;
            for (var x = 0; x < size; x++)
                if ((x & bit) != 0)
                    coefficients[x] ^= coefficients[x ^ bit];
        }

        var masks = new List<int>();
        for (var mask = 0; mask < size; mask++)
            if (coefficients[mask] != 0)
                masks.Add(mask);

        // No monomials present: the function is the constant false.
        if (masks.Count == 0) return ConstantNode.False;

        // Canonical monomial order: lower degree first (constant term leads), ties by mask.
        masks.Sort((a, b) =>
        {
            var degreeA = BitOperations.PopCount((uint)a);
            var degreeB = BitOperations.PopCount((uint)b);
            return degreeA != degreeB ? degreeA.CompareTo(degreeB) : a.CompareTo(b);
        });

        var factory = new FormulaFactory();
        AstNode? result = null;
        foreach (var mask in masks)
        {
            var monomial = BuildMonomial(factory, variables, mask);
            result = result == null ? monomial : new XorNode(result, monomial);
        }

        return result!;
    }

    private static AstNode BuildMonomial(FormulaFactory factory, IReadOnlyList<string> variables, int mask)
    {
        // The empty monomial (no variables) is the constant true.
        if (mask == 0) return factory.True;

        var factors = new List<AstNode>();
        for (var k = 0; k < variables.Count; k++)
            if ((mask & (1 << k)) != 0)
                factors.Add(factory.Variable(variables[k]));

        // And(single) returns the variable itself; And(many) is a canonical AndNode.
        return factory.And(factors);
    }
}
