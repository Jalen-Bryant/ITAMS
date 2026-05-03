using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using MongoDB.Bson;

namespace ITAMS.Api.Endpoints;

public static class LifecycleEventEndpoints
{
    public static IEndpointRouteBuilder MapLifecycleEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/lifecycle-events").WithTags("Lifecycle Events");

        group.MapGet(string.Empty, GetAllLifecycleEventsAsync)
            .WithName("GetLifecycleEvents")
            .RequireAuthorization(AuthorizationPolicies.HistoryRead);

        group.MapGet("/{id}", GetLifecycleEventByIdAsync)
            .WithName("GetLifecycleEventById")
            .RequireAuthorization(AuthorizationPolicies.HistoryRead);

        return app;
    }

    private static async Task<IResult> GetAllLifecycleEventsAsync(
        int? offset,
        int? limit,
        LifecycleEventsService lifecycleEventsService,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(offset, limit, out var pageRequest, out var validationErrors))
        {
            return Results.ValidationProblem(validationErrors);
        }

        var lifecycleEvents = await lifecycleEventsService.GetAllAsync(pageRequest, cancellationToken);
        return Results.Ok(lifecycleEvents.Select(MapResponse));
    }

    private static async Task<IResult> GetLifecycleEventByIdAsync(
        string id,
        LifecycleEventsService lifecycleEventsService,
        CancellationToken cancellationToken)
    {
        if (!TryParseLifecycleEventId(id, out var lifecycleEventId, out var validationResult))
        {
            return validationResult;
        }

        var lifecycleEvent = await lifecycleEventsService.GetByIdAsync(lifecycleEventId, cancellationToken);
        return lifecycleEvent is null ? Results.NotFound() : Results.Ok(MapResponse(lifecycleEvent));
    }

    private static bool TryParseLifecycleEventId(string rawId, out ObjectId lifecycleEventId, out IResult validationResult)
    {
        if (ObjectId.TryParse(rawId, out lifecycleEventId))
        {
            validationResult = Results.Empty;
            return true;
        }

        validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = ["The lifecycle event id must be a valid MongoDB ObjectId."]
        });
        return false;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static LifecycleEventResponse MapResponse(LifecycleEventDocument lifecycleEvent) =>
        new()
        {
            Id = lifecycleEvent.Id.ToString(),
            AssetId = lifecycleEvent.AssetId.ToString(),
            Changes = lifecycleEvent.Changes
                .Select(change => new LifecycleEventChangeResponse
                {
                    Field = change.Field,
                    OldValue = change.OldValue,
                    NewValue = change.NewValue.IsBsonNull
                        ? null
                        : change.NewValue.IsObjectId
                            ? change.NewValue.AsObjectId.ToString()
                            : change.NewValue.ToString(),
                    NewIsObjectId = change.NewValue.IsObjectId
                })
                .ToArray(),
            EventType = lifecycleEvent.EventType,
            Notes = lifecycleEvent.Notes,
            PerformedByUserId = lifecycleEvent.PerformedByUserId.ToString(),
            Timestamp = EnsureUtc(lifecycleEvent.Timestamp)
        };
}
