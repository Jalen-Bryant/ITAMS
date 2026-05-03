using ITAMS.Api.Models;
using MongoDB.Bson;

namespace ITAMS.Api.Services;

internal static class MutationDocumentFactory
{
    public static AuditLogDocument CreateAuditLog(
        string action,
        ObjectId actorUserId,
        ObjectId entityId,
        string entityType,
        string? note,
        string ip,
        string userAgent,
        string result = "SUCCESS")
    {
        return new AuditLogDocument
        {
            Id = ObjectId.GenerateNewId(),
            Action = action,
            ActorUserId = actorUserId,
            Details = new AuditLogDetailDocument
            {
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                Result = result
            },
            EntityId = entityId,
            EntityType = entityType,
            Ip = ip,
            Timestamp = DateTime.UtcNow,
            UserAgent = userAgent
        };
    }

    public static LifecycleEventDocument CreateLifecycleEvent(
        ObjectId assetId,
        string eventType,
        ObjectId performedByUserId,
        IEnumerable<LifecycleEventChangeDocument> changes,
        string note)
    {
        return new LifecycleEventDocument
        {
            Id = ObjectId.GenerateNewId(),
            AssetId = assetId,
            Changes = changes.ToArray(),
            EventType = eventType,
            Notes = note,
            PerformedByUserId = performedByUserId,
            Timestamp = DateTime.UtcNow
        };
    }

    public static LifecycleEventChangeDocument CreateStringChange(
        string field,
        string? oldValue,
        string? newValue)
    {
        return new LifecycleEventChangeDocument
        {
            Field = field,
            OldValue = oldValue,
            NewValue = newValue is null ? BsonNull.Value : new BsonString(newValue)
        };
    }

    public static LifecycleEventChangeDocument CreateObjectIdChange(
        string field,
        ObjectId? oldValue,
        ObjectId? newValue)
    {
        return new LifecycleEventChangeDocument
        {
            Field = field,
            OldValue = oldValue?.ToString(),
            NewValue = newValue is null ? BsonNull.Value : new BsonObjectId(newValue.Value)
        };
    }
}
