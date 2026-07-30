using Xunit;

namespace LogicalOptimizer.Tests;

/// <summary>
///     Tests that redirect <see cref="Console.Out" />/<see cref="Console.Error" /> to capture what
///     the CLI prints. The redirection is process-global, so these run alone: two such classes in
///     parallel would each capture parts of the other's output and fail at random.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Console redirection";
}
