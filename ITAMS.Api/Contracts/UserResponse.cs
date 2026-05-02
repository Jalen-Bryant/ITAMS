namespace ITAMS.Api.Contracts;

public sealed class UserResponse
{
    public string Id { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string Department { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}
