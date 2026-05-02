namespace ITAMS.Api.Contracts;

public sealed class RefreshTokenResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; init; }

    public DateTime RefreshTokenExpiresAt { get; init; }
}
