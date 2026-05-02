namespace ITAMS.Api.Contracts;

public sealed class UpdateUserRequest
{
    public string? Username { get; init; }

    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public string? Role { get; init; }

    public string? Department { get; init; }

    public bool? IsActive { get; init; }
}
