using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class AssetMutationService
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<AssetDocument> _assetsCollection;
    private readonly IMongoCollection<AuditLogDocument> _auditLogsCollection;
    private readonly IMongoCollection<LifecycleEventDocument> _lifecycleEventsCollection;

    public AssetMutationService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        _mongoClient = mongoClient;
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _assetsCollection = database.GetCollection<AssetDocument>(mongoDbSettings.AssetsCollectionName);
        _auditLogsCollection = database.GetCollection<AuditLogDocument>(mongoDbSettings.AuditLogsCollectionName);
        _lifecycleEventsCollection = database.GetCollection<LifecycleEventDocument>(mongoDbSettings.LifecycleEventsCollectionName);
    }

    public async Task CreateAsync(
        AssetDocument asset,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        await _assetsCollection.InsertOneAsync(session, asset, cancellationToken: cancellationToken);

        var auditLog = MutationDocumentFactory.CreateAuditLog(
            "CREATE",
            actorUserId,
            asset.Id,
            "Asset",
            asset.Notes ?? "Asset created via API.",
            ip,
            userAgent);
        await _auditLogsCollection.InsertOneAsync(session, auditLog, cancellationToken: cancellationToken);

        var lifecycleEvent = MutationDocumentFactory.CreateLifecycleEvent(
            asset.Id,
            "Registered",
            actorUserId,
            BuildRegisteredChanges(asset),
            asset.Notes ?? "Asset registered via API.");
        await _lifecycleEventsCollection.InsertOneAsync(session, lifecycleEvent, cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
    }

    public async Task<MutationResult<AssetDocument>> ReplaceAsync(
        AssetDocument updatedAsset,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var existingAsset = await _assetsCollection
            .Find(session, asset => asset.Id == updatedAsset.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingAsset is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssetDocument> { NotFound = true };
        }

        var result = await _assetsCollection.ReplaceOneAsync(
            session,
            asset => asset.Id == updatedAsset.Id,
            updatedAsset,
            cancellationToken: cancellationToken);
        if (result.MatchedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssetDocument> { NotFound = true };
        }

        var auditLog = MutationDocumentFactory.CreateAuditLog(
            "UPDATE",
            actorUserId,
            updatedAsset.Id,
            "Asset",
            updatedAsset.Notes ?? "Asset updated via API.",
            ip,
            userAgent);
        await _auditLogsCollection.InsertOneAsync(session, auditLog, cancellationToken: cancellationToken);

        var changes = BuildUpdateChanges(existingAsset, updatedAsset);
        // Only persist lifecycle history when the replacement actually changed tracked business fields.
        if (changes.Count > 0)
        {
            var lifecycleEvent = MutationDocumentFactory.CreateLifecycleEvent(
                updatedAsset.Id,
                ResolveAssetLifecycleEventType(changes),
                actorUserId,
                changes,
                updatedAsset.Notes ?? "Asset updated via API.");
            await _lifecycleEventsCollection.InsertOneAsync(session, lifecycleEvent, cancellationToken: cancellationToken);
        }

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<AssetDocument> { Value = updatedAsset };
    }

    public async Task<MutationResult<AssetDocument>> DeleteAsync(
        ObjectId assetId,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var existingAsset = await _assetsCollection
            .Find(session, asset => asset.Id == assetId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingAsset is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssetDocument> { NotFound = true };
        }

        var deleteFilter = Builders<AssetDocument>.Filter.Eq(asset => asset.Id, assetId);
        var deleteResult = await _assetsCollection.DeleteOneAsync(
            session,
            deleteFilter,
            cancellationToken: cancellationToken);
        if (deleteResult.DeletedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<AssetDocument> { NotFound = true };
        }

        var auditLog = MutationDocumentFactory.CreateAuditLog(
            "DELETE",
            actorUserId,
            assetId,
            "Asset",
            existingAsset.Notes ?? "Asset deleted via API.",
            ip,
            userAgent);
        await _auditLogsCollection.InsertOneAsync(session, auditLog, cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<AssetDocument> { Value = existingAsset };
    }

    private static IReadOnlyList<LifecycleEventChangeDocument> BuildRegisteredChanges(AssetDocument asset) =>
    [
        MutationDocumentFactory.CreateStringChange("assetTag", null, asset.AssetTag),
        MutationDocumentFactory.CreateStringChange("status", null, asset.Status),
        MutationDocumentFactory.CreateStringChange("department", null, asset.Department),
        MutationDocumentFactory.CreateStringChange("location", null, asset.Location)
    ];

    private static List<LifecycleEventChangeDocument> BuildUpdateChanges(
        AssetDocument existingAsset,
        AssetDocument updatedAsset)
    {
        var changes = new List<LifecycleEventChangeDocument>();

        AddStringChange(changes, "assetTag", existingAsset.AssetTag, updatedAsset.AssetTag);
        AddStringChange(changes, "serialNumber", existingAsset.SerialNumber, updatedAsset.SerialNumber);
        AddStringChange(changes, "type", existingAsset.Type, updatedAsset.Type);
        AddStringChange(changes, "manufacturer", existingAsset.Manufacturer, updatedAsset.Manufacturer);
        AddStringChange(changes, "model", existingAsset.Model, updatedAsset.Model);
        AddStringChange(changes, "status", existingAsset.Status, updatedAsset.Status);
        AddStringChange(changes, "department", existingAsset.Department, updatedAsset.Department);
        AddStringChange(changes, "location", existingAsset.Location, updatedAsset.Location);
        AddStringChange(changes, "purchaseDate", FormatDate(existingAsset.PurchaseDate), FormatDate(updatedAsset.PurchaseDate));
        AddStringChange(changes, "warrantyEndDate", FormatDate(existingAsset.WarrantyEndDate), FormatDate(updatedAsset.WarrantyEndDate));
        AddStringChange(changes, "endOfLifeDate", FormatDate(existingAsset.EndOfLifeDate), FormatDate(updatedAsset.EndOfLifeDate));
        AddStringChange(changes, "notes", existingAsset.Notes, updatedAsset.Notes);

        return changes;
    }

    private static string ResolveAssetLifecycleEventType(IReadOnlyCollection<LifecycleEventChangeDocument> changes)
    {
        var changedFields = changes.Select(change => change.Field).ToArray();

        if (changedFields.Length == 1 && changedFields[0] == "status")
        {
            return "StatusChanged";
        }

        if (changedFields.Length == 1 && changedFields[0] == "location")
        {
            return "LocationUpdated";
        }

        return "Updated";
    }

    private static void AddStringChange(
        ICollection<LifecycleEventChangeDocument> changes,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add(MutationDocumentFactory.CreateStringChange(field, oldValue, newValue));
    }

    private static string FormatDate(DateTime value) =>
        EnsureUtc(value).ToString("O");

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
