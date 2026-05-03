using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class AuditLogsService
{
    private readonly IMongoCollection<AuditLogDocument> _auditLogsCollection;

    public AuditLogsService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _auditLogsCollection = database.GetCollection<AuditLogDocument>(mongoDbSettings.AuditLogsCollectionName);
    }

    public async Task<IReadOnlyList<AuditLogDocument>> GetAllAsync(
        PageRequest pageRequest,
        CancellationToken cancellationToken = default) =>
        await _auditLogsCollection
            .Find(FilterDefinition<AuditLogDocument>.Empty)
            .SortByDescending(auditLog => auditLog.Timestamp)
            .Skip(pageRequest.Offset)
            .Limit(pageRequest.Limit)
            .ToListAsync(cancellationToken);

    public async Task<AuditLogDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) =>
        await _auditLogsCollection
            .Find(auditLog => auditLog.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task CreateAsync(AuditLogDocument auditLog, CancellationToken cancellationToken = default) =>
        _auditLogsCollection.InsertOneAsync(auditLog, cancellationToken: cancellationToken);

    public async Task<bool> ReplaceAsync(AuditLogDocument auditLog, CancellationToken cancellationToken = default)
    {
        var result = await _auditLogsCollection.ReplaceOneAsync(
            existingAuditLog => existingAuditLog.Id == auditLog.Id,
            auditLog,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        var result = await _auditLogsCollection.DeleteOneAsync(
            auditLog => auditLog.Id == id,
            cancellationToken);

        return result.DeletedCount > 0;
    }
}
