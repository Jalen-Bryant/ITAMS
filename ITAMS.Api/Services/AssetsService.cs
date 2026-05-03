using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class AssetsService
{
    private static readonly (string FieldName, string IndexName)[] RequiredUniqueIndexes =
    [
        ("assetTag", "assetTag_1"),
        ("serialNumber", "serialNumber_1")
    ];

    private readonly IMongoCollection<AssetDocument> _assetsCollection;

    public AssetsService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _assetsCollection = database.GetCollection<AssetDocument>(mongoDbSettings.AssetsCollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var existingIndexesCursor = await _assetsCollection.Indexes.ListAsync(cancellationToken);
        var existingIndexes = await existingIndexesCursor.ToListAsync(cancellationToken);

        foreach (var (fieldName, indexName) in RequiredUniqueIndexes)
        {
            var existingIndex = existingIndexes.FirstOrDefault(index => IsSingleFieldAscendingIndex(index, fieldName));
            if (existingIndex is null)
            {
                await CreateUniqueIndexAsync(fieldName, indexName, cancellationToken);
                continue;
            }

            var isUnique = existingIndex.GetValue("unique", false).ToBoolean();
            if (!isUnique)
            {
                throw new InvalidOperationException(
                    $"The assets collection already has a non-unique index on '{fieldName}'. " +
                    "Drop or replace that index with a unique index before starting the API.");
            }
        }
    }

    private Task CreateUniqueIndexAsync(
        string fieldName,
        string indexName,
        CancellationToken cancellationToken)
    {
        var keys = Builders<AssetDocument>.IndexKeys.Ascending(fieldName);
        var index = new CreateIndexModel<AssetDocument>(
            keys,
            new CreateIndexOptions { Name = indexName, Unique = true });

        return _assetsCollection.Indexes.CreateOneAsync(index, cancellationToken: cancellationToken);
    }

    private static bool IsSingleFieldAscendingIndex(BsonDocument index, string fieldName)
    {
        if (!index.TryGetValue("key", out var keyValue) || !keyValue.IsBsonDocument)
        {
            return false;
        }

        var keyDocument = keyValue.AsBsonDocument;
        return keyDocument.ElementCount == 1 &&
               keyDocument.TryGetValue(fieldName, out var sortOrder) &&
               sortOrder.ToInt32() == 1;
    }

    public async Task<IReadOnlyList<AssetDocument>> GetAllAsync(
        PageRequest pageRequest,
        CancellationToken cancellationToken = default) =>
        await _assetsCollection
            .Find(FilterDefinition<AssetDocument>.Empty)
            .SortBy(asset => asset.AssetTag)
            .Skip(pageRequest.Offset)
            .Limit(pageRequest.Limit)
            .ToListAsync(cancellationToken);

    public async Task<AssetDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) =>
        await _assetsCollection
            .Find(asset => asset.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<AssetDocument>.Filter.Eq(asset => asset.Id, id);
        var count = await _assetsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public async Task<bool> HasCurrentAssignmentForUserAsync(ObjectId userId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<AssetDocument>.Filter.Eq("currentAssignment.userId", userId);
        var count = await _assetsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public Task CreateAsync(AssetDocument asset, CancellationToken cancellationToken = default) =>
        _assetsCollection.InsertOneAsync(asset, cancellationToken: cancellationToken);

    public async Task<bool> ReplaceAsync(AssetDocument asset, CancellationToken cancellationToken = default)
    {
        var result = await _assetsCollection.ReplaceOneAsync(
            existingAsset => existingAsset.Id == asset.Id,
            asset,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> SetCurrentAssignmentAsync(
        ObjectId assetId,
        AssetAssignmentDocument? currentAssignment,
        CancellationToken cancellationToken = default)
    {
        var update = Builders<AssetDocument>.Update
            .Set(asset => asset.CurrentAssignment, currentAssignment)
            .Set(asset => asset.UpdatedAt, DateTime.UtcNow);
        var result = await _assetsCollection.UpdateOneAsync(
            asset => asset.Id == assetId,
            update,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var result = await _assetsCollection.DeleteOneAsync(
            asset => asset.Id == id,
            cancellationToken);

        return result.DeletedCount > 0;
    }

    public static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey ||
        exception.WriteError?.Code is 11000 or 11001;

    public static string GetDuplicateKeyMessage(MongoWriteException exception)
    {
        var message = exception.WriteError?.Message ?? string.Empty;

        if (message.Contains("assetTag", StringComparison.OrdinalIgnoreCase))
        {
            return "An asset with the same assetTag already exists.";
        }

        if (message.Contains("serialNumber", StringComparison.OrdinalIgnoreCase))
        {
            return "An asset with the same serialNumber already exists.";
        }

        return "An asset with the same assetTag or serialNumber already exists.";
    }
}
