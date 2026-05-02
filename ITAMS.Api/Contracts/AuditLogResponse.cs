namespace ITAMS.Api.Contracts;

public sealed class AuditLogResponse
{
    public string Id { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string ActorUserId { get; init; } = string.Empty;

    public AuditLogDetailResponse? Details { get; init; }

    public string EntityId { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public string Ip { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; }

    public string UserAgent { get; init; } = string.Empty;
}
