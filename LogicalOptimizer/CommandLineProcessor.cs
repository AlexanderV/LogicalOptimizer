using System;
using System.Collections.Generic;
using System.IO;

namespace LogicalOptimizer;

/// <summary>
/// Handles command line argument parsing and validation
/// </summary>
public class CommandLineProcessor
{
    public class CommandLineOptions
    {
        public string Expression { get; set; } = string.Empty;
        public bool Verbose { get; set; }
        public bool CnfOnly { get; set; }
        public bool DnfOnly { get; set; }
        public bool Advanced { get; set; }
        public bool TruthTableOnly { get; set; }
        public bool CsvInput { get; set; }
        public bool ShowHelp { get; set; }
        public bool RunTests { get; set; }
        public bool RunDemo { get; set; }
        public bool RunBenchmark { get; set; }
        public bool RunStressTest { get; set; }
        public bool ShowCsvExample { get; set; }
        public bool IsValid { get; set; } = true;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public static CommandLineOptions ParseArguments(string[] args)
    {
        var options = new CommandLineOptions();

        if (args.Length == 0)
        {
            options.IsValid = false;
            options.ErrorMessage = "No arguments provided. Use --help for usage information.";
            return options;
        }

        // Special commands
        switch (args[0])
        {
            case "--help":
            case "-h":
                options.ShowHelp = true;
                return options;

            case "--test":
                options.RunTests = true;
                return options;

            case "--demo":
                options.RunDemo = true;
                return options;

            case "--benchmark":
                options.RunBenchmark = true;
                return options;

            case "--stress":
                options.RunStressTest = true;
                return options;

            case "--csv-example":
                options.ShowCsvExample = true;
                return options;
        }

        // Expression processing with options
        options.Expression = args[0];

        if (args.Length >= 2)
        {
            switch (args[0])
            {
                case "--verbose":
                    options.Verbose = true;
                    options.Expression = args[1];
                    break;
                case "--cnf":
                    options.CnfOnly = true;
                    options.Expression = args[1];
                    break;
                case "--dnf":
                    options.DnfOnly = true;
                    options.Expression = args[1];
                    break;
                case "--advanced":
                    options.Advanced = true;
                    options.Expression = args[1];
                    break;
                case "--truth-table":
                    options.TruthTableOnly = true;
                    options.Expression = args[1];
                    break;
                case "--csv":
                    options.CsvInput = true;
                    options.Expression = args[1];
                    break;
            }
        }

        // Auto-detect CSV input
        if (!options.CsvInput)
        {
            options.CsvInput = DetectCsvInput(options.Expression);
        }

        // Validate expression length
        if (!string.IsNullOrEmpty(options.Expression) && options.Expression.Length > 10000)
        {
            options.IsValid = false;
            options.ErrorMessage = "Expression is too long (maximum 10,000 characters)";
        }

        return options;
    }

    private static bool DetectCsvInput(string expression)
    {
        // Check if input is a file path first
        if (File.Exists(expression) &&
            (expression.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
             CsvTruthTableParser.LooksLikeCsv(File.ReadAllText(expression))))
        {
            return true;
        }

        // Then check if input looks like CSV content
        return CsvTruthTableParser.LooksLikeCsv(expression);
    }

    public static void ShowUsage()
    {
        Console.WriteLine("Usage: LogicalOptimizer.exe \"<expression>\"");
        Console.WriteLine("Example: LogicalOptimizer.exe \"a & b | a & !b\"");
        Console.WriteLine();
        Console.WriteLine("For help use: LogicalOptimizer.exe --help");
    }

    public static void ShowHelp()
    {
        Console.WriteLine("LogicalOptimizer - Boolean Expression Optimizer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LogicalOptimizer.exe \"<expression>\"       # Optimize expression");
        Console.WriteLine("  LogicalOptimizer.exe --verbose \"<expression>\" # Detailed output");
        Console.WriteLine("  LogicalOptimizer.exe --cnf \"<expression>\"     # Output only CNF");
        Console.WriteLine("  LogicalOptimizer.exe --dnf \"<expression>\"     # Output only DNF");
        Console.WriteLine("  LogicalOptimizer.exe --advanced \"<expression>\" # Include advanced logical forms");
        Console.WriteLine("  LogicalOptimizer.exe --truth-table \"<expression>\" # Output only truth table");
        Console.WriteLine("  LogicalOptimizer.exe --csv \"<csv_content>\"    # Parse CSV truth table");
        Console.WriteLine("  LogicalOptimizer.exe \"<csv_file.csv>\"      # Parse CSV file (auto-detected)");
        Console.WriteLine("  LogicalOptimizer.exe --test              # Run tests");
        Console.WriteLine("  LogicalOptimizer.exe --help              # This help");
        Console.WriteLine("  LogicalOptimizer.exe --demo              # Features demonstration");
        Console.WriteLine("  LogicalOptimizer.exe --benchmark         # Performance testing");
        Console.WriteLine("  LogicalOptimizer.exe --stress            # Extreme stress testing for large expressions");
        Console.WriteLine();
        Console.WriteLine("Supported operators:");
        Console.WriteLine("  !     - logical NOT (negation)");
        Console.WriteLine("  &     - logical AND (conjunction)");
        Console.WriteLine("  |     - logical OR (disjunction)");
        Console.WriteLine("  ()    - grouping");
        Console.WriteLine("  0, 1  - logical constants");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  LogicalOptimizer.exe \"a & b | a & c\"");
        Console.WriteLine("  LogicalOptimizer.exe \"!(a & b)\"");
        Console.WriteLine("  LogicalOptimizer.exe \"!!a\"");
        Console.WriteLine();
        Console.WriteLine("CSV Truth Table Examples:");
        Console.WriteLine("  LogicalOptimizer.exe \"a,b,Result\\n0,0,0\\n0,1,1\\n1,0,1\\n1,1,0\"");
        Console.WriteLine("  LogicalOptimizer.exe truth_table.csv");
        Console.WriteLine("  LogicalOptimizer.exe --csv \"x,y,Output\\n0,0,1\\n0,1,0\\n1,0,0\\n1,1,1\"");
        Console.WriteLine();
        Console.WriteLine("CSV Format Requirements:");
        Console.WriteLine("  - Header row with variable names and 'Result'/'Output'/'Value' column");
        Console.WriteLine("  - Values: 0/1, true/false, t/f, yes/no, y/n");
        Console.WriteLine("  - Comma-separated values");
        Console.WriteLine();
        Console.WriteLine("Limitations:");
        Console.WriteLine("  - Maximum expression length: 10,000 characters");
        Console.WriteLine("  - Maximum number of variables: 100");
        Console.WriteLine("  - Maximum nesting depth: 50 levels");
    }

    public static void ShowCsvExample()
    {
        Console.WriteLine("CSV Truth Table Format Example:");
        Console.WriteLine();
        Console.WriteLine("Format: Variable names as headers, Result/Output/Value column for output");
        Console.WriteLine();
        Console.WriteLine("Example 1 - XOR function:");
        Console.WriteLine("a,b,Result");
        Console.WriteLine("0,0,0");
        Console.WriteLine("0,1,1");
        Console.WriteLine("1,0,1");
        Console.WriteLine("1,1,0");
        Console.WriteLine();
        Console.WriteLine("Example 2 - AND function:");
        Console.WriteLine("x,y,Output");
        Console.WriteLine("false,false,false");
        Console.WriteLine("false,true,false");
        Console.WriteLine("true,false,false");
        Console.WriteLine("true,true,true");
        Console.WriteLine();
        Console.WriteLine("Supported value formats:");
        Console.WriteLine("  - 0/1, true/false, t/f, yes/no, y/n");
        Console.WriteLine("  - Case insensitive");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LogicalOptimizer.exe --csv \"a,b,Result\\n0,0,0\\n0,1,1\\n1,0,1\\n1,1,0\"");
        Console.WriteLine("  LogicalOptimizer.exe truth_table.csv");
    }
}
