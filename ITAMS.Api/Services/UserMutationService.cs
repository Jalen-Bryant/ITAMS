using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class UserMutationService
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<UserDocument> _usersCollection;
    private readonly IMongoCollection<AuditLogDocument> _auditLogsCollection;

    public UserMutationService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        _mongoClient = mongoClient;
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.UsersCollectionName);
        _auditLogsCollection = database.GetCollection<AuditLogDocument>(mongoDbSettings.AuditLogsCollectionName);
    }

    public async Task CreateAsync(
        UserDocument user,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        await _usersCollection.InsertOneAsync(session, user, cancellationToken: cancellationToken);

        var auditLog = MutationDocumentFactory.CreateAuditLog(
            "CREATE",
            actorUserId,
            user.Id,
            "User",
            $"User {user.Username} created via API.",
            ip,
            userAgent);
        await _auditLogsCollection.InsertOneAsync(session, auditLog, cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
    }

    public async Task<MutationResult<UserDocument>> ReplaceAsync(
        UserDocument updatedUser,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var existingUser = await _usersCollection
            .Find(session, user => user.Id == updatedUser.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingUser is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<UserDocument> { NotFound = true };
        }

        var replaceResult = await _usersCollection.ReplaceOneAsync(
            session,
            user => user.Id == updatedUser.Id,
            updatedUser,
            cancellationToken: cancellationToken);
        if (replaceResult.MatchedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<UserDocument> { NotFound = true };
        }

        var auditLog = MutationDocumentFactory.CreateAuditLog(
            "UPDATE",
            actorUserId,
            updatedUser.Id,
            "User",
            $"User {updatedUser.Username} updated via API.",
            ip,
            userAgent);
        await _auditLogsCollection.InsertOneAsync(session, auditLog, cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<UserDocument> { Value = updatedUser };
    }

    public async Task<MutationResult<UserDocument>> DeleteAsync(
        ObjectId userId,
        ObjectId actorUserId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var existingUser = await _usersCollection
            .Find(session, user => user.Id == userId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingUser is null)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<UserDocument> { NotFound = true };
        }

        var deleteFilter = Builders<UserDocument>.Filter.Eq(user => user.Id, userId);
        var deleteResult = await _usersCollection.DeleteOneAsync(
            session,
            deleteFilter,
            cancellationToken: cancellationToken);
        if (deleteResult.DeletedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new MutationResult<UserDocument> { NotFound = true };
        }

        var auditLog = MutationDocumentFactory.CreateAuditLog(
            "DELETE",
            actorUserId,
            userId,
            "User",
            $"User {existingUser.Username} deleted via API.",
            ip,
            userAgent);
        await _auditLogsCollection.InsertOneAsync(session, auditLog, cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new MutationResult<UserDocument> { Value = existingUser };
    }
}
