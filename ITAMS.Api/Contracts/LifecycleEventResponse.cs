namespace ITAMS.Api.Contracts;

public sealed class LifecycleEventResponse
{
    public string Id { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public IReadOnlyList<LifecycleEventChangeResponse> Changes { get; init; } = [];

    public string EventType { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string PerformedByUserId { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; }
}
