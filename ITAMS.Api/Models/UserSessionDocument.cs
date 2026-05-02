using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class UserSessionDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("userId")]
    public ObjectId UserId { get; init; }

    [BsonElement("refreshTokenHash")]
    public string RefreshTokenHash { get; init; } = string.Empty;

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; init; }

    [BsonElement("expiresAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAt { get; init; }

    [BsonElement("revokedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? RevokedAt { get; init; }

    [BsonElement("createdByIp")]
    public string CreatedByIp { get; init; } = string.Empty;

    [BsonElement("userAgent")]
    public string UserAgent { get; init; } = string.Empty;
}
