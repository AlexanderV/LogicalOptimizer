# LogicalOptimizer - Boolean Expression Optimizer

> **🤖 AI-Assisted Development Notice**
> 
> This project was developed with extensive assistance from Large Language Models (LLM), including:
> - Architecture design and implementation guidance
> - Code generation and optimization techniques
> - Comprehensive testing framework creation
> - Documentation and specification writing
> - Best practices implementation and code quality improvements
>
> The collaboration between human creativity and AI capabilities resulted in a robust, well-tested boolean expression optimization system with advanced features and comprehensive documentation.

## Overview

LogicalOptimizer is a powerful tool for parsing, optimizing and transforming boolean expressions into various normal forms with maximum simplification.

## Features

- ✅ **Core Boolean Operations**: AND (`&`), OR (`|`), NOT (`!`) with proper precedence
- ✅ **Smart Optimization**: All basic laws of boolean algebra with factorization
- ✅ **Normal Forms**: Conversion to CNF (Conjunctive) and DNF (Disjunctive)
- ✅ **Advanced Logic Forms**: Extended operators (XOR, IMP) generation 
- ✅ **Context-Aware Formatting**: Intelligent parentheses placement
- ✅ **Truth Table Generation**: Up to 20 variables with equivalence verification
- ✅ **Multiple Export Formats**: DIMACS, BLIF, Verilog, CSV, Mathematical notation, LaTeX
- ✅ **Performance Analytics**: Detailed metrics and benchmarking
- ✅ **Comprehensive Testing**: 800+ tests with full validation
- ✅ **Error Protection**: Input validation and infinite loop prevention

## Quick Start

### Installation
```bash
git clone https://github.com/your-repo/LogicalOptimizer.git
cd LogicalOptimizer
dotnet build
```

### Basic Usage
```bash
# Expression optimization
dotnet run --project LogicalOptimizer -- "a & b | a & c"
# Output:
# Original: a & b | a & c
# Optimized: a & (b | c)
# CNF: a & (b | c)
# DNF: a & b | a & c
# Variables: [a, b, c]
# Advanced: (empty - no advanced patterns found)

# Expression with XOR pattern
dotnet run --project LogicalOptimizer -- "a & !b | !a & b"
# Output:
# Original: a & !b | !a & b
# Optimized: a & !b | b & !a
# CNF: (a | b) & (!b | !a)
# DNF: a & !b | b & !a
# Variables: [a, b]
# Advanced: a XOR b

# Complex expression with multiple patterns (XOR + IMP)
dotnet run --project LogicalOptimizer -- "((a & !b) | (!a & b)) & ((!c | d) | (e & f))"
# Output:
# Original: ((a & !b) | (!a & b)) & ((!c | d) | (e & f))
# Optimized: (d | !c | e & f) & (a & !b | b & !a)
# CNF: (d | !c | e) & (d | !c | f) & (a | b) & (!b | !a)
# DNF: d & a & !b | d & b & !a | !c & a & !b | !c & b & !a | e & f & a & !b | e & f & b & !a
# Variables: [a, b, c, d, e, f]
# Advanced: (a XOR b) & ((c → d) | e & f)

# Get only CNF (Conjunctive Normal Form)
dotnet run --project LogicalOptimizer -- --cnf "a & b | c"
# Result: (a | c) & (b | c)

# Get only DNF (Disjunctive Normal Form)  
dotnet run --project LogicalOptimizer -- --dnf "(a | b) & c"
# Result: a & c | b & c

# Get only Advanced logical forms
dotnet run --project LogicalOptimizer -- --advanced "a & !b | !a & b"
# Result: XOR: a XOR b

dotnet run --project LogicalOptimizer -- --advanced "!a | b"
# Result: IMP: a → b

# Examples showing advanced forms (XOR, IMP):
# XOR pattern expression:
dotnet run --project LogicalOptimizer -- "a & !b | !a & b"
# Output includes: Advanced: XOR: a XOR b

# Implication pattern expression:
dotnet run --project LogicalOptimizer -- "!a | b"
# Output includes: Advanced: IMP: a → b

# Expression without advanced patterns:
dotnet run --project LogicalOptimizer -- "a & b | a & c"  
# Output includes: Advanced: (empty)

# Detailed output with metrics
dotnet run --project LogicalOptimizer -- --verbose "!(a & b)"

# Run built-in tests
dotnet run --project LogicalOptimizer -- --test

# Features demonstration
dotnet run --project LogicalOptimizer -- --demo

# Performance benchmarks
dotnet run --project LogicalOptimizer -- --benchmark

# Help
dotnet run --project LogicalOptimizer -- --help
```

## Supported Operators

### Core Operators
| Operator | Description | Priority | Example |
|----------|-------------|----------|---------|
| `!` | Logical NOT (negation) | 1 (Highest) | `!a` |
| `&` | Logical AND (conjunction) | 2 (Medium) | `a & b` |
| `\|` | Logical OR (disjunction) | 3 (Lowest) | `a \| b` |
| `()` | Grouping | - | `(a \| b) & c` |
| `0`, `1` | Logical constants | - | `a & 1` |

### Advanced Logical Forms
| Form | Description | Pattern | Advanced Display |
|------|-------------|---------|------------------|
| **XOR** | Exclusive OR | `a & !b \| !a & b` | `a XOR b` |
| **IMP** | Implication | `!a \| b` | `a → b` |

**Note**: Advanced forms are generated for display purposes and logical clarity. All internal processing uses core operators only.

## Usage Examples

### Factorization (main example from specification)
```bash
Input: "(a | b) & (a | c)"
Output: "a | (b & c)"
```

### De Morgan's Laws
```bash
Input: "!(a & b)"
Output: "!a | !b"
```

### Constants Simplification
```bash
Input: "a & 1 | b & 0"
Output: "a"
```

### Consensus rule
```bash
Input: "a & b | !a & c | b & c"
Output: "a & b | !a & c"
```

### Advanced Pattern Recognition
```bash
# XOR Pattern Detection
Input: "a & !b | !a & b"
Output: "a XOR b"

# Implication Pattern Detection  
Input: "!a | b"
Output: "a → b"

# Complex Mixed Patterns
Input: "((a & !b) | (!a & b)) & ((!c | d) | (e & f))"
Output: "(a XOR b) & ((c → d) | e & f)"
```

## Programming Interface (API)

```csharp
using BooleanOptimizer;

var optimizer = new BooleanExpressionOptimizer();
var result = optimizer.OptimizeExpression("a & b | a & c", includeMetrics: true);

Console.WriteLine($"Original: {result.Original}");
Console.WriteLine($"Optimized: {result.Optimized}");
Console.WriteLine($"CNF: {result.CNF}");
Console.WriteLine($"DNF: {result.DNF}");
Console.WriteLine($"Variables: [{string.Join(", ", result.Variables)}]");

// Performance metrics
if (result.Metrics != null)
{
    Console.WriteLine($"Time: {result.Metrics.ElapsedTime.TotalMilliseconds:F2}ms");
    Console.WriteLine($"Nodes: {result.Metrics.OriginalNodes} → {result.Metrics.OptimizedNodes}");
    Console.WriteLine($"Iterations: {result.Metrics.Iterations}");
    Console.WriteLine($"Rules applied: {result.Metrics.AppliedRules}");
}

// Equivalence verification through truth tables
Console.WriteLine($"Equivalent to original: {result.IsEquivalent()}");
```

## Export Formats

The optimizer supports multiple export formats for integration with external tools:

```csharp
using BooleanOptimizer;

string expression = "a & b | c";

// Export to DIMACS format (for SAT solvers)
string dimacs = BooleanExpressionExporter.ToDimacs(expression);

// Export to BLIF format (for digital circuit design)
string blif = BooleanExpressionExporter.ToBlif(expression, "my_circuit");

// Export to Verilog format (for hardware description)
string verilog = BooleanExpressionExporter.ToVerilog(expression, "my_module");

// Export to mathematical notation (Unicode symbols)
string math = BooleanExpressionExporter.ToMathematicalNotation(expression);
// Result: "a ∧ b ∨ c"

// Export to LaTeX format (for academic papers and documents)
string latex = BooleanExpressionExporter.ToLatex(expression);
// Result: "a \\land b \\lor c"

// Export truth table to CSV
string csv = BooleanExpressionExporter.TruthTableToCsv(expression);
```

## Testing

```bash
# Full test suite (800+ tests)
dotnet test

# Filtered tests
dotnet test --filter "TruthTable"

# Performance tests
dotnet test --filter "Performance"
```

## Advanced Features

### Performance Validation
```bash
# Run comprehensive benchmarks
dotnet run --project LogicalOptimizer -- --benchmark

# Performance analysis for specific expression
dotnet run --project LogicalOptimizer -- --verbose "complex_expression_here"
```

### AST Visualization
The system provides Abstract Syntax Tree visualization for debugging and educational purposes:

```csharp
var optimizer = new BooleanExpressionOptimizer();
var result = optimizer.OptimizeExpression("(a | b) & (c | d)", enableMetrics: true);

// Display parse tree structure
Console.WriteLine(result.AstVisualization);
```

### Quality Analysis
Built-in optimization quality analyzer provides detailed metrics:
- Expression complexity reduction percentage
- Number of optimization rules applied
- Convergence analysis
- Memory usage statistics

## Requirements

- **.NET 8.0 or higher**
- **Operating System**: Windows, Linux, or macOS
- **Memory**: Minimum 512MB RAM (1GB+ recommended for large expressions)
- **Storage**: 50MB free disk space

## Documentation

- 📖 **[Technical Specification](doc/Spec.md)** - Complete system specification
- 🚀 **[Advanced Features Guide](doc/ADVANCED_FEATURES.md)** - Extended functionality documentation
- 💡 **[Examples](doc/examples/)** - Comprehensive usage examples and demos
- 🧪 **[Testing Guide](LogicalOptimizer.Tests/)** - Test suite documentation

## Limitations

- Maximum expression length: 10,000 characters
- Maximum number of variables: 100
- Maximum nesting depth: 50 levels
- Maximum processing time: 30 seconds
- Maximum optimization iterations: 50

## Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌──────────────────────┐
│     Lexer       │───▶│     Parser       │───▶│  ExpressionOptimizer │
│ (tokenization)  │    │ (AST building)   │    │   (optimization)     │
└─────────────────┘    └──────────────────┘    └──────────────────────┘
                                                            │
                                                            ▼
┌─────────────────┐    ┌─────────────────────┐    ┌───────────────────────┐
│  TruthTable     │◀───│ NormalFormConverter │◀───│   FormattingController│
│ (verification)  │    │   (CNF/DNF)         │    │   (parentheses)       │
└─────────────────┘    └─────────────────────┘    └───────────────────────┘
```

## Project Statistics

- **Total tests**: 800+ (100% pass rate)
- **Code coverage**: 88% comprehensive coverage
- **Optimization algorithms**: 15+ (factorization, De Morgan, absorption, consensus, etc.)
- **Supported optimization rules**: 20+ boolean algebra transformations
- **Pattern recognition**: XOR and IMP pattern detection and replacement
- **Export formats**: 6 (DIMACS, BLIF, Verilog, Mathematical, LaTeX, CSV)
- **Operator support**: 3 core operators (AND, OR, NOT) + 2 advanced forms (XOR, IMP)
- **Performance**: < 1sec for expressions up to 50 variables
- **Truth table capacity**: Up to 20 variables (1M+ combinations)
- **Platform support**: Cross-platform (.NET 8.0)

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## License

Distributed under the Apache 2.0 License. See [LICENSE](https://github.com/AlexanderV/LogicalOptimizer#Apache-2.0-1-ov-file) for more information.

## Contact

Project: [https://github.com/AlexanderV/LogicalOptimizer](https://github.com/AlexanderV/LogicalOptimizer)
