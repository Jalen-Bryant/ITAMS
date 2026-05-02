using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class UserDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("username")]
    public string Username { get; init; } = string.Empty;

    [BsonElement("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; init; } = string.Empty;

    [BsonElement("normalizedUsername")]
    [BsonIgnoreIfNull]
    public string? NormalizedUsername { get; init; }

    [BsonElement("normalizedEmail")]
    [BsonIgnoreIfNull]
    public string? NormalizedEmail { get; init; }

    [BsonElement("passwordHash")]
    [BsonIgnoreIfNull]
    public string? PasswordHash { get; init; }

    [BsonElement("passwordChangedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? PasswordChangedAt { get; init; }

    [BsonElement("role")]
    public string Role { get; init; } = string.Empty;

    [BsonElement("department")]
    public string Department { get; init; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; init; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; init; }

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; init; }
}
