using System.Text.Json.Serialization;

namespace ITAMS.Api.Contracts;

public sealed class LifecycleEventChangeResponse
{
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("old")]
    public string? OldValue { get; init; }

    [JsonPropertyName("new")]
    public string? NewValue { get; init; }

    public bool NewIsObjectId { get; init; }
}
