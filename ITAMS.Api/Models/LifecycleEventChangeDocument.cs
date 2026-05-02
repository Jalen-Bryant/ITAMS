using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class LifecycleEventChangeDocument
{
    [BsonElement("field")]
    public string Field { get; init; } = string.Empty;

    [BsonElement("old")]
    public string? OldValue { get; init; }

    [BsonElement("new")]
    public BsonValue NewValue { get; init; } = BsonString.Empty;
}
