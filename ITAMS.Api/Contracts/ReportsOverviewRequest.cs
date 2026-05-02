namespace ITAMS.Api.Contracts;

public sealed class ReportsOverviewRequest
{
    public string? Preset { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? AssetDepartment { get; init; }
    public string? UserDepartment { get; init; }
}
