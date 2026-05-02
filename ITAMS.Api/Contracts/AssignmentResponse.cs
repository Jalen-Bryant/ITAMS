namespace ITAMS.Api.Contracts;

public sealed class AssignmentResponse
{
    public string Id { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public string AssignedByUserId { get; init; } = string.Empty;

    public DateTime StartDate { get; init; }

    public DateTime? EndDate { get; init; }

    public string? Notes { get; init; }

    public DateTime CreatedAt { get; init; }
}
