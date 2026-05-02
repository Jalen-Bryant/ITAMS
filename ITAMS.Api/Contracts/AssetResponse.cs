namespace ITAMS.Api.Contracts;

public sealed class AssetResponse
{
    public string Id { get; init; } = string.Empty;

    public string AssetTag { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Department { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public DateTime PurchaseDate { get; init; }

    public DateTime WarrantyEndDate { get; init; }

    public DateTime EndOfLifeDate { get; init; }

    public AssetAssignmentResponse? CurrentAssignment { get; init; }

    public string? Notes { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}
