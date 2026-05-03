using System.Net;
using System.Text.Json.Serialization;

namespace ITAMS.Client.Models;

public static class AppCatalogs
{
    public static readonly string[] AssetTypes =
    [
        "Laptop",
        "Desktop",
        "Monitor",
        "Mobile",
        "Tablet",
        "Dock",
        "Printer",
        "Server",
        "Scanner",
        "Peripheral"
    ];

    public static readonly string[] AssetStatuses =
    [
        "InStock",
        "Assigned",
        "Active",
        "InRepair",
        "Retired",
        "Lost"
    ];

    public static readonly string[] UserRoles =
    [
        "Admin",
        "Manager",
        "Technician",
        "User",
        "Auditor"
    ];

    public static readonly string[] UserReadRoles = ["Admin", "Manager", "Auditor"];
    public static readonly string[] UserWriteRoles = ["Admin"];
    public static readonly string[] AssetReadRoles = ["Admin", "Manager", "Technician", "Auditor"];
    public static readonly string[] AssetWriteRoles = ["Admin", "Manager", "Technician"];
    public static readonly string[] AssignmentReadRoles = ["Admin", "Manager", "Technician", "Auditor"];
    public static readonly string[] AssignmentWriteRoles = ["Admin", "Manager", "Technician"];
    public static readonly string[] HistoryReadRoles = ["Admin", "Manager", "Auditor"];
    public static readonly string[] ReportReadRoles = ["Admin", "Manager", "Auditor"];
}

public static class AppRoles
{
    public static bool IsInRole(string? role, IReadOnlyCollection<string> allowedRoles) =>
        !string.IsNullOrWhiteSpace(role) &&
        allowedRoles.Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool CanReadUsers(string? role) => IsInRole(role, AppCatalogs.UserReadRoles);
    public static bool CanWriteUsers(string? role) => IsInRole(role, AppCatalogs.UserWriteRoles);
    public static bool CanReadAssets(string? role) => IsInRole(role, AppCatalogs.AssetReadRoles);
    public static bool CanWriteAssets(string? role) => IsInRole(role, AppCatalogs.AssetWriteRoles);
    public static bool CanReadAssignments(string? role) => IsInRole(role, AppCatalogs.AssignmentReadRoles);
    public static bool CanWriteAssignments(string? role) => IsInRole(role, AppCatalogs.AssignmentWriteRoles);
    public static bool CanReadHistory(string? role) => IsInRole(role, AppCatalogs.HistoryReadRoles);
    public static bool CanReadReports(string? role) => IsInRole(role, AppCatalogs.ReportReadRoles);
}

public static class ReportPresets
{
    public const string Last30Days = "30d";
    public const string Last90Days = "90d";
    public const string Last12Months = "12m";
    public const string Custom = "custom";

    public static readonly string[] All =
    [
        Last30Days,
        Last90Days,
        Last12Months,
        Custom
    ];

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed class AuthSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public CurrentUserResponse? User { get; set; }
}

public sealed class CurrentUserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class LoginRequest
{
    public string? Identifier { get; set; }
    public string? Password { get; set; }
}

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public CurrentUserResponse User { get; set; } = new();
}

public sealed class RefreshTokenRequest
{
    public string? RefreshToken { get; set; }
}

public sealed class RefreshTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

public sealed class AssetAssignmentResponse
{
    public string UserId { get; set; } = string.Empty;
    public DateTime AssignedOn { get; set; }
}

public sealed class AssetRequest
{
    public string? AssetTag { get; set; }
    public string? SerialNumber { get; set; }
    public string? Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Status { get; set; }
    public string? Department { get; set; }
    public string? Location { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime WarrantyEndDate { get; set; }
    public DateTime EndOfLifeDate { get; set; }
    public object? CurrentAssignment { get; set; }
    public string? Notes { get; set; }
}

public sealed class AssetResponse
{
    public string Id { get; set; } = string.Empty;
    public string AssetTag { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public DateTime WarrantyEndDate { get; set; }
    public DateTime EndOfLifeDate { get; set; }
    public AssetAssignmentResponse? CurrentAssignment { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AssignmentRequest
{
    public string? AssetId { get; set; }
    public string? UserId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Notes { get; set; }
}

public sealed class AssignmentResponse
{
    public string Id { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AssignedByUserId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreateUserRequest
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public string? Department { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class UpdateUserRequest
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public string? Department { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class ResetUserPasswordRequest
{
    public string? NewPassword { get; set; }
}

public sealed class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ReportsOverviewQuery
{
    public string? Preset { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? AssetDepartment { get; set; }
    public string? UserDepartment { get; set; }
}

public sealed class ReportsOverviewResponse
{
    public ReportsFilterStateResponse Filters { get; set; } = new();
    public ReportsFilterOptionsResponse AvailableFilters { get; set; } = new();
    public ReportsKpiResponse Kpis { get; set; } = new();
    public IReadOnlyList<ReportsBreakdownItemResponse> AssetsByStatus { get; set; } = [];
    public IReadOnlyList<ReportsBreakdownItemResponse> AssetsByType { get; set; } = [];
    public IReadOnlyList<ReportsBreakdownItemResponse> UsersByRole { get; set; } = [];
    public IReadOnlyList<ReportsBreakdownItemResponse> UsersByDepartment { get; set; } = [];
    public IReadOnlyList<ReportsTimeSeriesPointResponse> AssignmentsOverTime { get; set; } = [];
    public IReadOnlyList<ReportsTimeSeriesPointResponse> WarrantyExpirationsOverTime { get; set; } = [];
    public IReadOnlyList<ReportsTimeSeriesPointResponse> LifecycleActivityOverTime { get; set; } = [];
}

public sealed class ReportsFilterStateResponse
{
    public string Preset { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? AssetDepartment { get; set; }
    public string? UserDepartment { get; set; }
    public string TimeGranularity { get; set; } = string.Empty;
}

public sealed class ReportsFilterOptionsResponse
{
    public IReadOnlyList<string> AssetDepartments { get; set; } = [];
    public IReadOnlyList<string> UserDepartments { get; set; } = [];
}

public sealed class ReportsKpiResponse
{
    public int TotalAssets { get; set; }
    public int OpenAssignments { get; set; }
    public int TotalUsers { get; set; }
    public int WarrantiesExpiringSoon { get; set; }
}

public sealed class ReportsBreakdownItemResponse
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class ReportsTimeSeriesPointResponse
{
    public DateTime BucketStart { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class AuditLogDetailResponse
{
    public string? Note { get; set; }
    public string? Result { get; set; }
}

public sealed class AuditLogResponse
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public AuditLogDetailResponse? Details { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class LifecycleEventChangeResponse
{
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("old")]
    public string? OldValue { get; set; }

    [JsonPropertyName("new")]
    public string? NewValue { get; set; }

    public bool NewIsObjectId { get; set; }
}

public sealed class LifecycleEventResponse
{
    public string Id { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public IReadOnlyList<LifecycleEventChangeResponse> Changes { get; set; } = [];
    public string EventType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string PerformedByUserId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public sealed class ValidationProblemResponse
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
    public Dictionary<string, string[]> Errors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MessageResponse
{
    public string? Message { get; set; }
}

public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, string message, IReadOnlyDictionary<string, string[]>? validationErrors = null)
        : base(message)
    {
        StatusCode = statusCode;
        ValidationErrors = validationErrors ?? new Dictionary<string, string[]>();
    }

    public HttpStatusCode StatusCode { get; }
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    public IEnumerable<string> GetMessages()
    {
        if (ValidationErrors.Count == 0)
        {
            yield return Message;
            yield break;
        }

        foreach (var error in ValidationErrors)
        {
            foreach (var message in error.Value)
            {
                yield return message;
            }
        }
    }
}
