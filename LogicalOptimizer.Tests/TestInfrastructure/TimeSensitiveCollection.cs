using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Collection for suites whose workloads run close to the facade's documented
///     wall-clock envelope (<see cref="PerformanceValidator.MAX_PROCESSING_TIME_SECONDS" />,
///     10 s per <c>OptimizeExpression</c> call). Inside the fully parallel gate a dense case
///     that solves in ~3 s alone can be CPU-starved past the cap and fail with the product's
///     own <see cref="TimeoutException" /> — a load flake, not a regression (observed twice on
///     the 8-input PLA corpus member under a loaded machine, 2026-07-31/2026-08-03).
///     DisableParallelization runs these suites in isolation, so they get the whole CPU and
///     their pinned results stay deterministic.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimeSensitiveCollection
{
    public const string Name = "Time-sensitive facade envelope";
}
