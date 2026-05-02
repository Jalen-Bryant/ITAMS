using System.Text.Json.Serialization;

namespace ITAMS.Api.Contracts;

public sealed class LifecycleEventChangeRequest
{
    public string? Field { get; init; }

    [JsonPropertyName("old")]
    public string? OldValue { get; init; }

    [JsonPropertyName("new")]
    public string? NewValue { get; init; }

    public bool NewIsObjectId { get; init; }
}
