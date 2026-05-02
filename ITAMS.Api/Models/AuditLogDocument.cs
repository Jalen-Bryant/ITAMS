using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class AuditLogDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("action")]
    public string Action { get; init; } = string.Empty;

    [BsonElement("actorUserId")]
    public ObjectId ActorUserId { get; init; }

    [BsonElement("details")]
    public AuditLogDetailDocument? Details { get; init; }

    [BsonElement("entityId")]
    public ObjectId EntityId { get; init; }

    [BsonElement("entityType")]
    public string EntityType { get; init; } = string.Empty;

    [BsonElement("ip")]
    public string Ip { get; init; } = string.Empty;

    [BsonElement("timestamp")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; init; }

    [BsonElement("userAgent")]
    public string UserAgent { get; init; } = string.Empty;
}
