namespace ITAMS.Api.Contracts;

public sealed class AssetAssignmentResponse
{
    public string UserId { get; init; } = string.Empty;

    public DateTime AssignedOn { get; init; }
}
