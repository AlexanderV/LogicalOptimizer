using System.Text.Json;

namespace Samples;

/// <summary>Shared JSON options for the machine-readable artifact a CI recipe emits.</summary>
internal static class SampleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
