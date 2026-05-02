using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class LifecycleEventDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("assetId")]
    public ObjectId AssetId { get; init; }

    [BsonElement("changes")]
    public IReadOnlyList<LifecycleEventChangeDocument> Changes { get; init; } = [];

    [BsonElement("eventType")]
    public string EventType { get; init; } = string.Empty;

    [BsonElement("notes")]
    public string Notes { get; init; } = string.Empty;

    [BsonElement("performedByUserId")]
    public ObjectId PerformedByUserId { get; init; }

    [BsonElement("timestamp")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; init; }
}
