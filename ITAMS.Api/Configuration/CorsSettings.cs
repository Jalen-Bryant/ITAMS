namespace ITAMS.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Frontend";

    public string[] AllowedOrigins { get; init; } = [];
}
