using System.ComponentModel.DataAnnotations;

namespace ITAMS.Api.Configuration;

public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDb";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string DatabaseName { get; init; } = string.Empty;

    [Required]
    public string AssetsCollectionName { get; init; } = string.Empty;

    [Required]
    public string AssignmentsCollectionName { get; init; } = string.Empty;

    [Required]
    public string AuditLogsCollectionName { get; init; } = string.Empty;

    [Required]
    public string LifecycleEventsCollectionName { get; init; } = string.Empty;

    [Required]
    public string UsersCollectionName { get; init; } = string.Empty;

    [Required]
    public string UserSessionsCollectionName { get; init; } = string.Empty;
}
