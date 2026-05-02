namespace ITAMS.Api.Contracts;

public sealed class AssetAssignmentRequest
{
    public string? UserId { get; init; }

    public DateTime AssignedOn { get; init; }
}
