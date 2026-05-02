using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using MongoDB.Bson;

namespace ITAMS.Api.Endpoints;

public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/audit-logs").WithTags("Audit Logs");

        group.MapGet(string.Empty, GetAllAuditLogsAsync)
            .WithName("GetAuditLogs")
            .RequireAuthorization(AuthorizationPolicies.HistoryRead);

        group.MapGet("/{id}", GetAuditLogByIdAsync)
            .WithName("GetAuditLogById")
            .RequireAuthorization(AuthorizationPolicies.HistoryRead);

        return app;
    }

    private static async Task<IResult> GetAllAuditLogsAsync(
        AuditLogsService auditLogsService,
        CancellationToken cancellationToken)
    {
        var auditLogs = await auditLogsService.GetAllAsync(cancellationToken);
        return Results.Ok(auditLogs.Select(MapResponse));
    }

    private static async Task<IResult> GetAuditLogByIdAsync(
        string id,
        AuditLogsService auditLogsService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAuditLogId(id, out var auditLogId, out var validationResult))
        {
            return validationResult;
        }

        var auditLog = await auditLogsService.GetByIdAsync(auditLogId, cancellationToken);
        return auditLog is null ? Results.NotFound() : Results.Ok(MapResponse(auditLog));
    }

    private static bool TryParseAuditLogId(string rawId, out ObjectId auditLogId, out IResult validationResult)
    {
        if (ObjectId.TryParse(rawId, out auditLogId))
        {
            validationResult = Results.Empty;
            return true;
        }

        validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = ["The audit log id must be a valid MongoDB ObjectId."]
        });
        return false;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static AuditLogResponse MapResponse(AuditLogDocument auditLog) =>
        new()
        {
            Id = auditLog.Id.ToString(),
            Action = auditLog.Action,
            ActorUserId = auditLog.ActorUserId.ToString(),
            Details = auditLog.Details is null
                ? null
                : new AuditLogDetailResponse
                {
                    Note = auditLog.Details.Note,
                    Result = auditLog.Details.Result
                },
            EntityId = auditLog.EntityId.ToString(),
            EntityType = auditLog.EntityType,
            Ip = auditLog.Ip,
            Timestamp = EnsureUtc(auditLog.Timestamp),
            UserAgent = auditLog.UserAgent
        };
}
