using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class AssignmentDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("assetId")]
    public ObjectId AssetId { get; init; }

    [BsonElement("userId")]
    public ObjectId UserId { get; init; }

    [BsonElement("assignedByUserId")]
    public ObjectId AssignedByUserId { get; init; }

    [BsonElement("startDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime StartDate { get; init; }

    [BsonElement("endDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EndDate { get; init; }

    [BsonElement("notes")]
    public string? Notes { get; init; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; init; }
}
