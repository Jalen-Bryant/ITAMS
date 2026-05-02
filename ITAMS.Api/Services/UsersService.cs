using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class UsersService
{
    private static readonly (string FieldName, string IndexName)[] RequiredUniqueIndexes =
    [
        ("normalizedUsername", "normalizedUsername_1"),
        ("normalizedEmail", "normalizedEmail_1")
    ];

    private readonly IMongoCollection<UserDocument> _usersCollection;

    public UsersService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.UsersCollectionName);
    }

    public async Task<IReadOnlyList<UserDocument>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _usersCollection
            .Find(FilterDefinition<UserDocument>.Empty)
            .ToListAsync(cancellationToken);

    public async Task<UserDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) =>
        await _usersCollection
            .Find(user => user.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<UserDocument?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default) =>
        await _usersCollection
            .Find(user => user.NormalizedUsername == normalizedUsername)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<UserDocument?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        await _usersCollection
            .Find(user => user.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<UserDocument?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var normalizedIdentifier = NormalizeValue(identifier);
        return await _usersCollection
            .Find(user =>
                user.NormalizedUsername == normalizedIdentifier ||
                user.NormalizedEmail == normalizedIdentifier)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default) =>
        await _usersCollection.CountDocumentsAsync(FilterDefinition<UserDocument>.Empty, cancellationToken: cancellationToken);

    public async Task<long> CountUsersWithPasswordHashAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<UserDocument>.Filter.And(
            Builders<UserDocument>.Filter.Exists(user => user.PasswordHash, true),
            Builders<UserDocument>.Filter.Ne(user => user.PasswordHash, null),
            Builders<UserDocument>.Filter.Ne(user => user.PasswordHash, string.Empty));

        return await _usersCollection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task EnsureAuthIndexesAsync(CancellationToken cancellationToken = default)
    {
        await BackfillNormalizedIdentityFieldsAsync(cancellationToken);

        var existingIndexesCursor = await _usersCollection.Indexes.ListAsync(cancellationToken);
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
                    $"The users collection already has a non-unique index on '{fieldName}'. " +
                    "Drop or replace that index with a unique index before starting the API.");
            }
        }
    }

    public async Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<UserDocument>.Filter.Eq(user => user.Id, id);
        var count = await _usersCollection.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public Task CreateAsync(UserDocument user, CancellationToken cancellationToken = default) =>
        _usersCollection.InsertOneAsync(user, cancellationToken: cancellationToken);

    public async Task<bool> ReplaceAsync(UserDocument user, CancellationToken cancellationToken = default)
    {
        var result = await _usersCollection.ReplaceOneAsync(
            existingUser => existingUser.Id == user.Id,
            user,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var result = await _usersCollection.DeleteOneAsync(
            user => user.Id == id,
            cancellationToken);

        return result.DeletedCount > 0;
    }

    public static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey ||
        exception.WriteError?.Code is 11000 or 11001;

    public static string GetDuplicateKeyMessage(MongoWriteException exception)
    {
        var message = exception.WriteError?.Message ?? string.Empty;

        if (message.Contains("normalizedUsername", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("username", StringComparison.OrdinalIgnoreCase))
        {
            return "A user with the same username already exists.";
        }

        if (message.Contains("normalizedEmail", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return "A user with the same email already exists.";
        }

        return "A user with the same username or email already exists.";
    }

    public static string NormalizeValue(string value) => value.Trim().ToUpperInvariant();

    private async Task BackfillNormalizedIdentityFieldsAsync(CancellationToken cancellationToken)
    {
        var users = await _usersCollection
            .Find(FilterDefinition<UserDocument>.Empty)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var normalizedUsername = NormalizeValue(user.Username);
            var normalizedEmail = NormalizeValue(user.Email);

            if (string.Equals(user.NormalizedUsername, normalizedUsername, StringComparison.Ordinal) &&
                string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            {
                continue;
            }

            var update = Builders<UserDocument>.Update
                .Set(existingUser => existingUser.NormalizedUsername, normalizedUsername)
                .Set(existingUser => existingUser.NormalizedEmail, normalizedEmail);

            await _usersCollection.UpdateOneAsync(
                existingUser => existingUser.Id == user.Id,
                update,
                cancellationToken: cancellationToken);
        }
    }

    private Task CreateUniqueIndexAsync(
        string fieldName,
        string indexName,
        CancellationToken cancellationToken)
    {
        var keys = Builders<UserDocument>.IndexKeys.Ascending(fieldName);
        var index = new CreateIndexModel<UserDocument>(
            keys,
            new CreateIndexOptions { Name = indexName, Unique = true });

        return _usersCollection.Indexes.CreateOneAsync(index, cancellationToken: cancellationToken);
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
}
