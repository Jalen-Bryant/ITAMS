using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class LifecycleEventsService
{
    private readonly IMongoCollection<LifecycleEventDocument> _lifecycleEventsCollection;

    public LifecycleEventsService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _lifecycleEventsCollection = database.GetCollection<LifecycleEventDocument>(mongoDbSettings.LifecycleEventsCollectionName);
    }

    public async Task<IReadOnlyList<LifecycleEventDocument>> GetAllAsync(
        PageRequest pageRequest,
        CancellationToken cancellationToken = default) =>
        await _lifecycleEventsCollection
            .Find(FilterDefinition<LifecycleEventDocument>.Empty)
            .SortByDescending(lifecycleEvent => lifecycleEvent.Timestamp)
            .Skip(pageRequest.Offset)
            .Limit(pageRequest.Limit)
            .ToListAsync(cancellationToken);

    public async Task<LifecycleEventDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) =>
        await _lifecycleEventsCollection
            .Find(lifecycleEvent => lifecycleEvent.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<LifecycleEventDocument>.Filter.Eq(lifecycleEvent => lifecycleEvent.Id, id);
        var count = await _lifecycleEventsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public Task CreateAsync(LifecycleEventDocument lifecycleEvent, CancellationToken cancellationToken = default) =>
        _lifecycleEventsCollection.InsertOneAsync(lifecycleEvent, cancellationToken: cancellationToken);

    public async Task<bool> ReplaceAsync(LifecycleEventDocument lifecycleEvent, CancellationToken cancellationToken = default)
    {
        var result = await _lifecycleEventsCollection.ReplaceOneAsync(
            existingLifecycleEvent => existingLifecycleEvent.Id == lifecycleEvent.Id,
            lifecycleEvent,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var result = await _lifecycleEventsCollection.DeleteOneAsync(
            lifecycleEvent => lifecycleEvent.Id == id,
            cancellationToken);

        return result.DeletedCount > 0;
    }
}
