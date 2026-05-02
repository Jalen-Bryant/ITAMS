namespace ITAMS.Api.Contracts;

public sealed class CreateAssignmentRequest
{
    public string? AssetId { get; init; }

    public string? UserId { get; init; }

    public DateTime StartDate { get; init; }

    public DateTime? EndDate { get; init; }

    public string? Notes { get; init; }
}
