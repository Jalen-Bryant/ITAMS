using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class AssignmentsService
{
    private readonly IMongoCollection<AssignmentDocument> _assignmentsCollection;

    public AssignmentsService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _assignmentsCollection = database.GetCollection<AssignmentDocument>(mongoDbSettings.AssignmentsCollectionName);
    }

    public async Task<IReadOnlyList<AssignmentDocument>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _assignmentsCollection
            .Find(FilterDefinition<AssignmentDocument>.Empty)
            .ToListAsync(cancellationToken);

    public async Task<AssignmentDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) =>
        await _assignmentsCollection
            .Find(assignment => assignment.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.Id, id);
        var count = await _assignmentsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public async Task<bool> HasAnyForAssetAsync(ObjectId assetId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.AssetId, assetId);
        var count = await _assignmentsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public async Task<bool> HasAnyForUserAsync(ObjectId userId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<AssignmentDocument>.Filter.Or(
            Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.UserId, userId),
            Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.AssignedByUserId, userId));
        var count = await _assignmentsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public Task CreateAsync(AssignmentDocument assignment, CancellationToken cancellationToken = default) =>
        _assignmentsCollection.InsertOneAsync(assignment, cancellationToken: cancellationToken);

    public async Task<bool> ReplaceAsync(AssignmentDocument assignment, CancellationToken cancellationToken = default)
    {
        var result = await _assignmentsCollection.ReplaceOneAsync(
            existingAssignment => existingAssignment.Id == assignment.Id,
            assignment,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var result = await _assignmentsCollection.DeleteOneAsync(
            assignment => assignment.Id == id,
            cancellationToken);

        return result.DeletedCount > 0;
    }

    public async Task<AssignmentDocument?> GetCurrentByAssetIdAsync(
        ObjectId assetId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<AssignmentDocument>.Filter.And(
            Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.AssetId, assetId),
            Builders<AssignmentDocument>.Filter.Lte(assignment => assignment.StartDate, asOfUtc),
            Builders<AssignmentDocument>.Filter.Or(
                Builders<AssignmentDocument>.Filter.Eq(assignment => assignment.EndDate, null),
                Builders<AssignmentDocument>.Filter.Gt(assignment => assignment.EndDate, asOfUtc)));

        return await _assignmentsCollection
            .Find(filter)
            .SortByDescending(assignment => assignment.StartDate)
            .ThenByDescending(assignment => assignment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
