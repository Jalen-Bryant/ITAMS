namespace ITAMS.Api.Contracts;

public sealed class AuditLogDetailRequest
{
    public string? Note { get; init; }

    public string? Result { get; init; }
}
