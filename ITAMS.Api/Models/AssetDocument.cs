using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ITAMS.Api.Models;

public sealed class AssetDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("assetTag")]
    public string AssetTag { get; init; } = string.Empty;

    [BsonElement("serialNumber")]
    public string SerialNumber { get; init; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; init; } = string.Empty;

    [BsonElement("manufacturer")]
    public string Manufacturer { get; init; } = string.Empty;

    [BsonElement("model")]
    public string Model { get; init; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; init; } = string.Empty;

    [BsonElement("department")]
    public string Department { get; init; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; init; } = string.Empty;

    [BsonElement("purchaseDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime PurchaseDate { get; init; }

    [BsonElement("warrantyEndDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime WarrantyEndDate { get; init; }

    [BsonElement("endOfLifeDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime EndOfLifeDate { get; init; }

    [BsonElement("currentAssignment")]
    public AssetAssignmentDocument? CurrentAssignment { get; init; }

    [BsonElement("notes")]
    public string? Notes { get; init; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; init; }

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; init; }
}
