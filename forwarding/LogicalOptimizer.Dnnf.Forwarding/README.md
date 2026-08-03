# LogicalOptimizer.Dnnf (deprecated forwarding package)

**Since v4.0 the whole toolkit ships as the single package
[LogicalOptimizer](https://www.nuget.org/packages/LogicalOptimizer/).**

LogicalOptimizer.Dnnf contains no code anymore: it only depends on `LogicalOptimizer`,
so existing project references keep compiling unchanged (all types stay in the
`LogicalOptimizer` namespace). Replace it at your convenience:

```bash
dotnet remove package LogicalOptimizer.Dnnf
dotnet add package LogicalOptimizer
```

This forwarding ID is published for a transition period and will stop receiving new
versions. Details: the package-consolidation decision record in the repository.
