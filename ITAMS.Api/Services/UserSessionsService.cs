using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class UserSessionsService
{
    private readonly IMongoCollection<UserSessionDocument> _sessionsCollection;

    public UserSessionsService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _sessionsCollection = database.GetCollection<UserSessionDocument>(mongoDbSettings.UserSessionsCollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var refreshTokenIndex = new CreateIndexModel<UserSessionDocument>(
            Builders<UserSessionDocument>.IndexKeys.Ascending(session => session.RefreshTokenHash),
            new CreateIndexOptions { Name = "refreshTokenHash_1", Unique = true });

        var userIndex = new CreateIndexModel<UserSessionDocument>(
            Builders<UserSessionDocument>.IndexKeys.Ascending(session => session.UserId),
            new CreateIndexOptions { Name = "userId_1" });

        var expiryIndex = new CreateIndexModel<UserSessionDocument>(
            Builders<UserSessionDocument>.IndexKeys.Ascending(session => session.ExpiresAt),
            new CreateIndexOptions { Name = "expiresAt_ttl", ExpireAfter = TimeSpan.Zero });

        await _sessionsCollection.Indexes.CreateManyAsync(
            [refreshTokenIndex, userIndex, expiryIndex],
            cancellationToken: cancellationToken);
    }

    public async Task<UserSessionDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) =>
        await _sessionsCollection
            .Find(session => session.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<UserSessionDocument?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken = default) =>
        await _sessionsCollection
            .Find(session => session.RefreshTokenHash == refreshTokenHash)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> IsActiveSessionAsync(
        ObjectId userId,
        ObjectId sessionId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<UserSessionDocument>.Filter.And(
            Builders<UserSessionDocument>.Filter.Eq(session => session.Id, sessionId),
            Builders<UserSessionDocument>.Filter.Eq(session => session.UserId, userId),
            Builders<UserSessionDocument>.Filter.Eq(session => session.RevokedAt, null),
            Builders<UserSessionDocument>.Filter.Gt(session => session.ExpiresAt, nowUtc));

        var count = await _sessionsCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public Task CreateAsync(UserSessionDocument session, CancellationToken cancellationToken = default) =>
        _sessionsCollection.InsertOneAsync(session, cancellationToken: cancellationToken);

    public async Task<bool> RevokeAsync(ObjectId sessionId, DateTime revokedAtUtc, CancellationToken cancellationToken = default)
    {
        var update = Builders<UserSessionDocument>.Update
            .Set(session => session.RevokedAt, revokedAtUtc)
            .Set(session => session.ExpiresAt, revokedAtUtc);

        var result = await _sessionsCollection.UpdateOneAsync(
            session => session.Id == sessionId && session.RevokedAt == null,
            update,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public Task RevokeAllForUserAsync(ObjectId userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default)
    {
        var update = Builders<UserSessionDocument>.Update
            .Set(session => session.RevokedAt, revokedAtUtc)
            .Set(session => session.ExpiresAt, revokedAtUtc);

        return _sessionsCollection.UpdateManyAsync(
            session => session.UserId == userId && session.RevokedAt == null,
            update,
            cancellationToken: cancellationToken);
    }
}
