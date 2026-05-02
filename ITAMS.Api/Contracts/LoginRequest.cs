namespace ITAMS.Api.Contracts;

public sealed class LoginRequest
{
    public string? Identifier { get; init; }

    public string? Password { get; init; }
}
