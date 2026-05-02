namespace ITAMS.Api.Contracts;

public sealed class UpdateAuditLogRequest
{
    public string? Action { get; init; }

    public string? ActorUserId { get; init; }

    public AuditLogDetailRequest? Details { get; init; }

    public string? EntityId { get; init; }

    public string? EntityType { get; init; }

    public string? Ip { get; init; }

    public string? UserAgent { get; init; }
}
