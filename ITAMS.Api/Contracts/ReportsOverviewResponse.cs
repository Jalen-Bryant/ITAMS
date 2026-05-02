namespace ITAMS.Api.Contracts;

public sealed class ReportsOverviewResponse
{
    public ReportsFilterStateResponse Filters { get; init; } = new();
    public ReportsFilterOptionsResponse AvailableFilters { get; init; } = new();
    public ReportsKpiResponse Kpis { get; init; } = new();
    public IReadOnlyList<ReportsBreakdownItemResponse> AssetsByStatus { get; init; } = [];
    public IReadOnlyList<ReportsBreakdownItemResponse> AssetsByType { get; init; } = [];
    public IReadOnlyList<ReportsBreakdownItemResponse> UsersByRole { get; init; } = [];
    public IReadOnlyList<ReportsBreakdownItemResponse> UsersByDepartment { get; init; } = [];
    public IReadOnlyList<ReportsTimeSeriesPointResponse> AssignmentsOverTime { get; init; } = [];
    public IReadOnlyList<ReportsTimeSeriesPointResponse> WarrantyExpirationsOverTime { get; init; } = [];
    public IReadOnlyList<ReportsTimeSeriesPointResponse> LifecycleActivityOverTime { get; init; } = [];
}

public sealed class ReportsFilterStateResponse
{
    public string Preset { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public string? AssetDepartment { get; init; }
    public string? UserDepartment { get; init; }
    public string TimeGranularity { get; init; } = string.Empty;
}

public sealed class ReportsFilterOptionsResponse
{
    public IReadOnlyList<string> AssetDepartments { get; init; } = [];
    public IReadOnlyList<string> UserDepartments { get; init; } = [];
}

public sealed class ReportsKpiResponse
{
    public int TotalAssets { get; init; }
    public int OpenAssignments { get; init; }
    public int TotalUsers { get; init; }
    public int WarrantiesExpiringSoon { get; init; }
}

public sealed class ReportsBreakdownItemResponse
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
}

public sealed class ReportsTimeSeriesPointResponse
{
    public DateTime BucketStart { get; init; }
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
}
