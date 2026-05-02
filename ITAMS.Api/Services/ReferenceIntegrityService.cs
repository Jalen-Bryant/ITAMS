using MongoDB.Bson;

namespace ITAMS.Api.Services;

public sealed class ReferenceIntegrityService(
    AssetsService assetsService,
    AssignmentsService assignmentsService,
    LifecycleEventsService lifecycleEventsService,
    UsersService usersService)
{
    public async Task<Dictionary<string, string[]>> ValidateAssetCurrentAssignmentAsync(
        ObjectId? currentAssignmentUserId,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (currentAssignmentUserId is null)
        {
            return errors;
        }

        if (!await usersService.ExistsAsync(currentAssignmentUserId.Value, cancellationToken))
        {
            errors["currentAssignment.userId"] = ["currentAssignment.userId references a user that does not exist."];
        }

        return errors;
    }

    public async Task<Dictionary<string, string[]>> ValidateAssignmentReferencesAsync(
        ObjectId assetId,
        ObjectId userId,
        ObjectId assignedByUserId,
        CancellationToken cancellationToken = default)
    {
        var assetExistsTask = assetsService.ExistsAsync(assetId, cancellationToken);
        var userExistsTask = usersService.ExistsAsync(userId, cancellationToken);
        var assignedByUserExistsTask = usersService.ExistsAsync(assignedByUserId, cancellationToken);

        await Task.WhenAll(assetExistsTask, userExistsTask, assignedByUserExistsTask);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!assetExistsTask.Result)
        {
            errors["assetId"] = ["assetId references an asset that does not exist."];
        }

        if (!userExistsTask.Result)
        {
            errors["userId"] = ["userId references a user that does not exist."];
        }

        if (!assignedByUserExistsTask.Result)
        {
            errors["assignedByUserId"] = ["assignedByUserId references a user that does not exist."];
        }

        return errors;
    }

    public async Task<Dictionary<string, string[]>> ValidateLifecycleEventReferencesAsync(
        ObjectId assetId,
        ObjectId performedByUserId,
        CancellationToken cancellationToken = default)
    {
        var assetExistsTask = assetsService.ExistsAsync(assetId, cancellationToken);
        var userExistsTask = usersService.ExistsAsync(performedByUserId, cancellationToken);

        await Task.WhenAll(assetExistsTask, userExistsTask);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!assetExistsTask.Result)
        {
            errors["assetId"] = ["assetId references an asset that does not exist."];
        }

        if (!userExistsTask.Result)
        {
            errors["performedByUserId"] = ["performedByUserId references a user that does not exist."];
        }

        return errors;
    }

    public async Task<Dictionary<string, string[]>> ValidateAuditLogReferencesAsync(
        string entityType,
        ObjectId entityId,
        ObjectId actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actorUserExistsTask = usersService.ExistsAsync(actorUserId, cancellationToken);
        var entityExistsTask = entityType switch
        {
            "Asset" => assetsService.ExistsAsync(entityId, cancellationToken),
            "User" => usersService.ExistsAsync(entityId, cancellationToken),
            "Assignment" => assignmentsService.ExistsAsync(entityId, cancellationToken),
            "LifecycleEvent" => lifecycleEventsService.ExistsAsync(entityId, cancellationToken),
            _ => Task.FromResult(false)
        };

        await Task.WhenAll(actorUserExistsTask, entityExistsTask);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!actorUserExistsTask.Result)
        {
            errors["actorUserId"] = ["actorUserId references a user that does not exist."];
        }

        if (!entityExistsTask.Result)
        {
            errors["entityId"] = [$"entityId references a {entityType} that does not exist."];
        }

        return errors;
    }

    public async Task<Dictionary<string, string[]>> ValidateAssetDeletionAsync(
        ObjectId assetId,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (await assignmentsService.HasAnyForAssetAsync(assetId, cancellationToken))
        {
            errors["id"] = ["This asset cannot be deleted because assignment history exists. Delete related assignments first."];
        }

        return errors;
    }

    public async Task<Dictionary<string, string[]>> ValidateUserDeletionAsync(
        ObjectId userId,
        CancellationToken cancellationToken = default)
    {
        var hasCurrentAssetAssignmentTask = assetsService.HasCurrentAssignmentForUserAsync(userId, cancellationToken);
        var hasAssignmentsTask = assignmentsService.HasAnyForUserAsync(userId, cancellationToken);

        await Task.WhenAll(hasCurrentAssetAssignmentTask, hasAssignmentsTask);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (hasCurrentAssetAssignmentTask.Result)
        {
            errors["id"] = ["This user cannot be deleted because they are the current assignee of an asset."];
        }
        else if (hasAssignmentsTask.Result)
        {
            errors["id"] = ["This user cannot be deleted because assignment history references them. Delete related assignments first."];
        }

        return errors;
    }
}
