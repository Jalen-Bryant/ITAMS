using System.ComponentModel.DataAnnotations;

namespace ITAMS.Api.Configuration;

public sealed class SecuritySettings
{
    public const string SectionName = "Security";

    public RateLimitSettings GeneralRateLimit { get; init; } = new()
    {
        PermitLimit = 300,
        WindowSeconds = 60
    };

    public RateLimitSettings AuthRateLimit { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60
    };

    public AuthLockoutSettings AuthLockout { get; init; } = new();

    [Range(1024, 10 * 1024 * 1024)]
    public int MaxRequestBodyBytes { get; init; } = 1024 * 1024;

    public bool ContentSecurityPolicyReportOnly { get; init; } = true;
}

public sealed class RateLimitSettings
{
    [Range(1, 10000)]
    public int PermitLimit { get; init; } = 60;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

public sealed class AuthLockoutSettings
{
    [Range(2, 20)]
    public int MaxFailedAttempts { get; init; } = 5;

    [Range(1, 1440)]
    public int FailureWindowMinutes { get; init; } = 15;

    [Range(1, 1440)]
    public int BaseLockoutMinutes { get; init; } = 5;

    [Range(1, 1440)]
    public int MaxLockoutMinutes { get; init; } = 60;
}
