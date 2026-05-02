using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class AssignmentMutationService
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<AssetDocument> _assetsCollection;
    private readonly IMongoCollection<AssignmentDocument> _assignmentsCollection;
    private readonly IMongoCollection<AuditLogDocument> _auditLogsCollection;
    private readonly IMongoCollection<LifecycleEventDocument> _lifecycleEventsCollection;
    private readonly IMongoCollection<UserDocument> _usersCollection;

    public AssignmentMutationService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        _mongoClient = mongoClient;
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _assetsCollection = database.GetCollection<AssetDocument>(mongoDbSettings.AssetsCollectionName);
        _assignmentsCollection = database.GetCollection<AssignmentDocument>(mongoDbSettings.AssignmentsCollectionName);
        _auditLogsCollection = database.GetCollection<AuditLogDocument>(mongoDbSettings.AuditLogsCollectionName);
        _lifecycleEventsCollection = database.GetCollection<LifecycleEventDocument>(mongoDbSettings.LifecycleEventsCollectionName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.UsersCollectionName);
    }

    public async Task<MutationResult<AssignmentDocument>> CreateAsync(
        AssignmentDocument assignment,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var validationErrors = await ValidateMutationAsync(session, assignment, null, cancellationToken);
        if (validationErrors.Count > 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssignmentDocument> { Errors = validationErrors };
        }

        // Capture asset state before the write so we can derive both currentAssignment synchronization and lifecycle deltas afterward.
        var beforeAssets = await LoadAssetStatesAsync(session, [assignment.AssetId], cancellationToken);

        await _assignmentsCollection.InsertOneAsync(session, assignment, cancellationToken: cancellationToken);

        var afterAssets = await SyncAssetStatesAsync(session, beforeAssets.Keys, cancellationToken);

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "CREATE",
                actorUserId,
                assignment.Id,
                "Assignment",
                assignment.Notes ?? "Assignment created via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await CreateLifecycleEventsForStateChangesAsync(
            session,
            beforeAssets,
            afterAssets,
            actorUserId,
            assignment.Notes ?? "Assignment created via API.",
            cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<AssignmentDocument> { Value = assignment };
    }

    public async Task<MutationResult<AssignmentDocument>> ReplaceAsync(
        AssignmentDocument updatedAssignment,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var existingAssignment = await _assignmentsCollection
            .Find(session, assignment => assignment.Id == updatedAssignment.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingAssignment is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssignmentDocument> { NotFound = true };
        }

        var validationErrors = await ValidateMutationAsync(session, updatedAssignment, updatedAssignment.Id, cancellationToken);
        if (validationErrors.Count > 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssignmentDocument> { Errors = validationErrors };
        }

        var affectedAssetIds = new[] { existingAssignment.AssetId, updatedAssignment.AssetId }.Distinct().ToArray();
        var beforeAssets = await LoadAssetStatesAsync(session, affectedAssetIds, cancellationToken);

        var replaceResult = await _assignmentsCollection.ReplaceOneAsync(
            session,
            assignment => assignment.Id == updatedAssignment.Id,
            updatedAssignment,
            cancellationToken: cancellationToken);
        if (replaceResult.MatchedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssignmentDocument> { NotFound = true };
        }

        var afterAssets = await SyncAssetStatesAsync(session, beforeAssets.Keys, cancellationToken);

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "UPDATE",
                actorUserId,
                updatedAssignment.Id,
                "Assignment",
                updatedAssignment.Notes ?? "Assignment updated via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await CreateLifecycleEventsForStateChangesAsync(
            session,
            beforeAssets,
            afterAssets,
            actorUserId,
            updatedAssignment.Notes ?? "Assignment updated via API.",
            cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<AssignmentDocument> { Value = updatedAssignment };
    }

    public async Task<MutationResult<AssignmentDocument>> DeleteAsync(
        ObjectId assignmentId,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var existingAssignment = await _assignmentsCollection
            .Find(session, assignment => assignment.Id == assignmentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingAssignment is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssignmentDocument> { NotFound = true };
        }

        var beforeAssets = await LoadAssetStatesAsync(session, [existingAssignment.AssetId], cancellationToken);

        var deleteFilter = Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.Id, assignmentId);
        var deleteResult = await _assignmentsCollection.DeleteOneAsync(
            session,
            deleteFilter,
            cancellationToken: cancellationToken);
        if (deleteResult.DeletedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssignmentDocument> { NotFound = true };
        }

        var afterAssets = await SyncAssetStatesAsync(session, beforeAssets.Keys, cancellationToken);

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "DELETE",
                actorUserId,
                assignmentId,
                "Assignment",
                existingAssignment.Notes ?? "Assignment deleted via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await CreateLifecycleEventsForStateChangesAsync(
            session,
            beforeAssets,
            afterAssets,
            actorUserId,
            existingAssignment.Notes ?? "Assignment deleted via API.",
            cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<AssignmentDocument> { Value = existingAssignment };
    }

    private async Task<Dictionary<string, string[]>> ValidateMutationAsync(
        IClientSessionHandle session,
        AssignmentDocument assignment,
        ObjectId? excludeAssignmentId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var assetExistsTask = ExistsAsync(session, _assetsCollection, assignment.AssetId, cancellationToken);
        var userExistsTask = ExistsAsync(session, _usersCollection, assignment.UserId, cancellationToken);
        var assignedByUserExistsTask = ExistsAsync(session, _usersCollection, assignment.AssignedByUserId, cancellationToken);
        var overlappingAssignmentTask = HasOverlappingAssignmentAsync(session, assignment, excludeAssignmentId, cancellationToken);

        await Task.WhenAll(assetExistsTask, userExistsTask, assignedByUserExistsTask, overlappingAssignmentTask);

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

        if (overlappingAssignmentTask.Result)
        {
            errors["startDate"] = ["This assignment overlaps an existing assignment for the same asset."];
        }

        return errors;
    }

    private async Task<bool> ExistsAsync<TDocument>(
        IClientSessionHandle session,
        IMongoCollection<TDocument> collection,
        ObjectId id,
        CancellationToken cancellationToken) where TDocument : class
    {
        var filter = Builders<TDocument>.Filter.Eq("_id", id);
        var count = await collection.CountDocumentsAsync(
            session,
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    private async Task<bool> HasOverlappingAssignmentAsync(
        IClientSessionHandle session,
        AssignmentDocument assignment,
        ObjectId? excludeAssignmentId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<AssignmentDocument>.Filter.Eq(existing => existing.AssetId, assignment.AssetId) &
                     Builders<AssignmentDocument>.Filter.Or(
                         Builders<AssignmentDocument>.Filter.Eq(existing => existing.EndDate, null),
                         Builders<AssignmentDocument>.Filter.Gt(existing => existing.EndDate, assignment.StartDate));

        if (assignment.EndDate is not null)
        {
            filter &= Builders<AssignmentDocument>.Filter.Lt(existing => existing.StartDate, assignment.EndDate.Value);
        }

        if (excludeAssignmentId is not null)
        {
            filter &= Builders<AssignmentDocument>.Filter.Ne(existing => existing.Id, excludeAssignmentId.Value);
        }

        var count = await _assignmentsCollection.CountDocumentsAsync(
            session,
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    private async Task<Dictionary<ObjectId, AssetDocument>> LoadAssetStatesAsync(
        IClientSessionHandle session,
        IEnumerable<ObjectId> assetIds,
        CancellationToken cancellationToken)
    {
        var assetIdList = assetIds.Distinct().ToArray();
        var filter = Builders<AssetDocument>.Filter.In(asset => asset.Id, assetIdList);
        var assets = await _assetsCollection
            .Find(session, filter)
            .ToListAsync(cancellationToken);

        return assets.ToDictionary(asset => asset.Id, asset => asset);
    }

    private async Task<Dictionary<ObjectId, AssetDocument>> SyncAssetStatesAsync(
        IClientSessionHandle session,
        IEnumerable<ObjectId> assetIds,
        CancellationToken cancellationToken)
    {
        var syncedAssets = new Dictionary<ObjectId, AssetDocument>();

        foreach (var assetId in assetIds.Distinct())
        {
            var syncedAsset = await SyncAssetStateAsync(session, assetId, cancellationToken);
            syncedAssets[assetId] = syncedAsset;
        }

        return syncedAssets;
    }

    private async Task<AssetDocument> SyncAssetStateAsync(
        IClientSessionHandle session,
        ObjectId assetId,
        CancellationToken cancellationToken)
    {
        var asset = await _assetsCollection
            .Find(session, existingAsset => existingAsset.Id == assetId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Asset '{assetId}' no longer exists during assignment synchronization.");

        var currentAssignment = await GetCurrentAssignmentAsync(session, assetId, cancellationToken);
        var synchronizedStatus = SynchronizeStatus(asset.Status, currentAssignment);

        var update = Builders<AssetDocument>.Update
            .Set(existingAsset => existingAsset.CurrentAssignment, currentAssignment)
            .Set(existingAsset => existingAsset.Status, synchronizedStatus)
            .Set(existingAsset => existingAsset.UpdatedAt, DateTime.UtcNow);

        var filter = Builders<AssetDocument>.Filter.Eq(existingAsset => existingAsset.Id, assetId);
        var options = new FindOneAndUpdateOptions<AssetDocument, AssetDocument>
        {
            ReturnDocument = ReturnDocument.After
        };

        return await _assetsCollection.FindOneAndUpdateAsync(
                   session,
                   filter,
                   update,
                   options,
                   cancellationToken)
               ?? throw new InvalidOperationException($"Asset '{assetId}' no longer exists during assignment synchronization.");
    }

    private async Task<AssetAssignmentDocument?> GetCurrentAssignmentAsync(
        IClientSessionHandle session,
        ObjectId assetId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<AssignmentDocument>.Filter.And(
            Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.AssetId, assetId),
            Builders<AssignmentDocument>.Filter.Lte(assignment => assignment.StartDate, now),
            Builders<AssignmentDocument>.Filter.Or(
                Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.EndDate, null),
                Builders<AssignmentDocument>.Filter.Gt(assignment => assignment.EndDate, now)));

        var currentAssignment = await _assignmentsCollection
            .Find(session, filter)
            .SortByDescending(assignment => assignment.StartDate)
            .ThenByDescending(assignment => assignment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // If overlapping active assignments ever exist, the latest effective assignment wins for the asset snapshot.
        return currentAssignment is null
            ? null
            : new AssetAssignmentDocument
            {
                UserId = currentAssignment.UserId,
                AssignedOn = EnsureUtc(currentAssignment.StartDate)
            };
    }

    private async Task CreateLifecycleEventsForStateChangesAsync(
        IClientSessionHandle session,
        IReadOnlyDictionary<ObjectId, AssetDocument> beforeAssets,
        IReadOnlyDictionary<ObjectId, AssetDocument> afterAssets,
        ObjectId actorUserId,
        string note,
        CancellationToken cancellationToken)
    {
        foreach (var (assetId, beforeAsset) in beforeAssets)
        {
            if (!afterAssets.TryGetValue(assetId, out var afterAsset))
            {
                continue;
            }

            var changes = BuildAssignmentStateChanges(beforeAsset, afterAsset);
            if (changes.Count == 0)
            {
                continue;
            }

            var lifecycleEvent = MutationDocumentFactory.CreateLifecycleEvent(
                assetId,
                ResolveAssignmentLifecycleEventType(beforeAsset, afterAsset),
                actorUserId,
                changes,
                note);

            await _lifecycleEventsCollection.InsertOneAsync(session, lifecycleEvent, cancellationToken: cancellationToken);
        }
    }

    private static List<LifecycleEventChangeDocument> BuildAssignmentStateChanges(
        AssetDocument beforeAsset,
        AssetDocument afterAsset)
    {
        var changes = new List<LifecycleEventChangeDocument>();

        var beforeUserId = beforeAsset.CurrentAssignment?.UserId;
        var afterUserId = afterAsset.CurrentAssignment?.UserId;
        if (beforeUserId != afterUserId)
        {
            changes.Add(MutationDocumentFactory.CreateObjectIdChange(
                "currentAssignment.userId",
                beforeUserId,
                afterUserId));
        }

        var beforeAssignedOn = beforeAsset.CurrentAssignment is null
            ? null
            : EnsureUtc(beforeAsset.CurrentAssignment.AssignedOn).ToString("O");
        var afterAssignedOn = afterAsset.CurrentAssignment is null
            ? null
            : EnsureUtc(afterAsset.CurrentAssignment.AssignedOn).ToString("O");
        if (!string.Equals(beforeAssignedOn, afterAssignedOn, StringComparison.Ordinal))
        {
            changes.Add(MutationDocumentFactory.CreateStringChange(
                "currentAssignment.assignedOn",
                beforeAssignedOn,
                afterAssignedOn));
        }

        if (!string.Equals(beforeAsset.Status, afterAsset.Status, StringComparison.Ordinal))
        {
            changes.Add(MutationDocumentFactory.CreateStringChange("status", beforeAsset.Status, afterAsset.Status));
        }

        return changes;
    }

    private static string ResolveAssignmentLifecycleEventType(AssetDocument beforeAsset, AssetDocument afterAsset)
    {
        if (beforeAsset.CurrentAssignment is null && afterAsset.CurrentAssignment is not null)
        {
            return "Assigned";
        }

        if (beforeAsset.CurrentAssignment is not null && afterAsset.CurrentAssignment is null)
        {
            return "Unassigned";
        }

        return "Updated";
    }

    private static string SynchronizeStatus(string currentStatus, AssetAssignmentDocument? currentAssignment)
    {
        if (currentAssignment is not null && string.Equals(currentStatus, "InStock", StringComparison.OrdinalIgnoreCase))
        {
            return "Assigned";
        }

        if (currentAssignment is null && string.Equals(currentStatus, "Assigned", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }

        return currentStatus;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
