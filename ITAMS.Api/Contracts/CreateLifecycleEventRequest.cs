namespace ITAMS.Api.Contracts;

public sealed class CreateLifecycleEventRequest
{
    public string? AssetId { get; init; }

    public IReadOnlyList<LifecycleEventChangeRequest>? Changes { get; init; }

    public string? EventType { get; init; }

    public string? Notes { get; init; }

    public string? PerformedByUserId { get; init; }
}
