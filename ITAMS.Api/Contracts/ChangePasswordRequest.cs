namespace ITAMS.Api.Contracts;

public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }

    public string? NewPassword { get; init; }
}
