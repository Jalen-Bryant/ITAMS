using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class AssetAssignmentDocument
{
    [BsonElement("userId")]
    public ObjectId UserId { get; init; }

    [BsonElement("assignedOn")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime AssignedOn { get; init; }
}
