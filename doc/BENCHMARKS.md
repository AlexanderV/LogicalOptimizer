# Benchmarks

Machine-readable results (roadmap P0.3): every run of `LogicalOptimizer.Benchmarks`
emits `BenchmarkDotNet.Artifacts/results/*-report-full.json` (full measurement data)
alongside CSV/HTML/GitHub-markdown reports. Regenerate with:

```powershell
dotnet run -c Release --project LogicalOptimizer.Benchmarks -- --filter * [--job short]
```

Environment of the published run: BenchmarkDotNet v0.14.0, Windows 11, .NET 10.0.9,
X64 RyuJIT AVX2, ShortRun job (3 iterations - indicative, not publication-grade;
re-run with the default job for tighter error bars).

Cross-library comparison methodology note: numeric comparison against SymPy/PyEDA
requires a shared corpus, one machine and one cost model; the differential
correctness corpus against SymPy lives in the test suite
(`SymPyDifferentialTests`, CI) - timing comparisons are deliberately NOT published
until they can be produced under those controls.
### OptimizationBenchmarks

| Method                    | Mean        | Error       | StdDev    | Gen0     | Gen1     | Gen2     | Allocated  |
|-------------------------- |------------:|------------:|----------:|---------:|---------:|---------:|-----------:|
| SmallExpression           |    87.94 μs |    31.98 μs |  1.753 μs |  35.8276 |   0.3052 |        - |  219.77 KB |
| GuaranteeZoneTenVariables | 7,791.00 μs | 1,244.70 μs | 68.226 μs | 578.1250 | 328.1250 | 242.1875 | 3976.35 KB |
| MidRangeFourteenVariables |   443.08 μs |   157.77 μs |  8.648 μs | 166.5039 |   3.9063 |        - | 1020.68 KB |

### SatSolverBenchmarks

| Method                         | Mean     | Error    | StdDev  | Gen0    | Gen1   | Allocated |
|------------------------------- |---------:|---------:|--------:|--------:|-------:|----------:|
| PhaseTransition60Variables     | 471.6 μs | 20.36 μs | 1.12 μs | 29.7852 | 4.8828 | 184.87 KB |
| DeMorganEquivalence30Variables | 167.7 μs |  8.94 μs | 0.49 μs | 28.3203 | 3.4180 | 174.32 KB |

### ExactMinimizationBenchmarks

| Method                     | Mean     | Error    | StdDev   | Gen0      | Gen1     | Allocated |
|--------------------------- |---------:|---------:|---------:|----------:|---------:|----------:|
| QuineMcCluskeyTenVariables | 81.65 ms | 9.980 ms | 0.547 ms | 1750.0000 | 125.0000 |   11.2 MB |

### NewEnginesBenchmarks

| Method                             | Mean         | Error         | StdDev     | Gen0       | Gen1      | Gen2     | Allocated    |
|----------------------------------- |-------------:|--------------:|-----------:|-----------:|----------:|---------:|-------------:|
| EspressoLite_FortyVariableCover    | 66,879.39 μs | 13,336.972 μs | 731.044 μs | 18727.2727 | 1090.9091 | 272.7273 | 115013.61 KB |
| BddSifting_TwelveVariablePairs     |  2,875.13 μs |    392.783 μs |  21.530 μs |   822.2656 |   76.1719 |        - |   5048.98 KB |
| FormulaFactory_ImportFortyVarCover |  1,689.61 μs |    225.850 μs |  12.380 μs |    50.7813 |   15.6250 |        - |     323.4 KB |
| Aig_FromFortyVarCover              |     62.24 μs |      6.092 μs |   0.334 μs |     8.7280 |    0.4272 |        - |     53.57 KB |


