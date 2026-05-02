using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class AuditLogDetailDocument
{
    [BsonElement("note")]
    public string? Note { get; init; }

    [BsonElement("result")]
    public string? Result { get; init; }
}
