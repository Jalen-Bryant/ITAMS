namespace ITAMS.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string Authenticated = "Authenticated";
    public const string UserRead = "UserRead";
    public const string UserWrite = "UserWrite";
    public const string AssetRead = "AssetRead";
    public const string AssetWrite = "AssetWrite";
    public const string AssignmentRead = "AssignmentRead";
    public const string AssignmentWrite = "AssignmentWrite";
    public const string HistoryRead = "HistoryRead";
    public const string ReportsRead = "ReportsRead";

    public static readonly string[] UserReadRoles = ["Admin", "Manager", "Auditor"];
    public static readonly string[] UserWriteRoles = ["Admin"];
    public static readonly string[] AssetReadRoles = ["Admin", "Manager", "Technician", "Auditor"];
    public static readonly string[] AssetWriteRoles = ["Admin", "Manager", "Technician"];
    public static readonly string[] AssignmentReadRoles = ["Admin", "Manager", "Technician", "Auditor"];
    public static readonly string[] AssignmentWriteRoles = ["Admin", "Manager", "Technician"];
    public static readonly string[] HistoryReadRoles = ["Admin", "Manager", "Auditor"];
    public static readonly string[] ReportsReadRoles = ["Admin", "Manager", "Auditor"];
}
