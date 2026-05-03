using ITAMS.Api.Models;

namespace ITAMS.Api.Services;

public sealed class AuthResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public UserDocument? User { get; init; }

    public string AccessToken { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; init; }

    public DateTime RefreshTokenExpiresAt { get; init; }
}

public sealed class PasswordChangeResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public bool NotFound { get; init; }
}
