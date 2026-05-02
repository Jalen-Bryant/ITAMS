namespace ITAMS.Api.Configuration;

public sealed class BootstrapAdminSettings
{
    public const string SectionName = "BootstrapAdmin";

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Department { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(Department) &&
        !string.IsNullOrWhiteSpace(Password);
}
